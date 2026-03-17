using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class FederationServerImagePlugin : IImagePlugin
{
    public string TypeId => "FederationServer";
    public string Label => "Federation Server";
    public string Description => "xcord federation instance";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(80)],
        MountPath: null,
        MinRamMb: 192,
        SharedOverheadMb: 0,
        DefaultScaling: PluginImageScaling.PerTenant);

    public IReadOnlyList<SecretDefinition> GetSecrets() => [];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];

    public bool HasCustomEnvVarBuilder => true;

    public IReadOnlyList<EnvVarEntry> BuildEnvVars(EnvVarContext context)
    {
        var envVars = new List<EnvVarEntry>();

        // PG wire
        var pg = context.ResolveWire("pg");
        if (pg != null)
        {
            envVars.Add(new("Database__ConnectionString",
                $"Host={pg.Host};Port={pg.Port};Database=xcord;Username=postgres;Password={pg.SecretRef}"));
        }

        // Redis wire
        var redis = context.ResolveWire("redis");
        if (redis != null)
            envVars.Add(new("Redis__ConnectionString", $"{redis.Host}:{redis.Port},password={redis.SecretRef}"));

        // MinIO wire
        var minio = context.ResolveWire("minio");
        if (minio != null)
        {
            envVars.Add(new("MinIO__Endpoint", $"{minio.Host}:{minio.Port}"));
            envVars.Add(new("MinIO__AccessKey", minio.SecretRef.Replace("secret_key", "access_key")));
            envVars.Add(new("MinIO__SecretKey", minio.SecretRef));
        }

        // Service keys - SMTP
        var smtpRef = context.ServiceKeyRef("smtp_host");
        if (smtpRef != null)
        {
            envVars.Add(new("Email__SmtpHost", smtpRef));
            var port = context.ServiceKeyRef("smtp_port");
            if (port != null) envVars.Add(new("Email__SmtpPort", port));
            var user = context.ServiceKeyRef("smtp_username");
            if (user != null) envVars.Add(new("Email__SmtpUsername", user));
            var pass = context.ServiceKeyRef("smtp_password");
            if (pass != null) envVars.Add(new("Email__SmtpPassword", pass));
            var from = context.ServiceKeyRef("smtp_from_address");
            if (from != null) envVars.Add(new("Email__FromAddress", from));
            var fromName = context.ServiceKeyRef("smtp_from_name");
            if (fromName != null) envVars.Add(new("Email__FromName", fromName));
        }

        // Tenor
        var tenorRef = context.ServiceKeyRef("tenor_api_key");
        if (tenorRef != null) envVars.Add(new("Gif__ApiKey", tenorRef));

        return envVars;
    }

    public IReadOnlyList<WireRequirement> GetWireRequirements() =>
    [
        new("pg", "PostgreSQL"),
        new("redis", "Redis"),
        new("minio", "MinIO")
    ];

    public SubdomainRule GetSubdomainRule() => new WildcardSubdomain();

    public BackupDefinition? GetBackupDefinition() => null;
    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => null;

    public DockerBehavior GetDockerBehavior() => new(
        RequiresPrivateRegistry: true,
        VersionVariableName: "fed_version",
        DbNameWhenConsuming: "xcord",
        RegistryName: "fed",
        GitRepoUrl: "https://github.com/xcord-net/xcord-fed.git");

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "FederationServer",
        Label: "Federation Server",
        Color: "#bb9af7",
        DefaultWidth: 140,
        DefaultHeight: 60,
        DefaultPorts:
        [
            new("http", "Network", "In", "Left", 0.5),
            new("pg", "Database", "Out", "Right", 0.25),
            new("redis", "Database", "Out", "Right", 0.5),
            new("minio", "Storage", "Out", "Right", 0.75)
        ],
        DefaultDockerImage: "{registry}/fed:latest",
        ConfigFields:
        [
            new("tier", "Tier", "select", OptionsFrom: "tierProfiles", ParentKinds: ["ComputePool"]),
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
        ],
        DefaultScaling: PluginImageScaling.PerTenant,
        Description: "xcord federation instance",
        WireRequirements: [new("pg", "PostgreSQL"), new("redis", "Redis"), new("minio", "MinIO")],
        DockerBehavior: new(RequiresPrivateRegistry: true, VersionVariableName: "fed_version", DbNameWhenConsuming: "xcord", RegistryName: "fed", GitRepoUrl: "https://github.com/xcord-net/xcord-fed.git"));
}
