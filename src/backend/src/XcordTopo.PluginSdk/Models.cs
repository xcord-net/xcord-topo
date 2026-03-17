namespace XcordTopo.PluginSdk;

// Note: ImageScaling is defined in XcordTopo.Models (Shared project) but we need it here
// without a dependency. Re-declare it in the SDK namespace.
public enum PluginImageScaling
{
    Shared,
    PerTenant
}

public sealed record ImageDescriptor(
    PortSpec[] Ports,
    string? MountPath,
    int MinRamMb,
    int SharedOverheadMb,
    string? DefaultDockerImage = null,
    PluginImageScaling DefaultScaling = PluginImageScaling.Shared,
    bool IsPublicEndpoint = false,
    bool IsDataService = false);

public sealed record PortSpec(
    int Port,
    string Protocol = "tcp",
    string Purpose = "primary");

public sealed record SecretDefinition(
    string Name,
    int Length,
    string Description);

public sealed record EnvVarTemplate(string Key, string ValueTemplate);

public sealed record EnvVarEntry(string Key, string Value);

/// <summary>
/// Context passed to IImagePlugin.BuildEnvVars() for complex plugins.
/// </summary>
public sealed record EnvVarContext(
    string HostName,
    string ImageName,
    Func<string, string> SecretRef,
    Func<string, WireResolution?> ResolveWire,
    IReadOnlyDictionary<string, string> ServiceKeys,
    Func<string, string?> ServiceKeyRef,
    string? BaseDomain);

public sealed record WireResolution(string Host, int Port, string SecretRef);

public sealed record WireRequirement(
    string PortName,
    string TargetTypeLabel,
    bool Required = true);

public abstract record SubdomainRule;
public sealed record FixedSubdomain(string Value) : SubdomainRule;
public sealed record WildcardSubdomain() : SubdomainRule;
public sealed record ConfigSubdomain(string ConfigKey) : SubdomainRule;
public sealed record DerivedSubdomain() : SubdomainRule;
public sealed record NoSubdomain() : SubdomainRule;

public sealed record BackupDefinition(string CommandTemplate);

public sealed record CatalogEntry(
    string TypeId,
    string Label,
    string Color,
    int DefaultWidth,
    int DefaultHeight,
    PortDefinition[] DefaultPorts,
    string? DefaultDockerImage,
    ConfigFieldDefinition[] ConfigFields,
    PluginImageScaling DefaultScaling,
    string Description,
    WireRequirement[]? WireRequirements = null,
    DockerBehavior? DockerBehavior = null);

public sealed record PortDefinition(
    string Name,
    string Type,
    string Direction,
    string Side,
    double Offset);

public sealed record ConfigFieldDefinition(
    string Key,
    string Label,
    string Type = "text",
    string? Placeholder = null,
    ConfigOption[]? Options = null,
    string? OptionsFrom = null,
    string[]? ParentKinds = null,
    string? ValidateRegex = null,
    string? ValidateMessage = null);

public sealed record ConfigOption(string Value, string Label);

public sealed record ManifestContribution(
    string ServiceCategory,
    ManifestField[] Fields);

public sealed record ManifestField(string Key, string ValueTemplate);

public sealed record DockerBehavior(
    bool RequiresPrivateRegistry = false,
    string? VersionVariableName = null,
    string? DbNameWhenConsuming = null,
    string? RegistryName = null,
    string? GitRepoUrl = null);
