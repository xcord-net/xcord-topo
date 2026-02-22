using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class WireResolverTests
{
    private static Topology BuildWiredTopology()
    {
        // Host with HubServer wired to PostgreSQL and Redis
        var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var redisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };

        var hubPgPort = new Port { Id = Guid.NewGuid(), Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };

        var pg = new Image
        {
            Id = Guid.NewGuid(), Name = "PostgreSQL", Kind = ImageKind.PostgreSQL,
            Ports = [pgPort], Width = 120, Height = 50
        };
        var redis = new Image
        {
            Id = Guid.NewGuid(), Name = "Redis", Kind = ImageKind.Redis,
            Ports = [redisPort], Width = 120, Height = 50
        };
        var hub = new Image
        {
            Id = Guid.NewGuid(), Name = "Hub Server", Kind = ImageKind.HubServer,
            Ports = [hubHttpPort, hubPgPort, hubRedisPort], Width = 140, Height = 60
        };

        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "hub-host", Kind = ContainerKind.Host,
            Images = [hub, pg, redis], Width = 400, Height = 300
        };

        var topology = new Topology
        {
            Name = "test",
            Containers = [host],
            Wires =
            [
                new Wire { FromNodeId = hub.Id, FromPortId = hubPgPort.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id },
                new Wire { FromNodeId = hub.Id, FromPortId = hubRedisPort.Id, ToNodeId = redis.Id, ToPortId = redisPort.Id }
            ]
        };

        return topology;
    }

    [Fact]
    public void ResolveOutgoing_FindsWiredTarget()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var hub = topology.Containers[0].Images[0]; // Hub Server
        var pg = topology.Containers[0].Images[1]; // PostgreSQL

        var result = resolver.ResolveOutgoing(hub.Id, "pg_connection");

        Assert.NotNull(result);
        Assert.Equal(pg.Id, ((Image)result.Value.Node).Id);
    }

    [Fact]
    public void ResolveIncoming_FindsAllSources()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var pg = topology.Containers[0].Images[1]; // PostgreSQL

        var results = resolver.ResolveIncoming(pg.Id, "postgres");

        Assert.Single(results);
        var sourceImage = results[0].Node as Image;
        Assert.NotNull(sourceImage);
        Assert.Equal(ImageKind.HubServer, sourceImage.Kind);
    }

    [Fact]
    public void AreOnSameHost_SameHost_ReturnsTrue()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var hub = topology.Containers[0].Images[0];
        var pg = topology.Containers[0].Images[1];

        Assert.True(resolver.AreOnSameHost(hub.Id, pg.Id));
    }

    [Fact]
    public void AreOnSameHost_DifferentHosts_ReturnsFalse()
    {
        var host1 = new Container
        {
            Id = Guid.NewGuid(), Name = "host-1", Kind = ContainerKind.Host,
            Images = [new Image { Id = Guid.NewGuid(), Name = "Img1", Kind = ImageKind.Redis, Width = 100, Height = 50 }],
            Width = 300, Height = 200
        };
        var host2 = new Container
        {
            Id = Guid.NewGuid(), Name = "host-2", Kind = ContainerKind.Host,
            Images = [new Image { Id = Guid.NewGuid(), Name = "Img2", Kind = ImageKind.PostgreSQL, Width = 100, Height = 50 }],
            Width = 300, Height = 200
        };
        var topology = new Topology { Containers = [host1, host2] };
        var resolver = new WireResolver(topology);

        Assert.False(resolver.AreOnSameHost(host1.Images[0].Id, host2.Images[0].Id));
    }

    [Fact]
    public void ResolveCaddyUpstreams_ReturnsWiredImages()
    {
        var hubImage = new Image
        {
            Id = Guid.NewGuid(), Name = "Hub Server", Kind = ImageKind.HubServer,
            Width = 140, Height = 60,
            Config = new() { ["upstreamPath"] = "/hub/*" }
        };
        var fedImage = new Image
        {
            Id = Guid.NewGuid(), Name = "Fed Server", Kind = ImageKind.FederationServer,
            Width = 140, Height = 60,
            Config = new() { ["upstreamPath"] = "/*" }
        };
        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Images = [hubImage, fedImage], Width = 400, Height = 300
        };
        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "server", Kind = ContainerKind.Host,
            Children = [caddy], Width = 500, Height = 500
        };
        var topology = new Topology { Containers = [host] };
        var resolver = new WireResolver(topology);

        var upstreams = resolver.ResolveCaddyUpstreams(caddy);

        Assert.Equal(2, upstreams.Count);
        Assert.Contains(upstreams, u => u.UpstreamPath == "/hub/*");
        Assert.Contains(upstreams, u => u.UpstreamPath == "/*");
    }

    [Fact]
    public void FindHostFor_ReturnsNearestHostAncestor()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var pg = topology.Containers[0].Images[1]; // PostgreSQL
        var host = topology.Containers[0]; // hub-host

        var result = resolver.FindHostFor(pg.Id);

        Assert.NotNull(result);
        Assert.Equal(host.Id, result.Id);
    }

    [Fact]
    public void ResolveWiredImage_ReturnsTargetImage()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var hub = topology.Containers[0].Images[0]; // Hub Server
        var pg = topology.Containers[0].Images[1]; // PostgreSQL

        var result = resolver.ResolveWiredImage(hub.Id, "pg_connection");

        Assert.NotNull(result);
        Assert.Equal(pg.Id, result.Id);
    }

    [Fact]
    public void ResolveOutgoing_NonExistentPort_ReturnsNull()
    {
        var topology = BuildWiredTopology();
        var resolver = new WireResolver(topology);
        var hub = topology.Containers[0].Images[0];

        var result = resolver.ResolveOutgoing(hub.Id, "nonexistent_port");

        Assert.Null(result);
    }
}
