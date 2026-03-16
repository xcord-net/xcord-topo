using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class CustomImagePlugin : IImagePlugin
{
    public string TypeId => "Custom";
    public string Label => "Custom Image";
    public string Description => "Custom Docker image";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [],
        MountPath: null,
        MinRamMb: 256,
        SharedOverheadMb: 0);

    public IReadOnlyList<SecretDefinition> GetSecrets() => [];
    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];
    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new ConfigSubdomain("subdomain");

    public BackupDefinition? GetBackupDefinition() => null;
    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => null;

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "Custom",
        Label: "Custom Image",
        Color: "#a9b1d6",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("port", "Generic", "InOut", "Left", 0.5)],
        DefaultDockerImage: null,
        ConfigFields:
        [
            new("subdomain", "Subdomain", Placeholder: "myapp", ValidateRegex: @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", ValidateMessage: "Lowercase letters, numbers, and hyphens only"),
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "Custom Docker image",
        WireRequirements: [],
        DockerBehavior: new());
}
