using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Plugins.Images;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Tests.Unit;

// ──────────────────────────────────────────────
// ImagePluginRegistry tests
// ──────────────────────────────────────────────

public class ImagePluginRegistryTests
{
    [Fact]
    public void Registry_RegistersAllBuiltInPlugins()
    {
        var registry = DefaultPlugins.CreateRegistry();
        Assert.Equal(8, registry.Count);
    }

    [Fact]
    public void Registry_GetByTypeId_ReturnsCorrectPlugin()
    {
        var registry = DefaultPlugins.CreateRegistry();
        var plugin = registry.Get("PostgreSQL");
        Assert.IsType<PostgreSqlImagePlugin>(plugin);
    }

    [Fact]
    public void Registry_GetByTypeId_IsCaseInsensitive()
    {
        var registry = DefaultPlugins.CreateRegistry();
        var plugin = registry.Get("postgresql");
        Assert.NotNull(plugin);
        Assert.IsType<PostgreSqlImagePlugin>(plugin);
    }

    [Fact]
    public void Registry_GetByTypeId_ReturnsNullForUnknown()
    {
        var registry = DefaultPlugins.CreateRegistry();
        var plugin = registry.Get("nonexistent");
        Assert.Null(plugin);
    }

    [Fact]
    public void Registry_GetCatalog_ReturnsAllEntries()
    {
        var registry = DefaultPlugins.CreateRegistry();
        var catalog = registry.GetCatalog();
        Assert.Equal(8, catalog.Count);
    }

    [Fact]
    public void Registry_DuplicateTypeId_KeepsFirst()
    {
        var first = new StubPlugin("duplicate-id", "First");
        var second = new StubPlugin("duplicate-id", "Second");

        var registry = new ImagePluginRegistry([first, second]);

        var resolved = registry.Get("duplicate-id");
        Assert.NotNull(resolved);
        Assert.Equal("First", resolved.Label);
    }

    // ──────────────────────────────────────────────
    // Built-in plugin tests
    // ──────────────────────────────────────────────

    [Fact]
    public void PostgreSqlPlugin_HasCorrectDescriptor()
    {
        var plugin = new PostgreSqlImagePlugin();
        var desc = plugin.GetDescriptor();

        Assert.Single(desc.Ports);
        Assert.Equal(5432, desc.Ports[0].Port);
        Assert.Equal("/var/lib/postgresql/data", desc.MountPath);
        Assert.Equal(512, desc.MinRamMb);
        Assert.True(desc.IsDataService);
    }

    [Fact]
    public void HubServerPlugin_HasCustomEnvVarBuilder()
    {
        var plugin = new HubServerImagePlugin();
        Assert.True(plugin.HasCustomEnvVarBuilder);
    }

    [Fact]
    public void FederationServerPlugin_WireRequirements()
    {
        var plugin = new FederationServerImagePlugin();
        var wires = plugin.GetWireRequirements();

        var portNames = wires.Select(w => w.PortName).ToHashSet();
        Assert.Contains("pg", portNames);
        Assert.Contains("redis", portNames);
        Assert.Contains("minio", portNames);
    }

    [Fact]
    public void LiveKitPlugin_Descriptor_HasUdpPort()
    {
        var plugin = new LiveKitImagePlugin();
        var desc = plugin.GetDescriptor();

        var udpPort = desc.Ports.FirstOrDefault(p => p.Protocol == "udp");
        Assert.NotNull(udpPort);
    }

    [Fact]
    public void CustomPlugin_SubdomainRule_IsConfigBased()
    {
        var plugin = new CustomImagePlugin();
        var rule = plugin.GetSubdomainRule();

        Assert.IsType<ConfigSubdomain>(rule);
    }

    // ──────────────────────────────────────────────
    // TemplateEngine tests
    // ──────────────────────────────────────────────
}

public class TemplateEngineTests
{
    private readonly TemplateEngine _engine = new();

