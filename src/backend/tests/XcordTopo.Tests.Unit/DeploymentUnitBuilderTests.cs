using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Features.Terraform;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

/// <summary>
/// Verifies that initial deploy only creates concrete hub-critical infrastructure.
/// DataPool and ComputePool resources exist in HCL but default to count=0,
/// so they're created later when the hub triggers provisioning.
/// </summary>
public sealed class StagedDeploymentTests
{
    private readonly MultiProviderHclGenerator _generator;

    public StagedDeploymentTests()
    {
        var linode = new LinodeProvider();
        var aws = new AwsProvider();
        var registry = new ProviderRegistry([linode, aws]);
        _generator = new MultiProviderHclGenerator(registry);
    }

    /// <summary>
    /// ComputePool host count must default to 0 - pool infra is deferred.
    /// Hub sets this > 0 when it needs to provision federation servers.
    /// </summary>
    [Fact]
    public void GenerateHcl_ComputePoolHostCount_DefaultsToZero()
    {
        var topology = CreateProductionTopology();
        var files = _generator.Generate(topology);

        var vars = files["variables.tf"];

        // Tier-qualified: compute_pool_free_host_count (one per tier profile)
        Assert.Contains("compute_pool_free_host_count", vars);
        Assert.Matches(@"variable\s+""compute_pool_free_host_count""[^}]*default\s*=\s*0\b", vars);
    }

    /// <summary>
    /// DataPool must have a count variable defaulting to 0 - tenant data infra is deferred.
    /// Hub sets this to 1 when it provisions the first tenant that needs a data pool.
    /// </summary>
    [Fact]
    public void GenerateHcl_DataPoolCount_DefaultsToZero()
    {
        var topology = CreateProductionTopology();
        var files = _generator.Generate(topology);

        var vars = files["variables.tf"];

        // DataPool needs a count variable (doesn't exist today)
        Assert.Contains("data_pool_count", vars);
        Assert.Matches(@"variable\s+""data_pool_count""[^}]*default\s*=\s*0\b", vars);
    }

    /// <summary>
    /// Caddy Caddyfile must NOT have a static reference to compute_pool IP.
    /// When compute_pool_host_count=0 there are no pool instances - any static
    /// reference like compute_pool[0].private_ip would be an invalid Terraform ref.
    /// Hub configures wildcard tenant routing via Caddy admin API at runtime.
    /// </summary>
    [Fact]
    public void GenerateHcl_CaddyProvisioner_NoStaticComputePoolReference()
    {
        var topology = CreateProductionTopology();
        var files = _generator.Generate(topology);

        var provisioning = files["provisioning_aws.tf"];

        // Extract the Caddy provisioner section
        var caddyStart = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyStart >= 0, "Caddy provisioner must exist");

        // Find the next provisioner or end of file
        var afterCaddy = provisioning[(caddyStart + "provision_caddy".Length)..];
        var nextProvisioner = afterCaddy.IndexOf("null_resource");
        var caddySection = nextProvisioner >= 0 ? afterCaddy[..nextProvisioner] : afterCaddy;

