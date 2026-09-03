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

    /// <summary>
    /// Shared provider cache for `terraform init`.
    ///
    /// Every case here inits a brand new temp directory, so without a cache
    /// each one re-downloads the full provider schema over the network. That
    /// was the single most expensive thing in the whole test suite: three
    /// cases, ~116s, essentially all of it download. Terraform reuses anything
    /// already in TF_PLUGIN_CACHE_DIR, so the first init pays once and every
    /// later init - in this run and in subsequent runs - links against it.
    /// </summary>
    private static readonly string PluginCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".terraform.d", "plugin-cache");

    /// <summary>
    /// Terraform's plugin cache is explicitly not safe for concurrent use: two
    /// inits populating it at once can leave a provider half-written and fail
    /// the later one. The cases above are independent and xunit runs them in
    /// parallel, so inits queue here. Only init touches the cache - validate
    /// runs unguarded, so the serialisation costs a download, not a test run.
    /// </summary>
    private static readonly SemaphoreSlim InitGate = new(1, 1);

    private static async Task<(int ExitCode, string Output)> RunTerraform(string workDir, string args)
    {
        Directory.CreateDirectory(PluginCacheDir);

        var isInit = args.StartsWith("init", StringComparison.Ordinal);
        if (isInit)
        {
            await InitGate.WaitAsync();
        }

        try
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
            psi.Environment["TF_PLUGIN_CACHE_DIR"] = PluginCacheDir;
            // A cached provider is not recorded in the lock file the way a fresh
            // download is; without this, init refuses to use the cache at all.
            psi.Environment["TF_PLUGIN_CACHE_MAY_BREAK_DEPENDENCY_LOCK_FILE"] = "true";

            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, $"{stdout}\n{stderr}".Trim());
        }
        finally
        {
            if (isInit)
            {
                InitGate.Release();
            }
        }
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
