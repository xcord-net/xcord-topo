using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed record ImageMetadata(
    int[] Ports,
    string? MountPath,
    int MinRamMb,
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
            CommandOverride: "redis-server --requirepass {password}",
            EnvVarTemplates: new()
        ),
        [ImageKind.MinIO] = new(
            Ports: [9000, 9001],
            MountPath: "/data",
            MinRamMb: 512,
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
}
