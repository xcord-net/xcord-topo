using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed record ImageMetadata(
    int[] Ports,
    string? MountPath,
    int MinRamMb,
    int SharedOverheadMb,
    string? CommandOverride,
    Dictionary<string, string> EnvVarTemplates
);

public sealed record CaddyContainerMetadata(
    int[] Ports,
    string? MountPath,
    int MinRamMb,
    string DockerImage
);

public static class ImageOperationalMetadata
{
    public static readonly Dictionary<ImageKind, ImageMetadata> Images = new()
    {
        [ImageKind.PostgreSQL] = new(
            Ports: [5432],
            MountPath: "/var/lib/postgresql/data",
            MinRamMb: 512,
            SharedOverheadMb: 1024,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["POSTGRES_PASSWORD"] = "{password}",
                ["POSTGRES_DB"] = "{dbName}",
                ["POSTGRES_USER"] = "postgres"
            }
        ),
        [ImageKind.Redis] = new(
            Ports: [6379],
            MountPath: "/data",
            MinRamMb: 256,
            SharedOverheadMb: 512,
            CommandOverride: "redis-server --requirepass {password}",
            EnvVarTemplates: new()
        ),
        [ImageKind.MinIO] = new(
            Ports: [9000, 9001],
            MountPath: "/data",
            MinRamMb: 512,
            SharedOverheadMb: 512,
            CommandOverride: "server /data --console-address :9001",
            EnvVarTemplates: new()
            {
                ["MINIO_ROOT_USER"] = "{accessKey}",
                ["MINIO_ROOT_PASSWORD"] = "{secretKey}"
            }
        ),
        [ImageKind.HubServer] = new(
            Ports: [80],
            MountPath: null,
            MinRamMb: 512,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["ConnectionStrings__DefaultConnection"] = "{pg_connection}",
                ["ConnectionStrings__Redis"] = "{redis_connection}"
            }
        ),
        [ImageKind.FederationServer] = new(
            Ports: [80],
            MountPath: null,
            MinRamMb: 512,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["ConnectionStrings__DefaultConnection"] = "{pg_connection}",
                ["ConnectionStrings__Redis"] = "{redis_connection}",
                ["MinIO__Endpoint"] = "{minio_endpoint}",
                ["MinIO__AccessKey"] = "{minio_accessKey}",
                ["MinIO__SecretKey"] = "{minio_secretKey}"
            }
        ),
        [ImageKind.LiveKit] = new(
            Ports: [7880, 7881, 7882],
            MountPath: null,
            MinRamMb: 1024,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["LIVEKIT_KEYS"] = "{apiKey}: {apiSecret}"
            }
        ),
        [ImageKind.Custom] = new(
            Ports: [],
            MountPath: null,
            MinRamMb: 256,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
        ),
    };

    public static readonly CaddyContainerMetadata Caddy = new(
        Ports: [80, 443],
        MountPath: "/data",
        MinRamMb: 128,
        DockerImage: "caddy:2-alpine"
    );

    public static readonly List<TierProfile> DefaultTierProfiles =
    [
        new()
        {
            Id = "free",
            Name = "Free Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 256, CpuMillicores = 250, DiskMb = 512 }
            }
        },
        new()
        {
            Id = "basic",
            Name = "Basic Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 512, CpuMillicores = 500, DiskMb = 2048 }
            }
        },
        new()
        {
            Id = "pro",
            Name = "Pro Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 1024, CpuMillicores = 1000, DiskMb = 5120 }
            }
        },
        new()
        {
            Id = "enterprise",
            Name = "Enterprise Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 2048, CpuMillicores = 2000, DiskMb = 25600 }
            }
        }
    ];

    /// <summary>
    /// Calculates the total shared infrastructure overhead (in MB) for a compute pool host.
    /// This is the sum of SharedOverheadMb for shared image kinds (PG, Redis, MinIO) plus Caddy.
    /// </summary>
    public static int CalculateSharedOverheadMb()
    {
        var overhead = Images[ImageKind.PostgreSQL].SharedOverheadMb
            + Images[ImageKind.Redis].SharedOverheadMb
            + Images[ImageKind.MinIO].SharedOverheadMb
            + Caddy.MinRamMb;
        return overhead;
    }

    /// <summary>
    /// Calculates how many tenants of a given tier can fit on a compute host with the given total memory.
    /// </summary>
    public static int CalculateTenantsPerHost(int hostMemoryMb, TierProfile tierProfile)
    {
        var sharedOverhead = CalculateSharedOverheadMb();
        var available = hostMemoryMb - sharedOverhead;
        if (available <= 0) return 0;

        var fedSpec = tierProfile.ImageSpecs.GetValueOrDefault("FederationServer");
        if (fedSpec == null || fedSpec.MemoryMb <= 0) return 0;

        return available / fedSpec.MemoryMb;
    }

    /// <summary>
    /// Calculates how many compute hosts are needed for a target number of tenants.
    /// </summary>
    public static int CalculateHostsRequired(int targetTenants, int tenantsPerHost)
    {
        if (tenantsPerHost <= 0) return targetTenants;
        return (targetTenants + tenantsPerHost - 1) / tenantsPerHost;
    }
}
