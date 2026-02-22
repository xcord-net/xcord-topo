using System.Text.Json.Serialization;

namespace XcordTopo.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageMatchKind
{
    Unchanged,
    Modified,
    Relocated,
    Split,
    Added,
    Removed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContainerMatchKind
{
    Matched,
    Added,
    Removed,
    SplitHost
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationPhaseType
{
    PreCheck,
    Infrastructure,
    DataMigration,
    Provisioning,
    Cutover,
    Cleanup
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationStepType
{
    Validate,
    TerraformApply,
    TerraformDestroy,
    DatabaseDump,
    DatabaseRestore,
    RedisSnapshot,
    RedisRestore,
    ObjectStorageMirror,
    DockerInstall,
    DockerRun,
    DnsUpdate,
    CaddyReload,
    HealthCheck,
    SecretGenerate,
    SecretImport,
    Manual
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionKind
{
    HubDatabaseMigration,
    HubRedisMigration,
    SecretHandling,
    DnsCutover,
    DowntimeTolerance,
    VariableValue
}

public sealed class DecisionOption
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class MigrationDecision
{
    public string Id { get; set; } = string.Empty;
    public DecisionKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
    public List<DecisionOption> Options { get; set; } = [];
    public string? SelectedOptionKey { get; set; }
    public string? CustomValue { get; set; }
}

public sealed class ImageMatch
{
    public Guid? SourceImageId { get; set; }
    public string? SourceImageName { get; set; }
    public ImageKind? SourceImageKind { get; set; }
    public Guid? SourceHostId { get; set; }
    public string? SourceHostName { get; set; }

    public Guid? TargetImageId { get; set; }
    public string? TargetImageName { get; set; }
    public ImageKind? TargetImageKind { get; set; }
    public Guid? TargetHostId { get; set; }
    public string? TargetHostName { get; set; }

    public ImageMatchKind Kind { get; set; }

    /// <summary>
    /// For Split matches: which consumer image in the source topology drove this split target.
    /// </summary>
    public Guid? SplitConsumerId { get; set; }

    /// <summary>
    /// Whether the target is in a FederationGroup (always fresh, no data migration needed).
    /// </summary>
    public bool TargetIsFederation { get; set; }
}

public sealed class ContainerMatch
{
    public Guid? SourceContainerId { get; set; }
    public string? SourceContainerName { get; set; }
    public Guid? TargetContainerId { get; set; }
    public string? TargetContainerName { get; set; }
    public ContainerMatchKind Kind { get; set; }
    public List<Guid> MatchedImageIds { get; set; } = [];
}

public sealed class MigrationStep
{
    public int Order { get; set; }
    public MigrationStepType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Script { get; set; }
    public bool CausesDowntime { get; set; }
    public string? EstimatedDuration { get; set; }
}

public sealed class MigrationPhase
{
    public MigrationPhaseType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<MigrationStep> Steps { get; set; } = [];
}

public sealed class MigrationDiffResult
{
    public string Summary { get; set; } = string.Empty;
    public int HostsAdded { get; set; }
    public int HostsRemoved { get; set; }
    public int ImagesRelocated { get; set; }
    public int ImagesAdded { get; set; }
    public int ImagesRemoved { get; set; }
    public int SplitsDetected { get; set; }
    public List<ImageMatch> ImageMatches { get; set; } = [];
    public List<ContainerMatch> ContainerMatches { get; set; } = [];
    public List<MigrationDecision> Decisions { get; set; } = [];
}

public sealed class MigrationPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceTopologyId { get; set; }
    public string SourceTopologyName { get; set; } = string.Empty;
    public Guid TargetTopologyId { get; set; }
    public string TargetTopologyName { get; set; } = string.Empty;
    public MigrationDiffResult Diff { get; set; } = new();
    public List<MigrationDecision> Decisions { get; set; } = [];
    public List<MigrationPhase> Phases { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