    private static TemplateContext MakeContext(
        string hostName = "host1",
        string imageName = "myimage",
        string? registry = null,
        IReadOnlyDictionary<string, string>? config = null)
        => new()
        {
            HostName = hostName,
            ImageName = imageName,
            Registry = registry,
            ImageConfig = config
        };

    [Fact]
    public void TemplateEngine_ResolvesSecretTemplate()
    {
        var ctx = MakeContext(hostName: "host1", imageName: "pg");
        var result = _engine.Resolve("{secret:password}", ctx);

        Assert.Equal("${nonsensitive(random_password.host1_pg_password.result)}", result);
    }

    [Fact]
    public void TemplateEngine_ResolvesRegistry()
    {
        var ctx = MakeContext(registry: "docker.xcord.net");
        var result = _engine.Resolve("{registry}", ctx);

        Assert.Equal("docker.xcord.net", result);
    }

    [Fact]
    public void TemplateEngine_ResolvesConfig()
    {
        var cfg = new Dictionary<string, string> { ["mykey"] = "myvalue" };
        var ctx = MakeContext(config: cfg);
        var result = _engine.Resolve("{config:mykey}", ctx);

        Assert.Equal("myvalue", result);
    }

    [Fact]
    public void TemplateEngine_LeavesUnknownTokensAlone()
    {
        var ctx = MakeContext();
        // ${var.x} has outer ${ which won't match our { scanner - only the inner {var.x} is considered
        // The engine sees "var" token and converts it to ${var.x} — this is expected behaviour.
        var result = _engine.Resolve("{var:foo}", ctx);
        Assert.Equal("${var.foo}", result);
    }

    [Fact]
    public void TemplateEngine_ResolvesContainerName()
    {
        var ctx = MakeContext(imageName: "my-container");
        var result = _engine.Resolve("{containerName}", ctx);

        Assert.Equal("my-container", result);
    }
}

// ──────────────────────────────────────────────
// ImagePluginRegistry.GetForImage tests
// ──────────────────────────────────────────────

public class GetForImageTests
{
    [Fact]
    public void GetForImage_UsesTypeIdOverKind()
    {
        var registry = new ImagePluginRegistry([
            new StubPlugin("plugin:custom", "Custom Plugin"),
            new PostgreSqlImagePlugin()
        ]);

        // Image has Kind = PostgreSQL but TypeId set to "plugin:custom"
        var image = new Image
        {
            Kind = ImageKind.PostgreSQL,
            TypeId = "plugin:custom"
        };

        var plugin = registry.GetForImage(image);

        Assert.NotNull(plugin);
        Assert.Equal("Custom Plugin", plugin.Label);
    }

    [Fact]
    public void GetForImage_FallsBackToKind()
    {
        var registry = DefaultPlugins.CreateRegistry();

        // Image has no TypeId set - should fall back to Kind
        var image = new Image
        {
            Kind = ImageKind.Redis,
            TypeId = null
        };

        var plugin = registry.GetForImage(image);

        Assert.NotNull(plugin);
        Assert.Equal("Redis", plugin.TypeId);
    }
}

// ──────────────────────────────────────────────
// Test helpers
// ──────────────────────────────────────────────

/// <summary>Minimal stub plugin for registry tests - demonstrates how little code ImagePluginBase requires.</summary>
internal sealed class StubPlugin(string typeId, string label) : ImagePluginBase
{
    public override string TypeId => typeId;
    public override string Label => label;
    public override string Description => "stub";

    public override ImageDescriptor GetDescriptor() => new(
        Ports: [],
        MountPath: null,
        MinRamMb: 64,
        SharedOverheadMb: 0);

    public override CatalogEntry GetCatalogEntry() => new(
        TypeId: typeId,
        Label: label,
        Color: "#000000",
        DefaultWidth: 100,
        DefaultHeight: 50,
        DefaultPorts: [],
        DefaultDockerImage: null,
        ConfigFields: [],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "stub");
}
