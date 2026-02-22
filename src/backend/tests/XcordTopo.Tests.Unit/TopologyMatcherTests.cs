using XcordTopo.Infrastructure.Migration;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class TopologyMatcherTests
{
    private readonly TopologyMatcher _matcher = new();

    /// <summary>
    /// Build a "Simple" topology: single host with Caddy (Hub + Fed images), shared PG, Redis, MinIO.
    /// </summary>
    private static Topology BuildSimpleTopology()
    {
        // Shared infrastructure images
        var pgPort = new Port { Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pg = new Image
        {
            Name = "PostgreSQL", Kind = ImageKind.PostgreSQL,
            Ports = [pgPort], Width = 120, Height = 50
        };

        var redisPort = new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redis = new Image
        {
            Name = "Redis", Kind = ImageKind.Redis,
            Ports = [redisPort], Width = 120, Height = 50
        };

        var minioPort = new Port { Name = "s3", Type = PortType.Storage, Direction = PortDirection.In };
        var minio = new Image
        {
            Name = "MinIO", Kind = ImageKind.MinIO,
            Ports = [minioPort], Width = 120, Height = 50
        };

        // Hub Server image (inside Caddy)
        var hubPgOut = new Port { Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubRedisOut = new Port { Name = "redis_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubHttp = new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In };
        var hub = new Image
        {
            Name = "Hub Server", Kind = ImageKind.HubServer,
            Ports = [hubHttp, hubPgOut, hubRedisOut], Width = 140, Height = 60,
            Config = new() { ["upstreamPath"] = "/hub/*" }
        };

        // Federation Server image (inside Caddy)
        var fedPgOut = new Port { Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var fedRedisOut = new Port { Name = "redis_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var fedMinioOut = new Port { Name = "s3_connection", Type = PortType.Storage, Direction = PortDirection.Out };
        var fedHttp = new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In };
        var fed = new Image
        {
            Name = "Federation Server", Kind = ImageKind.FederationServer,
            Ports = [fedHttp, fedPgOut, fedRedisOut, fedMinioOut], Width = 140, Height = 60,
            Config = new() { ["upstreamPath"] = "/*" }
        };

        // Caddy container with Hub + Fed images
        var caddy = new Container
        {
            Name = "Caddy", Kind = ContainerKind.Caddy,
            Images = [hub, fed], Width = 400, Height = 200
        };

        // Single Host with everything
        var server = new Container
        {
            Name = "server", Kind = ContainerKind.Host,
            Images = [pg, redis, minio], Children = [caddy],
            Width = 600, Height = 500
        };

        return new Topology
        {
            Name = "Simple",
            Containers = [server],
            Wires =
            [
                new Wire { FromNodeId = hub.Id, FromPortId = hubPgOut.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id },
                new Wire { FromNodeId = hub.Id, FromPortId = hubRedisOut.Id, ToNodeId = redis.Id, ToPortId = redisPort.Id },
                new Wire { FromNodeId = fed.Id, FromPortId = fedPgOut.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id },
                new Wire { FromNodeId = fed.Id, FromPortId = fedRedisOut.Id, ToNodeId = redis.Id, ToPortId = redisPort.Id },
                new Wire { FromNodeId = fed.Id, FromPortId = fedMinioOut.Id, ToNodeId = minio.Id, ToPortId = minioPort.Id },
            ]
        };
    }

    /// <summary>
    /// Build a "Robust" topology: proxy-host (Caddy), hub-host (Hub + PG + Redis),
    /// media-host (LiveKit), FederationGroup with fed-host template (Fed + PG + Redis + MinIO).
    /// </summary>
    private static Topology BuildRobustTopology()
    {
        // --- proxy-host: standalone Caddy ---
        var proxyCaddy = new Container
        {
            Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 300, Height = 150
        };
        var proxyHost = new Container
        {
            Name = "proxy-host", Kind = ContainerKind.Host,
            Children = [proxyCaddy], Width = 400, Height = 300
        };

        // --- hub-host: Hub Server (×3) + dedicated PG + dedicated Redis ---
        var hubPgPort = new Port { Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var hubPg = new Image
        {
            Name = "PostgreSQL", Kind = ImageKind.PostgreSQL,
            Ports = [hubPgPort], Width = 120, Height = 50
        };

        var hubRedisPort = new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var hubRedis = new Image
        {
            Name = "Redis", Kind = ImageKind.Redis,
            Ports = [hubRedisPort], Width = 120, Height = 50
        };

        var hubPgOut = new Port { Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubRedisOut = new Port { Name = "redis_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var hubHttp = new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In };
        var hubServer = new Image
        {
            Name = "Hub Server", Kind = ImageKind.HubServer,
            Ports = [hubHttp, hubPgOut, hubRedisOut], Width = 140, Height = 60,
            Config = new() { ["replicas"] = "3" }
        };

        var hubHost = new Container
        {
            Name = "hub-host", Kind = ContainerKind.Host,
            Images = [hubServer, hubPg, hubRedis], Width = 500, Height = 400
        };

        // --- media-host (×2 replicas): LiveKit ---
        var livekit = new Image
        {
            Name = "LiveKit", Kind = ImageKind.LiveKit,
            Width = 140, Height = 60
        };
        var mediaHost = new Container
        {
            Name = "media-host", Kind = ContainerKind.Host,
            Images = [livekit], Width = 300, Height = 200,
            Config = new() { ["replicas"] = "2" }
        };

        // --- FederationGroup with fed-host template ---
        var fedPgPort = new Port { Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var fedPg = new Image
        {
            Name = "PostgreSQL", Kind = ImageKind.PostgreSQL,
            Ports = [fedPgPort], Width = 120, Height = 50
        };

        var fedRedisPort = new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var fedRedis = new Image
        {
            Name = "Redis", Kind = ImageKind.Redis,
            Ports = [fedRedisPort], Width = 120, Height = 50
        };

        var fedMinioPort = new Port { Name = "s3", Type = PortType.Storage, Direction = PortDirection.In };
        var fedMinio = new Image
        {
            Name = "MinIO", Kind = ImageKind.MinIO,
            Ports = [fedMinioPort], Width = 120, Height = 50
        };

        var fedPgOut = new Port { Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var fedRedisOut = new Port { Name = "redis_connection", Type = PortType.Database, Direction = PortDirection.Out };
        var fedMinioOut = new Port { Name = "s3_connection", Type = PortType.Storage, Direction = PortDirection.Out };
        var fedHttp = new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In };
        var fedServer = new Image
        {
            Name = "Federation Server", Kind = ImageKind.FederationServer,
            Ports = [fedHttp, fedPgOut, fedRedisOut, fedMinioOut], Width = 140, Height = 60
        };

        var fedHost = new Container
        {
            Name = "fed-host", Kind = ContainerKind.Host,
            Images = [fedServer, fedPg, fedRedis, fedMinio], Width = 500, Height = 400
        };

        var fedGroup = new Container
        {
            Name = "federation", Kind = ContainerKind.FederationGroup,
            Children = [fedHost], Width = 600, Height = 500
        };

        // Wires within hub-host
        var hubWires = new List<Wire>
        {
            new() { FromNodeId = hubServer.Id, FromPortId = hubPgOut.Id, ToNodeId = hubPg.Id, ToPortId = hubPgPort.Id },
            new() { FromNodeId = hubServer.Id, FromPortId = hubRedisOut.Id, ToNodeId = hubRedis.Id, ToPortId = hubRedisPort.Id },
        };

        // Wires within fed-host
        var fedWires = new List<Wire>
        {
            new() { FromNodeId = fedServer.Id, FromPortId = fedPgOut.Id, ToNodeId = fedPg.Id, ToPortId = fedPgPort.Id },
            new() { FromNodeId = fedServer.Id, FromPortId = fedRedisOut.Id, ToNodeId = fedRedis.Id, ToPortId = fedRedisPort.Id },
            new() { FromNodeId = fedServer.Id, FromPortId = fedMinioOut.Id, ToNodeId = fedMinio.Id, ToPortId = fedMinioPort.Id },
        };

        return new Topology
        {
            Name = "Robust",
            Containers = [proxyHost, hubHost, mediaHost, fedGroup],
            Wires = [.. hubWires, .. fedWires]
        };
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsHubServerRelocated()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var hubMatch = result.ImageMatches.FirstOrDefault(m =>
            m.SourceImageKind == ImageKind.HubServer && m.TargetImageKind == ImageKind.HubServer);

        Assert.NotNull(hubMatch);
        Assert.Equal(ImageMatchKind.Relocated, hubMatch.Kind);
        Assert.Equal("hub-host", hubMatch.TargetHostName);
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsFedServerRelocated()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var fedMatch = result.ImageMatches.FirstOrDefault(m =>
            m.SourceImageKind == ImageKind.FederationServer && m.TargetImageKind == ImageKind.FederationServer);

        Assert.NotNull(fedMatch);
        Assert.Equal(ImageMatchKind.Relocated, fedMatch.Kind);
        Assert.True(fedMatch.TargetIsFederation);
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsPostgreSQLSplit()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var pgMatches = result.ImageMatches.Where(m =>
            m.SourceImageKind == ImageKind.PostgreSQL && m.Kind == ImageMatchKind.Split).ToList();

        // 1 source PG → 2 targets (hub PG + fed PG)
        Assert.Equal(2, pgMatches.Count);

        // One target should be in federation (fresh, no migration)
        Assert.Contains(pgMatches, m => m.TargetIsFederation);
        // One should be hub's PG (needs migration)
        Assert.Contains(pgMatches, m => !m.TargetIsFederation);
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsRedisSplit()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var redisMatches = result.ImageMatches.Where(m =>
            m.SourceImageKind == ImageKind.Redis && m.Kind == ImageMatchKind.Split).ToList();

        Assert.Equal(2, redisMatches.Count);
        Assert.Contains(redisMatches, m => m.TargetIsFederation);
        Assert.Contains(redisMatches, m => !m.TargetIsFederation);
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsLiveKitAdded()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var liveKitMatch = result.ImageMatches.FirstOrDefault(m =>
            m.TargetImageKind == ImageKind.LiveKit);

        Assert.NotNull(liveKitMatch);
        Assert.Equal(ImageMatchKind.Added, liveKitMatch.Kind);
        Assert.Null(liveKitMatch.SourceImageId);
    }

    [Fact]
    public void Match_SimpleToRobust_DetectsMinIORelocated()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var minioMatch = result.ImageMatches.FirstOrDefault(m =>
            m.SourceImageKind == ImageKind.MinIO);

        Assert.NotNull(minioMatch);
        // MinIO goes from server → federation's fed-host (relocated, 1:1)
        Assert.Equal(ImageMatchKind.Relocated, minioMatch.Kind);
        Assert.True(minioMatch.TargetIsFederation);
    }

    [Fact]
    public void Match_SimpleToRobust_GeneratesHubDatabaseDecision()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var dbDecision = result.Decisions.FirstOrDefault(d => d.Kind == DecisionKind.HubDatabaseMigration);
        Assert.NotNull(dbDecision);
        Assert.True(dbDecision.Required);
        Assert.Equal(3, dbDecision.Options.Count);
    }

    [Fact]
    public void Match_SimpleToRobust_GeneratesHubRedisDecision()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var redisDecision = result.Decisions.FirstOrDefault(d => d.Kind == DecisionKind.HubRedisMigration);
        Assert.NotNull(redisDecision);
        Assert.False(redisDecision.Required);
    }

    [Fact]
    public void Match_SimpleToRobust_SummaryIncludesAllChanges()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        Assert.True(result.HostsAdded > 0);
        Assert.True(result.ImagesRelocated > 0 || result.SplitsDetected > 0);
        Assert.True(result.ImagesAdded > 0); // LiveKit
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public void Match_SimpleToRobust_ContainerMatches_ServerIsSplitHost()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        // "server" container maps to multiple target hosts → SplitHost
        var serverMatches = result.ContainerMatches.Where(c =>
            c.SourceContainerName == "server").ToList();

        Assert.True(serverMatches.Count > 1 || serverMatches.Any(c => c.Kind == ContainerMatchKind.SplitHost));
    }

    [Fact]
    public void Match_SimpleToRobust_ContainerMatches_MediaHostIsAdded()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var mediaHost = result.ContainerMatches.FirstOrDefault(c =>
            c.TargetContainerName == "media-host");

        Assert.NotNull(mediaHost);
        Assert.Equal(ContainerMatchKind.Added, mediaHost.Kind);
    }

    [Fact]
    public void Match_IdenticalTopologies_AllUnchanged()
    {
        var topo = BuildSimpleTopology();
        // Match against itself — all images should be Unchanged
        var result = _matcher.Match(topo, topo);

        Assert.All(result.ImageMatches, m =>
            Assert.Equal(ImageMatchKind.Unchanged, m.Kind));
        Assert.Equal(0, result.HostsAdded);
        Assert.Equal(0, result.HostsRemoved);
    }

    [Fact]
    public void FlattenImages_SimpleTopology_FindsAllImages()
    {
        var simple = BuildSimpleTopology();

        var flat = TopologyMatcher.FlattenImages(simple);

        // 5 images: PG, Redis, MinIO on host; Hub, Fed in Caddy child
        Assert.Equal(5, flat.Count);
        Assert.Contains(flat, f => f.Image.Kind == ImageKind.HubServer);
        Assert.Contains(flat, f => f.Image.Kind == ImageKind.FederationServer);
        Assert.Contains(flat, f => f.Image.Kind == ImageKind.PostgreSQL);
        Assert.Contains(flat, f => f.Image.Kind == ImageKind.Redis);
        Assert.Contains(flat, f => f.Image.Kind == ImageKind.MinIO);
    }

    [Fact]
    public void FlattenImages_SimpleTopology_AllShareSameHost()
    {
        var simple = BuildSimpleTopology();

        var flat = TopologyMatcher.FlattenImages(simple);
        var hostIds = flat.Select(f => f.Host.Id).Distinct().ToList();

        Assert.Single(hostIds);
    }

    [Fact]
    public void FlattenImages_RobustTopology_FederationImagesHaveFedGroup()
    {
        var robust = BuildRobustTopology();

        var flat = TopologyMatcher.FlattenImages(robust);
        var fedImages = flat.Where(f => f.FederationGroup != null).ToList();

        // Fed Server, PG, Redis, MinIO all inside FederationGroup
        Assert.Equal(4, fedImages.Count);
    }

    [Fact]
    public void Match_SplitConsumerId_IsSetForWireBasedMatches()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        var pgSplitsWithConsumer = result.ImageMatches.Where(m =>
            m.Kind == ImageMatchKind.Split &&
            m.SourceImageKind == ImageKind.PostgreSQL &&
            m.SplitConsumerId.HasValue).ToList();

        // At least one PG split should have a consumer ID (the one matched by wire analysis)
        Assert.NotEmpty(pgSplitsWithConsumer);
    }

    [Fact]
    public void Match_FederationTargets_MarkedAsFresh()
    {
        var simple = BuildSimpleTopology();
        var robust = BuildRobustTopology();

        var result = _matcher.Match(simple, robust);

        // All federation-side targets should be marked as such
        var fedTargets = result.ImageMatches.Where(m => m.TargetIsFederation).ToList();
        Assert.True(fedTargets.Count >= 4); // Fed Server + PG + Redis + MinIO
    }
}
