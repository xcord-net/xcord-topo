using XcordTopo.PluginSdk;

namespace Zombocom;

public sealed class ZombocomImagePlugin : ImagePluginBase
{
    public override string TypeId => "plugin:zombocom";
    public override string Label => "Zombocom";
    public override string Description => "You can do anything at zombo.com";

    public override ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(80)],
        MountPath: null,
        MinRamMb: 64,
        SharedOverheadMb: 0,
        DefaultDockerImage: "{registry}/zombocom:latest",
        DefaultScaling: PluginImageScaling.Shared,
        IsPublicEndpoint: true);

    public override SubdomainRule GetSubdomainRule() => new ConfigSubdomain("subdomain");

    public override DockerBehavior GetDockerBehavior() => new(
        RequiresPrivateRegistry: true,
        VersionVariableName: "zombocom_version",
        RegistryName: "zombocom");

    public override CatalogEntry GetCatalogEntry() => new(
        TypeId: TypeId,
        Label: Label,
        Color: "#ff69b4",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("http", "Network", "In", "Left", 0.5)],
        DefaultDockerImage: "{registry}/zombocom:latest",
        ConfigFields: [
            new("subdomain", "Subdomain", Placeholder: "zombocom",
                ValidateRegex: @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$",
                ValidateMessage: "Lowercase letters, numbers, and hyphens only")
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: Description,
        WireRequirements: [],
        DockerBehavior: GetDockerBehavior());
}
