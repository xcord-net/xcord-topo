using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Models;

public sealed class Topology
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Provider { get; set; } = "linode";
    public Dictionary<string, string> ProviderConfig { get; set; } = new();
    public Dictionary<string, string> ServiceKeys { get; set; } = new();
    public List<Container> Containers { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];
    public List<TierProfile> TierProfiles { get; set; } = [];
    public string Registry { get; set; } = "docker.xcord.net";
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DeployStatus? LastDeployStatus { get; set; }
    public DateTimeOffset? LastDeployedAt { get; set; }
    public int DeployedResourceCount { get; set; }
    public BackupTarget? BackupTarget { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeployStatus
{
    Succeeded,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContainerKind
{
    Host,
    Caddy,
    ComputePool,
    Dns,
    DataPool
}

/// <summary>
/// Built-in image kinds. External plugins use Custom as the Kind value
/// with their actual type in TypeId (e.g. "plugin:zombocom").
/// </summary>
[JsonConverter(typeof(ImageKindConverter))]
public enum ImageKind
{
    HubServer,
    FederationServer,
    Redis,
    PostgreSQL,
    MinIO,
    LiveKit,
    Registry,
    Custom
}

/// <summary>
/// Deserializes known enum values normally; unknown strings (plugin types) map to Custom.
/// On serialization, writes the TypeId if the Image is available, otherwise the enum name.
/// </summary>
public sealed class ImageKindConverter : JsonConverter<ImageKind>
{
    /// <summary>
    /// When an unknown kind string is deserialized (e.g. "plugin:zombocom"),
    /// the original value is stashed here so Image.OnDeserialized can backfill TypeId.
    /// </summary>
    [ThreadStatic]
    internal static string? LastCustomKindValue;

    public override ImageKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        LastCustomKindValue = null;
        if (value is not null && Enum.TryParse<ImageKind>(value, ignoreCase: true, out var kind))
            return kind;
        LastCustomKindValue = value;
        return ImageKind.Custom;
    }

    public override void Write(Utf8JsonWriter writer, ImageKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageScaling
{
    Shared,
    PerTenant
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortType
{
    Network,
    Database,
    Storage,
    Control,
    Generic
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortDirection
{
    In,
    Out,
    InOut
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortSide
{
    Top,
    Right,
    Bottom,
    Left
}

public sealed class Container
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ContainerKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    public List<Port> Ports { get; set; } = [];
    public List<Image> Images { get; set; } = [];
    public List<Container> Children { get; set; } = [];
    public Dictionary<string, string> Config { get; set; } = new();
}

public sealed class Image : IJsonOnDeserialized
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ImageKind Kind { get; set; }
    public string? TypeId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 60;
    public List<Port> Ports { get; set; } = [];
    public string? DockerImage { get; set; }
    public Dictionary<string, string> Config { get; set; } = new();
    public ImageScaling Scaling { get; set; } = ImageScaling.Shared;

    void IJsonOnDeserialized.OnDeserialized()
    {
        // If kind was an unknown string (plugin type), backfill TypeId so
        // ResolveTypeId() returns the original plugin identifier.
        if (Kind == ImageKind.Custom && TypeId == null && ImageKindConverter.LastCustomKindValue != null)
        {
            TypeId = ImageKindConverter.LastCustomKindValue;
            ImageKindConverter.LastCustomKindValue = null;
        }
    }
}

public sealed class Port
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public PortType Type { get; set; }
    public PortDirection Direction { get; set; }
    public PortSide Side { get; set; }
    public double Offset { get; set; }
}

public sealed class Wire
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public Guid FromPortId { get; set; }
    public Guid ToNodeId { get; set; }
    public Guid ToPortId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupTargetKind
{
    LinodeObjectStorage,
    AwsS3,
    S3Compatible
}

public sealed class BackupTarget
{
    public string Label { get; set; } = string.Empty;
    public BackupTargetKind Kind { get; set; }
    public string Region { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public int? GlacierTransitionDays { get; set; }
}

public static class ImageExtensions
{
    /// <summary>
    /// Returns the TypeId if set, otherwise falls back to Kind.ToString().
    /// This enables backward compatibility: old topologies without TypeId
    /// still work, and plugin types set TypeId explicitly.
    /// </summary>
    public static string ResolveTypeId(this Image image) =>
        image.TypeId ?? image.Kind.ToString();
}
