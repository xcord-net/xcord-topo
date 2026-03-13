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
        string imageTag,
        CancellationToken ct = default)
    {
        if (_runningProcesses.ContainsKey(topologyId))
            throw new InvalidOperationException($"Image push is already running for topology {topologyId}");

        var channel = Channel.CreateUnbounded<TerraformOutputLine>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningProcesses[topologyId] = cts;
        _activeReaders[topologyId] = channel.Reader;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for registry to be reachable (DNS propagation + TLS cert provisioning)
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = "--- waiting for registry to be reachable ---",
                    IsError = false
                }, CancellationToken.None);

                var registryReady = await WaitForRegistryAsync(registryUrl, channel.Writer, cts.Token);
                if (!registryReady)
                {
                    await channel.Writer.WriteAsync(new TerraformOutputLine
                    {
                        Text = $"\n--- Image push exited with code 1 ---",
                        IsError = true
                    }, CancellationToken.None);
                    return;
                }

                // Docker login — use --password-stdin to avoid leaking password in process args
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = "--- docker login ---",
                    IsError = false
                }, CancellationToken.None);

                var loginExitCode = await RunDockerLoginAsync(registryUrl, registryUsername, registryPassword, channel.Writer, cts.Token);
                if (loginExitCode != 0)
                {
                    await channel.Writer.WriteAsync(new TerraformOutputLine
                    {
                        Text = $"\n--- Image push exited with code {loginExitCode} ---",
                        IsError = true
                    }, CancellationToken.None);
                    return;
                }

                var steps = new[]
                {
                    ($"tag xcord-hub:latest {registryUrl}/hub:{imageTag}", $"docker tag hub:{imageTag}"),
                    ($"push {registryUrl}/hub:{imageTag}", $"docker push hub:{imageTag}"),
                    ($"tag xcord-fed:latest {registryUrl}/fed:{imageTag}", $"docker tag fed:{imageTag}"),
                    ($"push {registryUrl}/fed:{imageTag}", $"docker push fed:{imageTag}"),
                };

                int lastExitCode = 0;
                foreach (var (arguments, stepName) in steps)
                {
                    if (cts.Token.IsCancellationRequested) break;

                    await channel.Writer.WriteAsync(new TerraformOutputLine
                    {
                        Text = $"--- {stepName} ---",
                        IsError = false
                    }, CancellationToken.None);

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

                    var outputTask = ReadStreamAsync(process.StandardOutput, false, channel.Writer, cts.Token);
                    var errorTask = ReadStreamAsync(process.StandardError, true, channel.Writer, cts.Token);

                    await Task.WhenAll(outputTask, errorTask);
                    await process.WaitForExitAsync(cts.Token);

                    lastExitCode = process.ExitCode;
                    if (process.ExitCode != 0)
                    {
                        await channel.Writer.WriteAsync(new TerraformOutputLine
                        {
                            Text = $"\n--- Image push exited with code {process.ExitCode} ---",
                            IsError = true
                        }, CancellationToken.None);
                        return;
                    }
                }

                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"\n--- Image push exited with code {lastExitCode} ---",
                    IsError = false
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = "\n--- Image push was cancelled ---",
                    IsError = true
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image push failed for topology {Id}", topologyId);
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"Error: {ex.Message}",
                    IsError = true
                }, CancellationToken.None);
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = "\n--- Image push exited with code 1 ---",
                    IsError = true
                }, CancellationToken.None);
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
                    await writer.WriteAsync(new TerraformOutputLine
                    {
                        Text = $"Registry reachable (HTTP {(int)response.StatusCode})",
                        IsError = false
                    }, ct);
                    return true;
                }

                await writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"Attempt {i}/{maxAttempts}: HTTP {(int)response.StatusCode}, retrying in {delaySeconds}s...",
                    IsError = false
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"Attempt {i}/{maxAttempts}: {ex.GetType().Name}: {ex.Message}, retrying in {delaySeconds}s...",
                    IsError = false
                }, ct);
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }

        await writer.WriteAsync(new TerraformOutputLine
        {
            Text = $"Registry not reachable after {maxAttempts * delaySeconds}s",
            IsError = true
        }, ct);
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
}
