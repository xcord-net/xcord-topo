using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class RegistryImagePlugin : IImagePlugin
{
    public string TypeId => "Registry";
    public string Label => "Docker Registry";
    public string Description => "Private Docker registry with auto-TLS via Caddy";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(5000)],
        MountPath: "/var/lib/registry",
        MinRamMb: 256,
        SharedOverheadMb: 0,
        DefaultDockerImage: "registry:2",
        IsPublicEndpoint: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() => [];
    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];
    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new DerivedSubdomain();

    public BackupDefinition? GetBackupDefinition() => null;
    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => null;

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "Registry",
        Label: "Docker Registry",
        Color: "#2ac3de",
        DefaultWidth: 140,
        DefaultHeight: 60,
        DefaultPorts: [],
        DefaultDockerImage: "registry:2",
        ConfigFields:
        [
            new("domain", "Domain", Placeholder: "docker.xcord.net"),
            new("volumeSize", "Volume (GB)", Placeholder: "50"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "Private Docker registry with auto-TLS via Caddy",
        WireRequirements: [],
        DockerBehavior: new());
}
