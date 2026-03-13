using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class TopologyValidatorTests
{
    private readonly TopologyValidator _validator;

    public TopologyValidatorTests()
    {
        var registry = new ProviderRegistry([new LinodeProvider(), new AwsProvider()]);
        _validator = new TopologyValidator(registry);
    }

    // ─── Existing structural tests (ported) ──────────────────────────

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
            FromNodeId = Guid.NewGuid(),
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
            Name = "Custom Service",
            Kind = ImageKind.Custom,
            Width = 140,
            Height = 60,
            Config = new Dictionary<string, string> { ["replicas"] = "$TIER_REPLICAS" }
        });
        topology.Containers.Add(container);

        var errors = _validator.Validate(topology);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RecursesIntoChildren()
    {
        var topology = new Topology { Name = "Test" };
        var parent = new Container
        {
            Name = "Parent",
            Kind = ContainerKind.Host,
            Width = 800,
            Height = 600
        };
        var child = new Container
        {
            Name = "",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        parent.Children.Add(child);
        topology.Containers.Add(parent);

        var errors = _validator.Validate(topology);

        Assert.Contains(errors, e => e.Contains("must have a name"));
    }

    // ─── New deploy validation tests ─────────────────────────────────

    [Fact]
    public void ValidateFull_DuplicateSanitizedNames_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container { Name = "my server", Kind = ContainerKind.Host, Width = 300, Height = 200 });
        topology.Containers.Add(new Container { Name = "my-server", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("my_server") && e.Message.Contains("collision"));
    }

    [Fact]
    public void ValidateFull_InvalidProvider_ReturnsError()
    {
        var topology = new Topology { Name = "Test", Provider = "azure" };
        topology.Containers.Add(new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("azure") && e.Message.Contains("not registered"));
    }

    [Fact]
    public void ValidateFull_InvalidRegion_ReturnsError()
    {
        var topology = new Topology
        {
            Name = "Test",
            Provider = "linode",
            ProviderConfig = new() { ["linode_region"] = "mars-1" }
        };
        topology.Containers.Add(new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("mars-1") && e.Field == "linode_region");
    }

    [Fact]
    public void ValidateFull_ValidLinodeRegion_NoRegionError()
    {
        var topology = new Topology
        {
            Name = "Test",
            Provider = "linode",
            ProviderConfig = new() { ["linode_region"] = "us-east" }
        };
        topology.Containers.Add(new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.DoesNotContain(result.Errors, e => e.Field == "linode_region");
    }

    [Fact]
    public void ValidateFull_CaddyWithEmptyDomain_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Proxy",
            Kind = ContainerKind.Caddy,
            Width = 200,
            Height = 100,
            Config = new() { ["domain"] = "" }
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("Caddy") && e.Field == "domain");
    }

    [Fact]
    public void ValidateFull_CaddyWithInvalidDomain_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Proxy",
            Kind = ContainerKind.Caddy,
            Width = 200,
            Height = 100,
            Config = new() { ["domain"] = "not a domain" }
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("not a valid domain") && e.Field == "domain");
    }

    [Fact]
    public void ValidateFull_ComputePoolWithoutFedServer_NoError()
    {
        // ComputePool no longer requires FederationServer - they are hub-provisioned at runtime
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Pool",
            Kind = ContainerKind.ComputePool,
            Width = 300,
            Height = 200,
            Images = [new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 60 }]
        });

        var result = _validator.ValidateFull(topology);

        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("FederationServer"));
    }

    [Fact]
    public void ValidateFull_DataPoolWithoutDataImage_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "DataPool",
            Kind = ContainerKind.DataPool,
            Width = 300,
            Height = 200,
            Images = [new Image { Name = "Hub", Kind = ImageKind.HubServer, Width = 120, Height = 60 }]
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("DataPool") && e.Message.Contains("data service"));
    }

    [Fact]
    public void ValidateFull_DataPoolWithDataImage_NoError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "DataPool",
            Kind = ContainerKind.DataPool,
            Width = 300,
            Height = 200,
            Images = [new Image { Name = "PG", Kind = ImageKind.PostgreSQL, Width = 120, Height = 60 }]
        });

        var result = _validator.ValidateFull(topology);

        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("DataPool") && e.Message.Contains("data service"));
    }

    [Fact]
    public void ValidateFull_FedServerWithNoWires_ReturnsErrors()
    {
        var topology = new Topology { Name = "Test" };
        var host = new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 };
        host.Images.Add(new Image
        {
            Name = "Fed",
            Kind = ImageKind.FederationServer,
            Width = 120,
            Height = 60,
            Ports =
            [
                new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.3 },
                new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.5 },
                new Port { Name = "minio", Type = PortType.Storage, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.7 },
            ]
        });
        topology.Containers.Add(host);

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("PostgreSQL"));
        Assert.Contains(result.Errors, e => e.Message.Contains("Redis"));
        Assert.Contains(result.Errors, e => e.Message.Contains("MinIO"));
    }

    [Fact]
    public void ValidateFull_HubServerWithNoWires_ReturnsErrors()
    {
        var topology = new Topology { Name = "Test" };
        var host = new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 };
        host.Images.Add(new Image
        {
            Name = "Hub",
            Kind = ImageKind.HubServer,
            Width = 120,
            Height = 60,
            Ports =
            [
                new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.3 },
                new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.Out, Side = PortSide.Right, Offset = 0.5 },
            ]
        });
        topology.Containers.Add(host);

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("PostgreSQL"));
        Assert.Contains(result.Errors, e => e.Message.Contains("Redis"));
    }

    [Fact]
    public void ValidateFull_UnknownTierProfile_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Pool",
            Kind = ContainerKind.ComputePool,
            Width = 300,
            Height = 200,
            Config = new() { ["tierProfile"] = "platinum" },
            Images = [new Image { Name = "Fed", Kind = ImageKind.FederationServer, Width = 120, Height = 60 }]
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("platinum") && e.Field == "tierProfile");
    }

    [Fact]
    public void ValidateFull_InvalidVolumeSize_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        var host = new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 };
        host.Images.Add(new Image
        {
            Name = "PG",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 60,
            Config = new() { ["volumeSize"] = "-5" }
        });
        topology.Containers.Add(host);

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("volumeSize") && e.Field == "volumeSize");
    }

    [Fact]
    public void ValidateFull_CaddyWithInjectionChars_ReturnsError()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Proxy",
            Kind = ContainerKind.Caddy,
            Width = 200,
            Height = 100,
            Config = new() { ["domain"] = "example.com; rm -rf /" }
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Errors, e => e.Message.Contains("unsafe characters"));
    }

    [Fact]
    public void ValidateFull_WarningsDoNotBlockDeploy()
    {
        var topology = new Topology { Name = "Test" };
        topology.TierProfiles.Add(new TierProfile { Id = "unused", Name = "Unused", ImageSpecs = new() });
        topology.Containers.Add(new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.True(result.CanDeploy);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateFull_UnusedTierProfile_ReturnsWarning()
    {
        var topology = new Topology { Name = "Test" };
        topology.TierProfiles.Add(new TierProfile { Id = "custom", Name = "Custom", ImageSpecs = new() });
        topology.Containers.Add(new Container { Name = "Host", Kind = ContainerKind.Host, Width = 300, Height = 200 });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Warnings, w => w.Message.Contains("custom") && w.Message.Contains("not referenced"));
    }

    [Fact]
    public void ValidateFull_TierAgnosticPool_NoUnusedTierWarnings()
    {
        // A ComputePool with no tierProfile is tier-agnostic - all profiles are implicitly referenced
        var topology = new Topology { Name = "Test" };
        topology.TierProfiles.Add(new TierProfile { Id = "free", Name = "Free", ImageSpecs = new() });
        topology.TierProfiles.Add(new TierProfile { Id = "pro", Name = "Pro", ImageSpecs = new() });
        topology.Containers.Add(new Container
        {
            Name = "Pool",
            Kind = ContainerKind.ComputePool,
            Width = 300, Height = 200,
            Config = new() // no tierProfile key
        });

        var result = _validator.ValidateFull(topology);

        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("not referenced"));
    }

    [Fact]
    public void ValidateFull_InvalidBackupFrequency_ReturnsWarning()
    {
        var topology = new Topology { Name = "Test" };
        topology.Containers.Add(new Container
        {
            Name = "Host",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Config = new() { ["backupFrequency"] = "monthly" }
        });

        var result = _validator.ValidateFull(topology);

        Assert.Contains(result.Warnings, w => w.Message.Contains("monthly") && w.Message.Contains("silently ignored"));
    }
}
