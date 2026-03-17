namespace XcordTopo.PluginSdk;

/// <summary>
/// Base class for image plugins. Provides sensible defaults for all optional members
/// so simple plugins only need to implement identity + descriptor + catalog entry.
/// </summary>
public abstract class ImagePluginBase : IImagePlugin
{
    // --- Required: identity ---
    public abstract string TypeId { get; }
    public abstract string Label { get; }
    public abstract string Description { get; }

    // --- Required: what this image looks like ---
    public abstract ImageDescriptor GetDescriptor();
    public abstract CatalogEntry GetCatalogEntry();

    // --- Optional: override as needed ---
    public virtual IReadOnlyList<SecretDefinition> GetSecrets() => [];
    public virtual IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];
    public virtual bool HasCustomEnvVarBuilder => false;
    public virtual IReadOnlyList<EnvVarEntry> BuildEnvVars(EnvVarContext context) => [];
    public virtual IReadOnlyList<WireRequirement> GetWireRequirements() => [];
    public virtual SubdomainRule GetSubdomainRule() => new NoSubdomain();
    public virtual BackupDefinition? GetBackupDefinition() => null;
    public virtual string? GetCommandOverride() => null;
    public virtual ManifestContribution? GetManifestContribution() => null;
    public virtual DockerBehavior GetDockerBehavior() => new();
}
