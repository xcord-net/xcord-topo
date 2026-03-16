using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class RedisImagePlugin : IImagePlugin
{
    public string TypeId => "Redis";
    public string Label => "Redis";
    public string Description => "Redis in-memory data store";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(6379)],
        MountPath: "/data",
        MinRamMb: 256,
        SharedOverheadMb: 512,
        DefaultDockerImage: "redis:7-alpine",
        IsDataService: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() =>
        [new("password", 32, "Redis password")];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];

    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new NoSubdomain();

    public BackupDefinition? GetBackupDefinition() => new(
        "docker exec {containerName} redis-cli BGSAVE && sleep 2 && docker cp {containerName}:/data/dump.rdb {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).rdb");

    public string? GetCommandOverride() => "redis-server --requirepass {secret:password}";

    public ManifestContribution? GetManifestContribution() => new(
        "Cache",
        [new("ConnectionString", "{privateIp}:6379,password={secret:password}")]);

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "Redis",
        Label: "Redis",
        Color: "#dc382d",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("redis", "Database", "In", "Left", 0.5)],
        DefaultDockerImage: "redis:7-alpine",
        ConfigFields:
        [
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
            new("volumeSize", "Volume (GB)", Placeholder: "25"),
            new("backupFrequency", "Backup Frequency", Placeholder: "daily"),
            new("backupRetention", "Backup Retention", Placeholder: "7"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "Redis in-memory data store",
        WireRequirements: [],
        DockerBehavior: new());
}