        // Caddy must NOT reference compute_pool - it won't exist at initial deploy
        Assert.DoesNotContain("compute_pool", caddySection);
    }

    /// <summary>
    /// GetHostingOptions must return BOTH ComputePool and DataPool in the pools section,
    /// even when the topology has no explicit tierProfile config on the ComputePool.
    /// Uses the real production-simple fixture to catch config gaps.
    /// </summary>
    [Fact]
    public async Task GetHostingOptions_ProductionSimple_ReturnsComputePoolAndDataPoolInPools()
    {
        var topology = DeserializeFixture("production-simple.json");
        var registry = new ProviderRegistry([new LinodeProvider(), new AwsProvider()]);
        var store = new InMemoryTopologyStore(topology);
        var handler = new GetHostingOptionsHandler(store, registry);

        var result = await handler.Handle(
            new GetHostingOptionsRequest(topology.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, "Handler should succeed");
        var response = result.Value;

        // ComputePool emits one entry per tier profile
        var computePools = response.Pools.Where(p => p.PoolName == "Compute Pool").ToList();
        Assert.True(computePools.Count >= 2,
            $"Expected multiple tier entries for ComputePool, got {computePools.Count}");

        // Each tier must have viable plan options with tenant density
        foreach (var cp in computePools)
        {
            Assert.True(cp.Options.Count > 0,
                $"ComputePool tier '{cp.TierProfileName}' must have viable plan options");
            Assert.True(cp.Options.All(o => o.TenantsPerHost > 0),
                $"ComputePool tier '{cp.TierProfileName}' options must all have tenantsPerHost > 0");
        }

        // Higher tiers fit fewer tenants per host (more RAM per tenant)
        var tierNames = computePools.Select(p => p.TierProfileName).ToList();
        Assert.Contains("Free Tier", tierNames);
        Assert.Contains("Enterprise Tier", tierNames);

        // DataPool still appears
        var dataPool = response.Pools.First(p => p.PoolName == "Data Pool");
        Assert.True(dataPool.Options.Count > 0,
            "DataPool must have viable plan options");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static Topology DeserializeFixture(string name)
    {
        var assembly = typeof(StagedDeploymentTests).Assembly;
        var resourceName = $"XcordTopo.Tests.Unit.Fixtures.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Fixture not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Topology>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize fixture");
    }

    private sealed class InMemoryTopologyStore(Topology topology) : ITopologyStore
    {
        public Task<List<Topology>> ListAsync(CancellationToken ct = default) => Task.FromResult(new List<Topology> { topology });
        public Task<Topology?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Topology?>(id == topology.Id ? topology : null);
        public Task SaveAsync(Topology t, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Topology factory - production-like with all node types
    // -------------------------------------------------------------------------

    private static Topology CreateProductionTopology()
    {
        var hubServerPgPort = new Port { Id = Guid.NewGuid(), Name = "pg", Type = PortType.Database, Direction = PortDirection.Out };
        var hubServerRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.Out };
        var hubServer = new Image
        {
            Id = Guid.NewGuid(), Name = "hub_server", Kind = ImageKind.HubServer,
            Width = 140, Height = 60,
            Ports = [new() { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In }, hubServerPgPort, hubServerRedisPort],
            Config = new() { ["replicas"] = "1-3" },
            Scaling = ImageScaling.Shared
        };

        var liveKitRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.Out };
        var liveKit = new Image
        {
            Id = Guid.NewGuid(), Name = "live_kit", Kind = ImageKind.LiveKit,
            Width = 120, Height = 50,
            Ports = [
                new() { Id = Guid.NewGuid(), Name = "rtc", Type = PortType.Network, Direction = PortDirection.In },
                new() { Id = Guid.NewGuid(), Name = "api", Type = PortType.Network, Direction = PortDirection.In },
                liveKitRedisPort
            ],
            Config = new() { ["replicas"] = "1-10" },
            Scaling = ImageScaling.Shared
        };

        var redisLiveKitPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redisLiveKit = new Image
        {
            Id = Guid.NewGuid(), Name = "redis_livekit", Kind = ImageKind.Redis,
            Width = 120, Height = 50, Ports = [redisLiveKitPort], Scaling = ImageScaling.Shared
        };

        var redisHubPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redisHub = new Image
        {
            Id = Guid.NewGuid(), Name = "redis_hub", Kind = ImageKind.Redis,
            Width = 120, Height = 50, Ports = [redisHubPort], Scaling = ImageScaling.Shared
        };

        var pgHubPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pgHub = new Image
        {
            Id = Guid.NewGuid(), Name = "pg_hub", Kind = ImageKind.PostgreSQL,
            Width = 120, Height = 50, Ports = [pgHubPort], Scaling = ImageScaling.Shared
        };

        var registry = new Image
        {
            Id = Guid.NewGuid(), Name = "registry", Kind = ImageKind.Registry,
            Width = 140, Height = 60, Ports = [],
            Config = new() { ["domain"] = "docker.xcord.net" },
            Scaling = ImageScaling.Shared
        };

        var dataPool = new Container
        {
            Id = Guid.NewGuid(), Name = "Data Pool", Kind = ContainerKind.DataPool,
            Width = 200, Height = 245,
            Images =
            [
                new Image { Id = Guid.NewGuid(), Name = "pg", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In }],
                    Scaling = ImageScaling.Shared },
                new Image { Id = Guid.NewGuid(), Name = "redis", Kind = ImageKind.Redis, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In }],
                    Scaling = ImageScaling.Shared },
                new Image { Id = Guid.NewGuid(), Name = "mio", Kind = ImageKind.MinIO, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "s3", Type = PortType.Storage, Direction = PortDirection.In }],
                    Scaling = ImageScaling.Shared }
            ]
        };

        var computePool = new Container
        {
            Id = Guid.NewGuid(), Name = "Compute Pool", Kind = ContainerKind.ComputePool,
            Width = 225, Height = 366,
            Config = new() { ["tierProfile"] = "free" },
            Images =
            [
                new Image { Id = Guid.NewGuid(), Name = "fed_free", Kind = ImageKind.FederationServer, Width = 140, Height = 60,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In }],
                    Scaling = ImageScaling.PerTenant }
            ]
        };

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 878, Height = 430,
            Images = [hubServer, liveKit, redisLiveKit, redisHub, pgHub, registry],
            Children = [dataPool, computePool],
            Config = new() { ["domain"] = "xcord.net" }
        };

        var dnsContainer = new Container
        {
            Id = Guid.NewGuid(), Name = "XCord Net", Kind = ContainerKind.Dns,
            Width = 925, Height = 497,
            Children = [caddy],
            Config = new() { ["domain"] = "xcord.net", ["provider"] = "linode" }
        };

        var wires = new List<Wire>
        {
            new() { FromNodeId = hubServer.Id, FromPortId = hubServerPgPort.Id, ToNodeId = pgHub.Id, ToPortId = pgHubPort.Id },
            new() { FromNodeId = hubServer.Id, FromPortId = hubServerRedisPort.Id, ToNodeId = redisHub.Id, ToPortId = redisHubPort.Id },
            new() { FromNodeId = liveKit.Id, FromPortId = liveKitRedisPort.Id, ToNodeId = redisLiveKit.Id, ToPortId = redisLiveKitPort.Id },
        };

        return new Topology
        {
            Name = "Production - Simple",
            Provider = "aws",
            Containers = [dnsContainer],
            Wires = wires,
            ServiceKeys = new()
            {
                ["registry_url"] = "docker.xcord.net",
                ["registry_username"] = "docker_admin",
                ["smtp_host"] = "mail.xcord.net",
                ["smtp_username"] = "admin@mail.xcord.net",
                ["smtp_from_address"] = "noreply@xcord.net",
                ["smtp_from_name"] = "xcord-net",
            },
            TierProfiles =
            [
                new() { Id = "free", Name = "Free Tier", ImageSpecs = new()
                    { ["FederationServer"] = new() { MemoryMb = 192, CpuMillicores = 100, DiskMb = 256 } } }
            ],
            Registry = "docker.xcord.net"
        };
    }
}
