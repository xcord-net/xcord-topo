using System.Text.Json.Serialization;

namespace XcordTopo.Models;

public sealed class Topology
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Provider { get; set; } = "linode";
    public Dictionary<string, string> ProviderConfig { get; set; } = new();
    public List<Container> Containers { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContainerKind
{
    Host,
    Network,
    Caddy,
    FederationGroup
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageKind
{
    HubServer,
    FederationServer,
    Redis,
    PostgreSQL,
    MinIO,
    LiveKit,
    Custom
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

public sealed class Image
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ImageKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 60;
    public List<Port> Ports { get; set; } = [];
    public string? DockerImage { get; set; }
    public Dictionary<string, string> Config { get; set; } = new();
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
