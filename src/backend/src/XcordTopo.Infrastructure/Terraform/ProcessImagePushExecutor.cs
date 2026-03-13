using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Terraform;

public sealed class ProcessImagePushExecutor : IImagePushExecutor
{
    // Docker CLI config dir — writable regardless of which UID the container runs as
    private static readonly string DockerConfigDir = Path.Combine(Path.GetTempPath(), ".docker");

    private readonly ILogger<ProcessImagePushExecutor> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningProcesses = new();
    private readonly ConcurrentDictionary<Guid, ChannelReader<TerraformOutputLine>> _activeReaders = new();

    public ProcessImagePushExecutor(ILogger<ProcessImagePushExecutor> logger)
    {
        _logger = logger;
    }

    public Task<ChannelReader<TerraformOutputLine>> ExecuteAsync(
        Guid topologyId,
        string registryUrl,
        string registryUsername,
        string registryPassword,
        IReadOnlyList<ImageBuildSpec> images,
        CancellationToken ct = default)
    {
        if (_runningProcesses.ContainsKey(topologyId))
            throw new InvalidOperationException($"Image build/push is already running for topology {topologyId}");

        var channel = Channel.CreateUnbounded<TerraformOutputLine>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningProcesses[topologyId] = cts;
        _activeReaders[topologyId] = channel.Reader;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for registry to be reachable (DNS propagation + TLS cert provisioning)
                await WriteLineAsync(channel.Writer, "--- waiting for registry to be reachable ---");

                var registryReady = await WaitForRegistryAsync(registryUrl, channel.Writer, cts.Token);
                if (!registryReady)
                {
                    await WriteLineAsync(channel.Writer, "\n--- Build & push exited with code 1 ---", isError: true);
                    return;
                }

                // Docker login — use --password-stdin to avoid leaking password in process args
                await WriteLineAsync(channel.Writer, "--- docker login ---");

                var loginExitCode = await RunDockerLoginAsync(registryUrl, registryUsername, registryPassword, channel.Writer, cts.Token);
                if (loginExitCode != 0)
                {
                    await WriteLineAsync(channel.Writer, $"\n--- Build & push exited with code {loginExitCode} ---", isError: true);
                    return;
                }

                // Build and push each image
                foreach (var image in images)
                {
                    if (cts.Token.IsCancellationRequested) break;

                    var fullTag = $"{registryUrl}/{image.RegistryName}:{image.GitRef}";

                    // Build from git URL — Docker daemon clones the repo at the specified ref
                    await WriteLineAsync(channel.Writer, $"--- docker build {image.RegistryName}:{image.GitRef} ---");

                    var buildExitCode = await RunDockerCommandAsync(
                        $"build --build-arg VERSION={image.GitRef} -t {fullTag} {image.RepoUrl}#{image.GitRef}",
                        channel.Writer, cts.Token);

                    if (buildExitCode != 0)
                    {
                        await WriteLineAsync(channel.Writer, $"\n--- Build & push exited with code {buildExitCode} ---", isError: true);
                        return;
                    }

                    // Push to registry
                    await WriteLineAsync(channel.Writer, $"--- docker push {image.RegistryName}:{image.GitRef} ---");

                    var pushExitCode = await RunDockerCommandAsync(
                        $"push {fullTag}",
                        channel.Writer, cts.Token);

                    if (pushExitCode != 0)
                    {
                        await WriteLineAsync(channel.Writer, $"\n--- Build & push exited with code {pushExitCode} ---", isError: true);
                        return;
                    }
                }

                await WriteLineAsync(channel.Writer, "\n--- Build & push exited with code 0 ---");
            }
            catch (OperationCanceledException)
            {
                await WriteLineAsync(channel.Writer, "\n--- Build & push was cancelled ---", isError: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image build/push failed for topology {Id}", topologyId);
                await WriteLineAsync(channel.Writer, $"Error: {ex.Message}", isError: true);
                await WriteLineAsync(channel.Writer, "\n--- Build & push exited with code 1 ---", isError: true);
            }
            finally
            {
                channel.Writer.Complete();
                _runningProcesses.TryRemove(topologyId, out _);
                // Don't remove reader here — the SSE stream handler needs to read buffered output
                // even after the process exits. The reader is cleaned up by ReleaseOutputStream().
            }
        }, CancellationToken.None);

        return Task.FromResult<ChannelReader<TerraformOutputLine>>(channel.Reader);
    }

    public ChannelReader<TerraformOutputLine>? GetOutputStream(Guid topologyId) =>
        _activeReaders.GetValueOrDefault(topologyId);

    public void ReleaseOutputStream(Guid topologyId) =>
        _activeReaders.TryRemove(topologyId, out _);

    public bool IsRunning(Guid topologyId) => _runningProcesses.ContainsKey(topologyId);

    public void Cancel(Guid topologyId)
    {
        if (_runningProcesses.TryGetValue(topologyId, out var cts))
        {
            cts.Cancel();
            // Don't dispose here — let the finally block in the task handle cleanup
        }
    }

    private async Task<int> RunDockerCommandAsync(
        string arguments,
        ChannelWriter<TerraformOutputLine> writer,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["DOCKER_CONFIG"] = DockerConfigDir;

        using var process = Process.Start(psi)!;

        var outputTask = ReadStreamAsync(process.StandardOutput, false, writer, ct);
        var errorTask = ReadStreamAsync(process.StandardError, true, writer, ct);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(ct);

        return process.ExitCode;
    }

    private static async Task<bool> WaitForRegistryAsync(
        string registryUrl,
        ChannelWriter<TerraformOutputLine> writer,
        CancellationToken ct)
    {
        // Registry may need time for DNS propagation + Caddy TLS cert provisioning
        const int maxAttempts = 30;
        const int delaySeconds = 10;
        var url = registryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? $"{registryUrl}/v2/"
            : $"https://{registryUrl}/v2/";

        // Accept self-signed or Let's Encrypt staging certs during initial provisioning
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        for (var i = 1; i <= maxAttempts; i++)
        {
            try
            {
                var response = await client.GetAsync(url, ct);
                // 200 or 401 (auth required) both mean registry is up
                if ((int)response.StatusCode is 200 or 401)
                {
                    await WriteLineAsync(writer, $"Registry reachable (HTTP {(int)response.StatusCode})");
                    return true;
                }

                await WriteLineAsync(writer, $"Attempt {i}/{maxAttempts}: HTTP {(int)response.StatusCode}, retrying in {delaySeconds}s...");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await WriteLineAsync(writer, $"Attempt {i}/{maxAttempts}: {ex.GetType().Name}: {ex.Message}, retrying in {delaySeconds}s...");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }

        await WriteLineAsync(writer, $"Registry not reachable after {maxAttempts * delaySeconds}s", isError: true);
        return false;
    }

    private static async Task<int> RunDockerLoginAsync(
        string registryUrl,
        string username,
        string password,
        ChannelWriter<TerraformOutputLine> writer,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"login -u {username} --password-stdin {registryUrl}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["DOCKER_CONFIG"] = DockerConfigDir;

        using var process = Process.Start(psi)!;

        // Write password to stdin and close it
        await process.StandardInput.WriteAsync(password);
        process.StandardInput.Close();

        var outputTask = ReadStreamAsync(process.StandardOutput, false, writer, ct);
        var errorTask = ReadStreamAsync(process.StandardError, true, writer, ct);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(ct);

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(
        System.IO.StreamReader reader,
        bool isError,
        ChannelWriter<TerraformOutputLine> writer,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            await writer.WriteAsync(new TerraformOutputLine
            {
                Text = line,
                IsError = isError
            }, ct);
        }
    }

    private static Task WriteLineAsync(ChannelWriter<TerraformOutputLine> writer, string text, bool isError = false) =>
        writer.WriteAsync(new TerraformOutputLine { Text = text, IsError = isError }, CancellationToken.None).AsTask();
}
