using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Terraform;

public sealed class ProcessTerraformExecutor : ITerraformExecutor
{
    private readonly IHclFileManager _hclFileManager;
    private readonly string _credentialsBasePath;
    private readonly ILogger<ProcessTerraformExecutor> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningProcesses = new();
    private readonly ConcurrentDictionary<Guid, ChannelReader<TerraformOutputLine>> _activeReaders = new();

    public ProcessTerraformExecutor(
        IHclFileManager hclFileManager,
        IOptions<DataOptions> dataOptions,
        ILogger<ProcessTerraformExecutor> logger)
    {
        _hclFileManager = hclFileManager;
        _credentialsBasePath = Path.Combine(dataOptions.Value.BasePath, "credentials");
        _logger = logger;
    }

    public Task<ChannelReader<TerraformOutputLine>> ExecuteAsync(
        Guid topologyId,
        TerraformCommand command,
        string providerKey = "linode",
        CancellationToken ct = default)
    {
        if (_runningProcesses.ContainsKey(topologyId))
            throw new InvalidOperationException($"Terraform is already running for topology {topologyId}");

        var channel = Channel.CreateUnbounded<TerraformOutputLine>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runningProcesses[topologyId] = cts;
        _activeReaders[topologyId] = channel.Reader;

        var workDir = _hclFileManager.GetTerraformDirectory(topologyId);
        var args = command switch
        {
            TerraformCommand.Init => "init -no-color",
            TerraformCommand.Plan => "plan -no-color -input=false",
            TerraformCommand.Apply => "apply -no-color -input=false -auto-approve",
            TerraformCommand.Destroy => "destroy -no-color -input=false -auto-approve",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

        var credentialsFile = Path.Combine(_credentialsBasePath, $"{providerKey}.tfvars");
        if (File.Exists(credentialsFile) && command != TerraformCommand.Init)
        {
            args += $" -var-file=\"{credentialsFile}\"";
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "terraform",
                    Arguments = args,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi)!;

                var outputTask = ReadStreamAsync(process.StandardOutput, false, channel.Writer, cts.Token);
                var errorTask = ReadStreamAsync(process.StandardError, true, channel.Writer, cts.Token);

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(cts.Token);

                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"\n--- Terraform {command} exited with code {process.ExitCode} ---",
                    IsError = process.ExitCode != 0
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"\n--- Terraform {command} was cancelled ---",
                    IsError = true
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Terraform execution failed for topology {Id}", topologyId);
                await channel.Writer.WriteAsync(new TerraformOutputLine
                {
                    Text = $"Error: {ex.Message}",
                    IsError = true
                }, CancellationToken.None);
            }
            finally
            {
                channel.Writer.Complete();
                _runningProcesses.TryRemove(topologyId, out _);
                _activeReaders.TryRemove(topologyId, out _);
            }
        }, CancellationToken.None);

        return Task.FromResult<ChannelReader<TerraformOutputLine>>(channel.Reader);
    }

    public ChannelReader<TerraformOutputLine>? GetOutputStream(Guid topologyId) =>
        _activeReaders.GetValueOrDefault(topologyId);

    public bool IsRunning(Guid topologyId) => _runningProcesses.ContainsKey(topologyId);

    public void Cancel(Guid topologyId)
    {
        if (_runningProcesses.TryRemove(topologyId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
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
