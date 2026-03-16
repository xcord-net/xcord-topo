using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class PostgreSqlImagePlugin : IImagePlugin
{
    public string TypeId => "PostgreSQL";
    public string Label => "PostgreSQL";
    public string Description => "PostgreSQL database";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(5432)],
        MountPath: "/var/lib/postgresql/data",
        MinRamMb: 512,
        SharedOverheadMb: 1024,
        DefaultDockerImage: "postgres:17-alpine",
        IsDataService: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() =>
        [new("password", 32, "PostgreSQL password")];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() =>
    [
        new("POSTGRES_PASSWORD", "{secret:password}"),
        new("POSTGRES_DB", "{derived:dbName}"),
        new("POSTGRES_USER", "postgres")
    ];

    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new NoSubdomain();

    public BackupDefinition? GetBackupDefinition() => new(
        "docker exec {containerName} pg_dumpall -U postgres | gzip > {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).sql.gz");

    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => new(
        "Database",
        [new("ConnectionString", "Host={privateIp};Port=5432;Database=postgres;Username=postgres;Password={secret:password}")]);

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "PostgreSQL",
        Label: "PostgreSQL",
        Color: "#336791",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("postgres", "Database", "In", "Left", 0.5)],
        DefaultDockerImage: "postgres:17-alpine",
        ConfigFields:
        [
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
            new("volumeSize", "Volume (GB)", Placeholder: "25"),
            new("backupFrequency", "Backup Frequency", Placeholder: "daily"),
            new("backupRetention", "Backup Retention", Placeholder: "7"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "PostgreSQL database",
        WireRequirements: [],
        DockerBehavior: new());
}
