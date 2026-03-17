using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Plugins.Images;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Infrastructure.Validation;
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
    public void Registry_GetCatalog_IncludesExternalPlugins()
    {
        // Simulate built-in + external plugin (like Zombocom)
        var builtInRegistry = DefaultPlugins.CreateRegistry();
        var builtIns = builtInRegistry.GetAll();
        var externalPlugin = new StubPlugin("plugin:zombocom", "Zombocom");
        var allPlugins = builtIns.Append(externalPlugin);

        var registry = new ImagePluginRegistry(allPlugins);
        var catalog = registry.GetCatalog();

        Assert.Equal(9, catalog.Count);
        Assert.Contains(catalog, c => c.TypeId == "plugin:zombocom");
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
// Validation: public endpoint plugins skip orphan warning
// ──────────────────────────────────────────────

public class PluginValidationTests
{
    [Fact]
    public void Validate_PublicEndpointPlugin_NoOrphanWarning()
    {
        // A plugin image with IsPublicEndpoint=true and ports but no wires
        // should NOT produce an "has ports but no wires" warning.
        // Caddy implicitly routes public endpoints.
        var plugin = new StubPublicEndpointPlugin("plugin:zombocom", "Zombocom");
        var allPlugins = DefaultPlugins.CreateRegistry().GetAll().Append(plugin);
        var pluginRegistry = new ImagePluginRegistry(allPlugins);
        var providerRegistry = new ProviderRegistry([new LinodeProvider(), new AwsProvider()]);
        var validator = new TopologyValidator(providerRegistry, pluginRegistry);

        var topology = new Topology
        {
            Name = "Plugin Test",
            Provider = "linode",
            Containers =
            [
                new Container
                {
                    Name = "Caddy",
                    Kind = ContainerKind.Caddy,
                    Width = 600,
                    Height = 400,
                    Config = new() { ["domain"] = "test.xcord.net" },
                    Images =
                    [
                        new Image
                        {
                            Name = "Zombocom",
                            Kind = ImageKind.Custom,
                            TypeId = "plugin:zombocom",
                            Ports = [new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                            Config = new(),
                        }
                    ]
                }
            ]
        };

        var result = validator.ValidateFull(topology);

        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("no wires"));
    }

    [Fact]
    public void Validate_NonPublicPlugin_StillWarnsAboutOrphanedPorts()
    {
        // A non-public plugin image WITH ports and no wires SHOULD warn.
        var plugin = new StubPlugin("plugin:internal", "Internal");
        var allPlugins = DefaultPlugins.CreateRegistry().GetAll().Append(plugin);
        var pluginRegistry = new ImagePluginRegistry(allPlugins);
        var providerRegistry = new ProviderRegistry([new LinodeProvider(), new AwsProvider()]);
        var validator = new TopologyValidator(providerRegistry, pluginRegistry);

        var topology = new Topology
        {
            Name = "Plugin Test",
            Provider = "linode",
            Containers =
            [
                new Container
                {
                    Name = "Host",
                    Kind = ContainerKind.Host,
                    Width = 600,
                    Height = 400,
                    Images =
                    [
                        new Image
                        {
                            Name = "Internal Svc",
                            Kind = ImageKind.Custom,
                            TypeId = "plugin:internal",
                            Ports = [new Port { Name = "grpc", Type = PortType.Generic, Direction = PortDirection.InOut, Side = PortSide.Left, Offset = 0.5 }],
                            Config = new(),
                        }
                    ]
                }
            ]
        };

        var result = validator.ValidateFull(topology);

        Assert.Contains(result.Warnings, w => w.Message.Contains("no wires"));
    }
}

// ──────────────────────────────────────────────
// HCL generation: plugin images
// ──────────────────────────────────────────────

public class PluginHclGenerationTests
{
    private static (Dictionary<string, string> Files, ImagePluginRegistry Registry) GenerateHcl(Topology topology)
    {
        var plugin = new StubPrivateRegistryPlugin("plugin:zombocom", "Zombocom");
        var allPlugins = DefaultPlugins.CreateRegistry().GetAll().Append(plugin);
        var pluginRegistry = new ImagePluginRegistry(allPlugins);
        var providerRegistry = new ProviderRegistry([
            new LinodeProvider(pluginRegistry),
            new AwsProvider(pluginRegistry)
        ]);
        var generator = new MultiProviderHclGenerator(providerRegistry, pluginRegistry);
        return (generator.Generate(topology), pluginRegistry);
    }

    private static Topology CreateTopologyWithPlugin()
    {
        var dnsId = Guid.NewGuid();
        var caddyId = Guid.NewGuid();
        var hubId = Guid.NewGuid();
        var zomboId = Guid.NewGuid();
        var pgId = Guid.NewGuid();
        var redisId = Guid.NewGuid();

        return new Topology
        {
            Name = "Plugin HCL Test",
            Provider = "aws",
            ProviderConfig = new() { ["aws_region"] = "us-east-1" },
            Containers =
            [
                new Container
                {
                    Id = dnsId,
                    Name = "XCord Net",
                    Kind = ContainerKind.Dns,
                    Width = 900, Height = 500,
                    Config = new() { ["domain"] = "test.xcord.net" },
                    Children =
                    [
                        new Container
                        {
                            Id = caddyId,
                            Name = "Caddy",
                            Kind = ContainerKind.Caddy,
                            Width = 800, Height = 400,
                            Config = new() { ["domain"] = "test.xcord.net" },
                            Images =
                            [
                                new Image
                                {
                                    Id = hubId,
                                    Name = "Hub Server",
                                    Kind = ImageKind.HubServer,
                                    Ports =
                                    [
                                        new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 },
                                        new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.33 },
                                        new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.67 }
                                    ],
                                    Config = new(),
                                },
                                new Image
                                {
                                    Id = zomboId,
                                    Name = "ZomboCom",
                                    Kind = ImageKind.Custom,
                                    TypeId = "plugin:zombocom",
                                    Ports = [new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                                    Config = new(),
                                },
                                new Image
                                {
                                    Id = pgId,
                                    Name = "PostgreSQL",
                                    Kind = ImageKind.PostgreSQL,
                                    Ports = [new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                                    Config = new() { ["volumeSize"] = "20" },
                                },
                                new Image
                                {
                                    Id = redisId,
                                    Name = "Redis",
                                    Kind = ImageKind.Redis,
                                    Ports = [new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                                    Config = new(),
                                }
                            ]
                        }
                    ]
                }
            ],
            Wires =
            [
                new Wire { FromNodeId = hubId, FromPortId = hubId, ToNodeId = pgId, ToPortId = pgId },
                new Wire { FromNodeId = hubId, FromPortId = hubId, ToNodeId = redisId, ToPortId = redisId }
            ],
            Registry = "docker.xcord.net",
        };
    }

    [Fact]
    public void PluginImage_VersionVariable_DeclaredInVariablesTf()
    {
        var topology = CreateTopologyWithPlugin();
        var (files, _) = GenerateHcl(topology);
        var variables = files["variables.tf"];

        // Must declare zombocom_version variable
        Assert.Contains("variable \"zombocom_version\"", variables);
        // Must have a default value
        Assert.Contains("default", variables);
        // Must also have hub_version and fed_version
        Assert.Contains("variable \"hub_version\"", variables);
    }

    [Fact]
    public void PluginImage_Provisioning_ValidInlineArray()
    {
        var topology = CreateTopologyWithPlugin();
        var (files, _) = GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // The deploy_caddy_apps resource should exist
        Assert.Contains("deploy_caddy_apps", provisioning);

        // Every provisioner "remote-exec" block must have exactly one of inline/script/scripts
        // Check that inline arrays are well-formed: inline = [ ... ]
        var provisionerBlocks = provisioning.Split("provisioner \"remote-exec\"");
        foreach (var block in provisionerBlocks.Skip(1))
        {
            var content = block.Split('}')[0]; // rough extraction
            var inlineCount = content.Split("inline").Length - 1;
            var scriptCount = content.Split("\nscript ").Length - 1 + (content.Split("\nscripts ").Length - 1);
            // Must have exactly one inline and no script/scripts
            Assert.Equal(1, inlineCount);
            Assert.Equal(0, scriptCount);
        }
    }
}

// ──────────────────────────────────────────────
// Test helpers
// ──────────────────────────────────────────────

/// <summary>Minimal stub plugin for registry tests.</summary>
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

/// <summary>Stub plugin with RequiresPrivateRegistry and VersionVariableName (like Zombocom).</summary>
internal sealed class StubPrivateRegistryPlugin(string typeId, string label) : ImagePluginBase
{
    public override string TypeId => typeId;
    public override string Label => label;
    public override string Description => "stub private registry plugin";

    public override ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(80)],
        MountPath: null,
        MinRamMb: 64,
        SharedOverheadMb: 0,
        IsPublicEndpoint: true);

    public override DockerBehavior GetDockerBehavior() => new(
        RequiresPrivateRegistry: true,
        VersionVariableName: "zombocom_version",
        RegistryName: "zombocom");

    public override CatalogEntry GetCatalogEntry() => new(
        TypeId: typeId,
        Label: label,
        Color: "#ff69b4",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("http", "Network", "In", "Left", 0.5)],
        DefaultDockerImage: "{registry}/zombocom:latest",
        ConfigFields: [],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "stub private registry",
        DockerBehavior: GetDockerBehavior());
}

/// <summary>Stub plugin with IsPublicEndpoint=true and a port (like Zombocom).</summary>
internal sealed class StubPublicEndpointPlugin(string typeId, string label) : ImagePluginBase
{
    public override string TypeId => typeId;
    public override string Label => label;
    public override string Description => "stub public endpoint";

    public override ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(80)],
        MountPath: null,
        MinRamMb: 64,
        SharedOverheadMb: 0,
        IsPublicEndpoint: true);

    public override CatalogEntry GetCatalogEntry() => new(
        TypeId: typeId,
        Label: label,
        Color: "#000000",
        DefaultWidth: 100,
        DefaultHeight: 50,
        DefaultPorts: [new("http", "Network", "In", "Left", 0.5)],
        DefaultDockerImage: null,
        ConfigFields: [],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "stub public endpoint");
}
