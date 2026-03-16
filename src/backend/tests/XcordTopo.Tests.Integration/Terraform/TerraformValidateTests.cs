using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Integration.Terraform;

[Trait("Category", "Terraform")]
public sealed class TerraformValidateTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string? TerraformPath = FindTerraform();
    private readonly string _tempDir;
    private readonly MultiProviderHclGenerator _generator;

    public TerraformValidateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"xcord-topo-tf-validate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var registry = new ProviderRegistry([new AwsProvider(), new LinodeProvider()]);
        _generator = new MultiProviderHclGenerator(registry, DefaultPlugins.CreateRegistry());
    }

    [SkippableFact]
    public async Task ProductionSimple_GeneratesValidHcl()
    {
        Skip.If(TerraformPath is null, "terraform not installed");
        await AssertValidHcl("production-simple.json");
    }

    [SkippableFact]
    public async Task ProductionRobust_GeneratesValidHcl()
    {
        Skip.If(TerraformPath is null, "terraform not installed");
        await AssertValidHcl("production-robust.json");
    }

    [SkippableFact]
    public async Task ProductionElastic_GeneratesValidHcl()
    {
        Skip.If(TerraformPath is null, "terraform not installed");
        await AssertValidHcl("production-elastic.json");
    }

    private async Task AssertValidHcl(string fixtureName)
    {
        var topology = DeserializeFixture(fixtureName);
        var files = _generator.Generate(topology);

        // Write all .tf files to a temp directory
        var tfDir = Path.Combine(_tempDir, Path.GetFileNameWithoutExtension(fixtureName));
        Directory.CreateDirectory(tfDir);

        foreach (var (fileName, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(tfDir, fileName), content);
        }

        // terraform init -backend=false downloads provider schemas for validate
        var (initExit, initOutput) = await RunTerraform(tfDir, "init -backend=false -no-color");
        Assert.True(initExit == 0,
            $"terraform init failed for {fixtureName}:\n{initOutput}");

        // terraform validate checks HCL semantics
        var (validateExit, validateOutput) = await RunTerraform(tfDir, "validate -no-color");
        Assert.True(validateExit == 0,
            $"terraform validate failed for {fixtureName}:\n{validateOutput}");
    }

    private static async Task<(int ExitCode, string Output)> RunTerraform(string workDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = TerraformPath!,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, $"{stdout}\n{stderr}".Trim());
    }

    private static Topology DeserializeFixture(string name)
    {
        var assembly = typeof(TerraformValidateTests).Assembly;
        var resourceName = $"XcordTopo.Tests.Integration.Fixtures.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Fixture not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Topology>(json, JsonOptions)
            ?? throw new InvalidOperationException("Deserialized topology was null");
    }

    private static string? FindTerraform()
    {
        // Check common locations
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "terraform"),
            "/usr/local/bin/terraform",
            "/usr/bin/terraform"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // Fall back to PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "terraform",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrEmpty(result) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
