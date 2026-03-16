using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class MinIOImagePlugin : IImagePlugin
{
    public string TypeId => "MinIO";
    public string Label => "MinIO";
    public string Description => "S3-compatible object storage";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(9000, Purpose: "primary"), new PortSpec(9001, Purpose: "console")],
        MountPath: "/data",
        MinRamMb: 512,
        SharedOverheadMb: 512,
        DefaultDockerImage: "minio/minio:RELEASE.2025-02-28T09-55-16Z",
        IsDataService: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() =>
    [
        new("access_key", 20, "MinIO access key"),
        new("secret_key", 40, "MinIO secret key")
    ];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() =>
    [
        new("MINIO_ROOT_USER", "{secret:access_key}"),
        new("MINIO_ROOT_PASSWORD", "{secret:secret_key}")
    ];

    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new NoSubdomain();

    public BackupDefinition? GetBackupDefinition() => new(
        "docker run --rm --network xcord-bridge -v {backupDir}:/backup minio/mc mirror http://{containerName}:9000 /backup/{containerName}_$(date +%Y%m%d_%H%M%S)/");

    public string? GetCommandOverride() => "server /data --console-address :9001";

    public ManifestContribution? GetManifestContribution() => new(
        "Storage",
        [
            new("Endpoint", "https://{privateIp}:9000"),
            new("AccessKey", "{secret:access_key}"),
            new("SecretKey", "{secret:secret_key}"),
            new("UseSsl", "true")
        ]);

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "MinIO",
        Label: "MinIO",
        Color: "#c72e49",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts: [new("s3", "Storage", "In", "Left", 0.5)],
        DefaultDockerImage: "minio/minio:RELEASE.2025-02-28T09-55-16Z",
        ConfigFields:
        [
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
            new("volumeSize", "Volume (GB)", Placeholder: "25"),
            new("backupFrequency", "Backup Frequency", Placeholder: "daily"),
            new("backupRetention", "Backup Retention", Placeholder: "7"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "S3-compatible object storage",
        WireRequirements: [],
        DockerBehavior: new());
}
