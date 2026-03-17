using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class HubServerImagePlugin : IImagePlugin
{
    public string TypeId => "HubServer";
    public string Label => "Hub Server";
    public string Description => "xcord hub control plane";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(80)],
        MountPath: null,
        MinRamMb: 512,
        SharedOverheadMb: 0,
        IsPublicEndpoint: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() =>
    [
        new("jwt_secret", 64, "JWT signing key for hub server"),
        new("encryption_key", 32, "Encryption key for hub server")
    ];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];

    public bool HasCustomEnvVarBuilder => true;

    public IReadOnlyList<EnvVarEntry> BuildEnvVars(EnvVarContext context)
    {
        var envVars = new List<EnvVarEntry>();

        // PG wire
        var pg = context.ResolveWire("pg");
        if (pg != null)
        {
            var dbName = "xcord_hub";
            envVars.Add(new("Database__ConnectionString",
                $"Host={pg.Host};Port={pg.Port};Database={dbName};Username=postgres;Password={pg.SecretRef}"));
        }

        // Redis wire
        var redis = context.ResolveWire("redis");
        if (redis != null)
            envVars.Add(new("Redis__ConnectionString", $"{redis.Host}:{redis.Port},password={redis.SecretRef}"));

        // MinIO wire (optional)
        var minio = context.ResolveWire("minio");
        if (minio != null)
        {
            envVars.Add(new("Storage__Endpoint", $"{minio.Host}:{minio.Port}"));
            // MinIO access key uses "access_key" secret, secret key uses "secret_key" secret
            // The wire resolution provides the primary secret; we need to derive the others
            // from the wired MinIO image's secrets
            envVars.Add(new("Storage__AccessKey", minio.SecretRef.Replace("secret_key", "access_key")));
            envVars.Add(new("Storage__SecretKey", minio.SecretRef));
            envVars.Add(new("Storage__BucketName", "xcord-hub"));
            envVars.Add(new("Storage__UseSsl", "false"));
        }

        // JWT (auto-generated secrets - use global names for hub)
        envVars.Add(new("Jwt__SecretKey", context.SecretRef("jwt_secret")));
        envVars.Add(new("Jwt__Audience", "xcord-hub"));

        // Encryption
        envVars.Add(new("Encryption__Key", context.SecretRef("encryption_key")));

        // Captcha
        envVars.Add(new("Captcha__Enabled", "false"));

        // Service keys - Stripe
        var stripeRef = context.ServiceKeyRef("stripe_publishable_key");
        if (stripeRef != null)
        {
            envVars.Add(new("Stripe__PublishableKey", stripeRef));
            var sk = context.ServiceKeyRef("stripe_secret_key");
            if (sk != null) envVars.Add(new("Stripe__SecretKey", sk));
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
            envVars.Add(new("Email__UseSsl", "true"));
            envVars.Add(new("Email__DevMode", "false"));
        }

        // Service keys - Admin
        var adminUser = context.ServiceKeyRef("hub_admin_username");
        if (adminUser != null) envVars.Add(new("Admin__Username", adminUser));
        var adminEmail = context.ServiceKeyRef("hub_admin_email");
        if (adminEmail != null) envVars.Add(new("Admin__Email", adminEmail));
        var adminPass = context.ServiceKeyRef("hub_admin_password");
        if (adminPass != null) envVars.Add(new("Admin__Password", adminPass));

        // Derive JWT issuer and CORS from hub_base_domain
        if (context.BaseDomain != null)
        {
            envVars.Add(new("Jwt__Issuer", $"https://${{var.hub_base_domain}}"));
            envVars.Add(new("Cors__AllowedOrigins__0", $"https://${{var.hub_base_domain}}"));
            envVars.Add(new("Cors__AllowedOrigins__1", $"https://www.${{var.hub_base_domain}}"));
            envVars.Add(new("Email__HubBaseUrl", $"https://${{var.hub_base_domain}}"));
        }

        // Tenor
        var tenorRef = context.ServiceKeyRef("tenor_api_key");
        if (tenorRef != null) envVars.Add(new("Tenor__ApiKey", tenorRef));

        return envVars;
    }

    public IReadOnlyList<WireRequirement> GetWireRequirements() =>
    [
        new("pg", "PostgreSQL"),
        new("redis", "Redis")
    ];

    public SubdomainRule GetSubdomainRule() => new FixedSubdomain("www");

    public BackupDefinition? GetBackupDefinition() => null;
    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => null;

    public DockerBehavior GetDockerBehavior() => new(
        RequiresPrivateRegistry: true,
        VersionVariableName: "hub_version",
        DbNameWhenConsuming: "xcord_hub",
        RegistryName: "hub",
        GitRepoUrl: "https://github.com/xcord-net/xcord-hub.git");

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "HubServer",
        Label: "Hub Server",
        Color: "#7aa2f7",
        DefaultWidth: 140,
        DefaultHeight: 60,
        DefaultPorts:
        [
            new("http", "Network", "In", "Left", 0.5),
            new("pg", "Database", "Out", "Right", 0.33),
            new("redis", "Database", "Out", "Right", 0.67)
        ],
        DefaultDockerImage: "{registry}/hub:latest",
        ConfigFields:
        [
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "xcord hub control plane",
        WireRequirements: [new("pg", "PostgreSQL"), new("redis", "Redis")],
        DockerBehavior: new(RequiresPrivateRegistry: true, VersionVariableName: "hub_version", DbNameWhenConsuming: "xcord_hub", RegistryName: "hub", GitRepoUrl: "https://github.com/xcord-net/xcord-hub.git"));
}
