using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Tests.Integration.Terraform;

public sealed class ProcessTerraformExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProcessTerraformExecutor _executor;

    public ProcessTerraformExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"xcord-topo-exec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dataOptions = Options.Create(new DataOptions { BasePath = _tempDir });
        var hclFileManager = new HclFileManager(dataOptions, NullLogger<HclFileManager>.Instance);
        _executor = new ProcessTerraformExecutor(hclFileManager, dataOptions, NullLogger<ProcessTerraformExecutor>.Instance);
    }

    [Fact]
    public async Task Execute_WhenProcessFails_ChannelStillReadableAfterCompletion()
    {
        // Validates the race condition fix: after a process completes, the channel
        // reader must remain available for the SSE stream handler to consume output.
        var binDir = Path.Combine(_tempDir, "bin-fails");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/bash\necho 'Error: init failed' >&2\nexit 1\n");
        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();

            var reader = await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            // Wait briefly for the background task to complete the channel
            await Task.Delay(500);

            // The key assertion: GetOutputStream still returns the reader even after
            // the process has exited (the race condition fix)
            var streamReader = _executor.GetOutputStream(topologyId);
            Assert.NotNull(streamReader);

            // Read all buffered output - should contain at least the exit code sentinel
            var lines = new List<TerraformOutputLine>();
            await foreach (var line in streamReader!.ReadAllAsync(CancellationToken.None))
            {
                lines.Add(line);
            }

            Assert.NotEmpty(lines);
            // Verify exit code sentinel is present (format: "--- Terraform {cmd} exited with code {n} ---")
            Assert.Contains(lines, l => l.Text.Contains("exited with code"));

            // After consuming, ReleaseOutputStream cleans up
            _executor.ReleaseOutputStream(topologyId);
            Assert.Null(_executor.GetOutputStream(topologyId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task Execute_WhenProcessFails_IsRunningReturnsFalseAfterCompletion()
    {
        var binDir = Path.Combine(_tempDir, "bin-isrunning");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/bash\nexit 1\n");
        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();

            await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            // Wait for background task to complete
            await Task.Delay(500);

            Assert.False(_executor.IsRunning(topologyId));
            _executor.ReleaseOutputStream(topologyId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task Execute_ConcurrentExecution_ThrowsInvalidOperation()
    {
        // Use a long-running fake terraform so the process is still running
        // when we attempt the second execution
        var binDir = Path.Combine(_tempDir, "bin-concurrent");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/bash\nsleep 30\n");
        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();
            await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            // Process should be running
            Assert.True(_executor.IsRunning(topologyId));

            // Second execution for the same topology should throw
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _executor.ExecuteAsync(
                    topologyId, TerraformCommand.Plan, new List<string>(), ct: CancellationToken.None));

            _executor.Cancel(topologyId);
            _executor.ReleaseOutputStream(topologyId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void GetOutputStream_NoExecution_ReturnsNull()
    {
        Assert.Null(_executor.GetOutputStream(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReleaseOutputStream_Idempotent()
    {
        var binDir = Path.Combine(_tempDir, "bin-release");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/bash\nexit 0\n");
        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();

            await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            await Task.Delay(500);

            _executor.ReleaseOutputStream(topologyId);
            _executor.ReleaseOutputStream(topologyId); // Should not throw
            Assert.Null(_executor.GetOutputStream(topologyId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task Cancel_ActiveExecution_WritesCancelledMessage()
    {
        var binDir = Path.Combine(_tempDir, "bin-cancel");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/bash\nsleep 30\n");
        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();

            var reader = await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            // Cancel immediately
            _executor.Cancel(topologyId);

            // Read output - should contain either cancellation or error message
            var lines = new List<TerraformOutputLine>();
            await foreach (var line in reader.ReadAllAsync(CancellationToken.None))
            {
                lines.Add(line);
            }

            Assert.NotEmpty(lines);
            _executor.ReleaseOutputStream(topologyId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task Execute_WithBashScript_StreamsOutputCorrectly()
    {
        // Create a fake "terraform" script in a temp bin dir and prepend to PATH
        var binDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, """
            #!/bin/bash
            echo "Initializing providers..."
            echo "Provider: local"
            echo "Init complete."
            echo "" >&2
            echo "Warning: no backend configured" >&2
            exit 0
            """);

        // Make executable
        await SetExecutableAsync(scriptPath);

        // Override PATH so the executor finds our fake terraform
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();
            var reader = await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Init, new List<string>(), ct: CancellationToken.None);

            var lines = new List<TerraformOutputLine>();
            await foreach (var line in reader.ReadAllAsync(CancellationToken.None))
            {
                lines.Add(line);
            }

            // Should have stdout lines
            Assert.Contains(lines, l => l.Text.Contains("Initializing providers"));
            Assert.Contains(lines, l => l.Text.Contains("Init complete"));

            // Should have stderr lines marked as errors
            Assert.Contains(lines, l => l.IsError && l.Text.Contains("no backend configured"));

            // Should have exit code sentinel
            Assert.Contains(lines, l => l.Text.Contains("exited with code 0"));

            // Process should no longer be running
            Assert.False(_executor.IsRunning(topologyId));

            // Reader should still be available (race condition fix)
            Assert.NotNull(_executor.GetOutputStream(topologyId));
            _executor.ReleaseOutputStream(topologyId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task Execute_WithFailingScript_ReportsNonZeroExitCode()
    {
        var binDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(binDir);

        var scriptPath = Path.Combine(binDir, "terraform");
        await File.WriteAllTextAsync(scriptPath, """
            #!/bin/bash
            echo "Error: resource not found" >&2
            exit 1
            """);

        await SetExecutableAsync(scriptPath);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{binDir}:{originalPath}");

            var topologyId = Guid.NewGuid();
            var reader = await _executor.ExecuteAsync(
                topologyId, TerraformCommand.Apply, new List<string>(), ct: CancellationToken.None);

            var lines = new List<TerraformOutputLine>();
            await foreach (var line in reader.ReadAllAsync(CancellationToken.None))
            {
                lines.Add(line);
            }

            // Exit code sentinel should indicate failure
            var exitLine = lines.FirstOrDefault(l => l.Text.Contains("exited with code"));
            Assert.NotNull(exitLine);
            Assert.Contains("exited with code 1", exitLine!.Text);
            Assert.True(exitLine.IsError);

            _executor.ReleaseOutputStream(topologyId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private static async Task SetExecutableAsync(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
