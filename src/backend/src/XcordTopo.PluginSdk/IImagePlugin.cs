namespace XcordTopo.PluginSdk;

/// <summary>
/// Defines an image type plugin. Implement this interface to add new image types
/// that can be placed on topologies and deployed via Terraform.
/// </summary>
public interface IImagePlugin
{
    /// <summary>Unique identifier for this image type. Built-in types use their enum name (e.g. "PostgreSQL").
    /// External plugins should use a "plugin:" prefix (e.g. "plugin:grafana").</summary>
    string TypeId { get; }

    string Label { get; }
    string Description { get; }

    ImageDescriptor GetDescriptor();
    IReadOnlyList<SecretDefinition> GetSecrets();

    /// <summary>Declarative env var templates. Used when HasCustomEnvVarBuilder is false.
    /// Templates use expressions like {secret:password}, {wire:pg:host}, {derived:dbName}, etc.</summary>
    IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates();

    /// <summary>If true, the engine calls BuildEnvVars() instead of resolving templates from GetEnvVarTemplates().</summary>
    bool HasCustomEnvVarBuilder => false;

    /// <summary>Imperative env var builder for complex plugins that can't be expressed declaratively.
    /// Only called when HasCustomEnvVarBuilder is true.</summary>
    IReadOnlyList<EnvVarEntry> BuildEnvVars(EnvVarContext context) => [];

    IReadOnlyList<WireRequirement> GetWireRequirements();
    SubdomainRule GetSubdomainRule();
    BackupDefinition? GetBackupDefinition();

    /// <summary>Command override template. Supports {secret:name} expressions.</summary>
    string? GetCommandOverride();

    CatalogEntry GetCatalogEntry();
    ManifestContribution? GetManifestContribution();
    DockerBehavior GetDockerBehavior();
}
