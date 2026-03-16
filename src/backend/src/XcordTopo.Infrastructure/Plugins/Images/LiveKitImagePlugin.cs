using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins.Images;

public sealed class LiveKitImagePlugin : IImagePlugin
{
    public string TypeId => "LiveKit";
    public string Label => "LiveKit";
    public string Description => "LiveKit WebRTC SFU";

    public ImageDescriptor GetDescriptor() => new(
        Ports: [new PortSpec(7880, "tcp", "primary"), new PortSpec(7881, "tcp", "api"), new PortSpec(7882, "udp", "rtc")],
        MountPath: null,
        MinRamMb: 1024,
        SharedOverheadMb: 0,
        IsPublicEndpoint: true);

    public IReadOnlyList<SecretDefinition> GetSecrets() =>
    [
        new("api_key", 32, "LiveKit API key"),
        new("api_secret", 40, "LiveKit API secret")
    ];

    public IReadOnlyList<EnvVarTemplate> GetEnvVarTemplates() => [];

    public bool HasCustomEnvVarBuilder => true;

    public IReadOnlyList<EnvVarEntry> BuildEnvVars(EnvVarContext context)
    {
        var envVars = new List<EnvVarEntry>();
        var apiKeyRef = context.SecretRef("api_key");
        var apiSecretRef = context.SecretRef("api_secret");
        envVars.Add(new("LIVEKIT_KEYS", $"{apiKeyRef}:{apiSecretRef}"));

        var redis = context.ResolveWire("redis");
        if (redis != null)
            envVars.Add(new("REDIS_URL", $"redis://:{redis.SecretRef}@{redis.Host}:{redis.Port}/0"));

        return envVars;
    }

    public IReadOnlyList<WireRequirement> GetWireRequirements() => [];

    public SubdomainRule GetSubdomainRule() => new DerivedSubdomain();

    public BackupDefinition? GetBackupDefinition() => null;
    public string? GetCommandOverride() => null;

    public ManifestContribution? GetManifestContribution() => new(
        "RTC",
        [
            new("Host", "wss://{privateIp}:7880"),
            new("ApiKey", "{secret:api_key}"),
            new("ApiSecret", "{secret:api_secret}")
        ]);

    public DockerBehavior GetDockerBehavior() => new();

    public CatalogEntry GetCatalogEntry() => new(
        TypeId: "LiveKit",
        Label: "LiveKit",
        Color: "#ff6b6b",
        DefaultWidth: 120,
        DefaultHeight: 50,
        DefaultPorts:
        [
            new("rtc", "Network", "In", "Left", 0.33),
            new("api", "Network", "In", "Left", 0.67),
            new("redis", "Database", "Out", "Right", 0.5)
        ],
        DefaultDockerImage: "livekit/livekit-server:v1.8.3",
        ConfigFields:
        [
            new("scaling", "Scaling", "select", Options: [new("Shared", "Shared (1 per host)"), new("PerTenant", "Per Tenant")], ParentKinds: ["ComputePool"]),
            new("replicas", "Replicas", Placeholder: "1"),
        ],
        DefaultScaling: PluginImageScaling.Shared,
        Description: "LiveKit WebRTC SFU",
        WireRequirements: [],
        DockerBehavior: new());
}
