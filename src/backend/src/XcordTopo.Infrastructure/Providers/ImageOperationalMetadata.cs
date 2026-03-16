using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed record ImageMetadata(
    int[] Ports,
    string? MountPath,
    int MinRamMb,
    int SharedOverheadMb,
    string? CommandOverride,
    Dictionary<string, string> EnvVarTemplates,
    string? DockerImage = null,
    bool IsPublicEndpoint = false
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
            },
            DockerImage: "postgres:17-alpine"
        ),
        [ImageKind.Redis] = new(
            Ports: [6379],
            MountPath: "/data",
            MinRamMb: 256,
            SharedOverheadMb: 512,
            CommandOverride: "redis-server --requirepass {password}",
            EnvVarTemplates: new(),
            DockerImage: "redis:7-alpine"
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
            },
            DockerImage: "minio/minio:RELEASE.2025-02-28T09-55-16Z"
        ),
        [ImageKind.HubServer] = new(
            Ports: [80],
            MountPath: null,
            MinRamMb: 512,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["Database__ConnectionString"] = "{pg}",
                ["Redis__ConnectionString"] = "{redis}"
            },
            IsPublicEndpoint: true
        ),
        [ImageKind.FederationServer] = new(
            Ports: [80],
            MountPath: null,
            MinRamMb: 192,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new()
            {
                ["Database__ConnectionString"] = "{pg}",
                ["Redis__ConnectionString"] = "{redis}",
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
            },
            IsPublicEndpoint: true
        ),
        [ImageKind.Registry] = new(
            Ports: [5000],
            MountPath: "/var/lib/registry",
            MinRamMb: 256,
            SharedOverheadMb: 0,
            CommandOverride: null,
            EnvVarTemplates: new(),
            DockerImage: "registry:2",
            IsPublicEndpoint: true
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
                ["FederationServer"] = new() { MemoryMb = 192, CpuMillicores = 100, DiskMb = 256 }
            }
        },
        new()
        {
            Id = "basic",
            Name = "Basic Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 256, CpuMillicores = 250, DiskMb = 512 }
            }
        },
        new()
        {
            Id = "pro",
            Name = "Pro Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 512, CpuMillicores = 350, DiskMb = 2048 }
            }
        },
        new()
        {
            Id = "enterprise",
            Name = "Enterprise Tier",
            ImageSpecs = new()
            {
                ["FederationServer"] = new() { MemoryMb = 1024, CpuMillicores = 750, DiskMb = 8192 }
            }
        }
    ];

    /// <summary>
    /// Calculates shared infrastructure overhead for a compute pool host
    /// based on actual images marked as Shared scaling.
    /// </summary>
    public static int CalculateSharedOverheadMb(List<Image> poolImages)
    {
        var overhead = 0;
        foreach (var image in poolImages)
        {
            if (image.Scaling == ImageScaling.Shared &&
                Images.TryGetValue(image.Kind, out var meta))
            {
                overhead += Math.Max(meta.MinRamMb, meta.SharedOverheadMb);
            }
        }
        // Always include Caddy overhead for compute pools
        overhead += Caddy.MinRamMb;
        return overhead;
    }

    /// <summary>
    /// Calculates how many tenants fit per host based on actual pool images and their scaling.
    /// </summary>
    public static int CalculateTenantsPerHost(int hostMemoryMb, TierProfile tierProfile, List<Image> poolImages)
    {
        var sharedOverhead = CalculateSharedOverheadMb(poolImages);
        var available = hostMemoryMb - sharedOverhead;
        if (available <= 0) return 0;

        // Sum per-tenant memory from all PerTenant images using tier profile specs
        var perTenantMb = 0;
        foreach (var image in poolImages)
        {
            if (image.Scaling != ImageScaling.PerTenant) continue;
            var spec = tierProfile.ImageSpecs.GetValueOrDefault(image.Kind.ToString());
            if (spec != null)
                perTenantMb += spec.MemoryMb;
            else if (Images.TryGetValue(image.Kind, out var meta))
                perTenantMb += meta.MinRamMb;
            else
                perTenantMb += 256;
        }

        if (perTenantMb <= 0)
            return poolImages.Count > 0 ? 1 : 0; // Dedicated host model: 1 tenant per host
        return available / perTenantMb;
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
