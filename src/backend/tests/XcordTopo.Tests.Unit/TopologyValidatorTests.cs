using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class TopologyValidatorTests
{
    private readonly TopologyValidator _validator = new();

    [Fact]
    public void Validate_EmptyName_ReturnsError()
    {
        var topology = new Topology { Name = "" };
        topology.Containers.Add(new Container { Name = "test", Width = 300, Height = 200 });

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("name is required"));
    }

    [Fact]
    public void Validate_NoContainers_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("at least one container"));
    }

    [Fact]
    public void Validate_ValidTopology_ReturnsNoErrors()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Server 1",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        });

        var errors = _validator.Validate(topology);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_WireWithInvalidSource_ReturnsError()
    {
        var container = new Container
        {
            Name = "Server 1",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(container);
        topology.Wires.Add(new Wire
        {
            FromNodeId = Guid.NewGuid(), // non-existent
            FromPortId = Guid.NewGuid(),
            ToNodeId = container.Id,
            ToPortId = Guid.NewGuid()
        });

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("non-existent source node"));
    }

    [Fact]
    public void Validate_SelfReferencingWire_ReturnsError()
    {
        var port1 = new Port { Name = "p1", Type = PortType.Network, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.5 };
        var port2 = new Port { Name = "p2", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 };
        var container = new Container
        {
            Name = "Server 1",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Ports = [port1, port2]
        };
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(container);
        topology.Wires.Add(new Wire
        {
            FromNodeId = container.Id,
            FromPortId = port1.Id,
            ToNodeId = container.Id,
            ToPortId = port2.Id
        });

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("cannot connect a node to itself"));
    }

    [Fact]
    public void Validate_ContainerWithNoName_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container { Name = "", Width = 300, Height = 200 });

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("must have a name"));
    }

    [Fact]
    public void Validate_ContainerWithZeroDimensions_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container { Name = "Bad", Width = 0, Height = 200 });

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("positive dimensions"));
    }

    [Fact]
    public void Validate_InvalidReplicas_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        var container = new Container
        {
            Name = "Server",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        container.Images.Add(new Image
        {
            Name = "Bad Image",
            Kind = ImageKind.HubServer,
            Width = 140,
            Height = 60,
            Config = new Dictionary<string, string> { ["replicas"] = "-1" }
        });
        topology.Containers.Add(container);

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("invalid replicas"));
    }

    [Fact]
    public void Validate_VariableReplicas_IsValid()
    {
        var topology = new Topology { Name = "Test" };
        var container = new Container
        {
            Name = "Server",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        container.Images.Add(new Image
        {
            Name = "Fed Server",
            Kind = ImageKind.FederationServer,
            Width = 140,
            Height = 60,
            Config = new Dictionary<string, string> { ["replicas"] = "$TIER_REPLICAS" }
        });
        topology.Containers.Add(container);

        var errors = _validator.Validate(topology);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidFederationGroupInstanceCount_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        var fedGroup = new Container
        {
            Name = "Federation",
            Kind = ContainerKind.FederationGroup,
            Width = 500,
            Height = 350,
            Config = new Dictionary<string, string> { ["instanceCount"] = "0" }
        };
        topology.Containers.Add(fedGroup);

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("invalid instanceCount"));
    }

    [Fact]
    public void Validate_RecursesIntoChildren()
    {
        var topology = new Topology { Name = "Test" };
        var network = new Container
        {
            Name = "Network",
            Kind = ContainerKind.Network,
            Width = 800,
            Height = 600
        };
        var host = new Container
        {
            Name = "", // invalid — no name
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        network.Children.Add(host);
        topology.Containers.Add(network);

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("must have a name"));
    }
}
