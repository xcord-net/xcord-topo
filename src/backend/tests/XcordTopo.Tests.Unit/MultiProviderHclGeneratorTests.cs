using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class MultiProviderHclGeneratorTests
{
    private readonly MultiProviderHclGenerator _generator;

    public MultiProviderHclGeneratorTests()
    {
        var linode = new LinodeProvider();
        var aws = new AwsProvider();
        var registry = new ProviderRegistry([linode, aws]);
        _generator = new MultiProviderHclGenerator(registry);
    }

    [Fact]
    public void SingleProvider_DelegatesToLegacyGenerateHcl()
    {
        var topology = new Topology
        {
            Name = "test-topo",
            Provider = "linode",
            Containers =
            [
                new Container { Name = "web-server", Kind = ContainerKind.Host, Width = 300, Height = 200 }
            ]
        };

        var files = _generator.Generate(topology);

        // Should produce the standard Linode file set
        Assert.Contains("main.tf", files.Keys);
        Assert.Contains("instances.tf", files.Keys);
        Assert.Contains("firewall.tf", files.Keys);
        Assert.Contains("linode_instance", files["instances.tf"]);
    }

    [Fact]
    public void TwoProviders_MainTfContainsBothProviderBlocks()
    {
        var topology = new Topology
        {
            Name = "multi-topo",
            Provider = "linode",
            Containers =
            [
                new Container { Name = "web-server", Kind = ContainerKind.Host, Width = 300, Height = 200 },
                new Container
                {
                    Name = "api-server", Kind = ContainerKind.Host, Width = 300, Height = 200,
                    Config = new Dictionary<string, string> { ["provider"] = "aws" }
                }
            ]
        };

        var files = _generator.Generate(topology);

        // Multi-provider produces a unified main.tf with both providers
        Assert.Contains("main.tf", files.Keys);

        // Unified main should have both providers
        Assert.Contains("linode/linode", files["main.tf"]);
        Assert.Contains("hashicorp/aws", files["main.tf"]);
    }

    [Fact]
    public void DnsContainerLinode_WiredToAwsHost_GeneratesCrossProviderRef()
    {
        var dnsPort = new Port
        {
            Id = Guid.NewGuid(), Name = "records", Type = PortType.Network,
            Direction = PortDirection.In, Side = PortSide.Left
        };
        var hostPort = new Port
        {
            Id = Guid.NewGuid(), Name = "public", Type = PortType.Network,
            Direction = PortDirection.InOut, Side = PortSide.Right
        };

        var awsHost = new Container
        {
            Id = Guid.NewGuid(),
            Name = "web-server",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Config = new Dictionary<string, string> { ["provider"] = "aws" },
            Ports = [hostPort]
        };

        var dnsContainer = new Container
        {
            Id = Guid.NewGuid(),
            Name = "DNS",
            Kind = ContainerKind.Dns,
            Width = 300,
            Height = 160,
            Config = new Dictionary<string, string> { ["provider"] = "linode", ["domain"] = "example.com" },
            Ports = [dnsPort]
        };

        var topology = new Topology
        {
            Name = "cross-provider",
            Provider = "linode",
            Containers = [awsHost, dnsContainer],
            Wires =
            [
                new Wire
                {
                    FromNodeId = awsHost.Id,
                    FromPortId = hostPort.Id,
                    ToNodeId = dnsContainer.Id,
                    ToPortId = dnsPort.Id
                }
            ]
        };

        var files = _generator.Generate(topology);

        // DNS file should exist under linode
        Assert.Contains("dns_linode.tf", files.Keys);
        var dns = files["dns_linode.tf"];

        // Should have linode_domain data source
        Assert.Contains("linode_domain", dns);
        // Should have linode_domain_record
        Assert.Contains("linode_domain_record", dns);
        // Should reference AWS instance IP (cross-provider)
        Assert.Contains("aws_instance.web_server", dns);
    }

    [Fact]
    public void DnsContainerWithoutDomain_SkippedSilently()
    {
        var dnsPort = new Port
        {
            Id = Guid.NewGuid(), Name = "records", Type = PortType.Network,
            Direction = PortDirection.In
        };

        var dnsContainer = new Container
        {
            Id = Guid.NewGuid(),
            Name = "DNS",
            Kind = ContainerKind.Dns,
            Width = 300,
            Height = 160,
            Config = new Dictionary<string, string> { ["provider"] = "linode" },
            Ports = [dnsPort]
        };

        var topology = new Topology
        {
            Name = "test-topo",
            Provider = "linode",
            Containers = [dnsContainer]
        };

        var files = _generator.Generate(topology);

        // DNS file should exist but have no domain_record resources
        if (files.TryGetValue("dns.tf", out var dns))
            Assert.DoesNotContain("linode_domain_record", dns);
    }

    [Fact]
    public void CollectActiveProviderKeys_ReturnsAllProviders()
    {
        var topology = new Topology
        {
            Name = "test",
            Provider = "linode",
            Containers =
            [
                new Container
                {
                    Name = "host1", Kind = ContainerKind.Host, Width = 300, Height = 200,
                    Config = new Dictionary<string, string> { ["provider"] = "aws" }
                },
                new Container { Name = "host2", Kind = ContainerKind.Host, Width = 300, Height = 200 }
            ]
        };

        var keys = TopologyHelpers.CollectActiveProviderKeys(topology);

        Assert.Contains("linode", keys);
        Assert.Contains("aws", keys);
        Assert.Equal(2, keys.Count);
    }

    // --- Issue 1: ComputePool DNS record uses non-indexed reference to counted resource ---

    [Fact]
    public void GenerateHcl_DnsAutoDiscovery_ExcludesComputePool()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);

        Assert.Contains("dns_linode.tf", files.Keys);
        var dns = files["dns_linode.tf"];

        // Caddy should get DNS records (wildcard + apex)
        Assert.Contains("linode_domain_record\" \"caddy\"", dns);
        Assert.Contains("linode_domain_record\" \"wildcard\"", dns);

        // ComputePool should NOT get a DNS record - it's internal infrastructure
        // accessed via Caddy reverse proxy, not direct DNS
        Assert.DoesNotContain("compute_pool", dns);
    }

    [Fact]
    public void GenerateHcl_DnsAutoDiscovery_ExcludesDataPool()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);

        Assert.Contains("dns_linode.tf", files.Keys);
        var dns = files["dns_linode.tf"];

        // DataPool should NOT get a DNS record - PG/Redis/MinIO are backend infra
        Assert.DoesNotContain("data_pool", dns);
    }

    // --- Issue 3: Sensitive service key variables missing from multi-provider HCL ---

    [Fact]
    public void GenerateHcl_ServiceKeyVariables_IncludesAllGroupMembers()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);
        var vars = files["variables.tf"];

        // topology.ServiceKeys has registry_url and registry_username
        // but NOT registry_password (stored in credential store as sensitive)
        // All group members should still be emitted as variables
        Assert.Contains("variable \"registry_url\"", vars);
        Assert.Contains("variable \"registry_username\"", vars);
        Assert.Contains("variable \"registry_password\"", vars);
    }

    [Fact]
    public void GenerateHcl_ServiceKeyVariables_SmtpGroupComplete()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);
        var vars = files["variables.tf"];

        // topology.ServiceKeys has smtp_host, smtp_username, smtp_from_address, smtp_from_name
        // but NOT smtp_password or smtp_port - all group members should be emitted
        Assert.Contains("variable \"smtp_host\"", vars);
        Assert.Contains("variable \"smtp_password\"", vars);
        Assert.Contains("variable \"smtp_port\"", vars);
    }

    // --- Issue 7: Apex DNS record when Caddy domain matches DNS zone ---

    [Fact]
    public void GenerateHcl_CaddyDomainMatchesDnsZone_GeneratesApexRecord()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);

        Assert.Contains("dns_linode.tf", files.Keys);
        var dns = files["dns_linode.tf"];

        // When Caddy domain (xcord.net) matches DNS zone domain (xcord.net),
        // an apex A record should be generated alongside the wildcard
        Assert.Contains("linode_domain_record\" \"apex\"", dns);
    }

    // --- Helper: Production - Simple topology (multi-provider) ---

    private static Topology CreateProductionSimpleTopology()
    {
        // Hub-level images on Caddy
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

        // DataPool with PG, Redis, MinIO
        var dataPool = new Container
        {
            Id = Guid.NewGuid(), Name = "Data Pool", Kind = ContainerKind.DataPool,
            Width = 200, Height = 245,
            Images =
            [
                new Image { Id = Guid.NewGuid(), Name = "pg", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In }], Scaling = ImageScaling.Shared },
                new Image { Id = Guid.NewGuid(), Name = "redis", Kind = ImageKind.Redis, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In }], Scaling = ImageScaling.Shared },
                new Image { Id = Guid.NewGuid(), Name = "mio", Kind = ImageKind.MinIO, Width = 120, Height = 50,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "s3", Type = PortType.Storage, Direction = PortDirection.In }], Scaling = ImageScaling.Shared }
            ],
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "ssh", Type = PortType.Control, Direction = PortDirection.In },
                new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut },
                new() { Id = Guid.NewGuid(), Name = "private", Type = PortType.Network, Direction = PortDirection.InOut }
            ]
        };

        // ComputePool with FederationServers
        var computePool = new Container
        {
            Id = Guid.NewGuid(), Name = "Compute Pool", Kind = ContainerKind.ComputePool,
            Width = 225, Height = 366,
            Images =
            [
                new Image { Id = Guid.NewGuid(), Name = "fed_free", Kind = ImageKind.FederationServer, Width = 140, Height = 60,
                    Ports = [new() { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In }], Scaling = ImageScaling.PerTenant },
            ],
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut },
                new() { Id = Guid.NewGuid(), Name = "control", Type = PortType.Control, Direction = PortDirection.In }
            ]
        };

        // Caddy (standalone - becomes its own host)
        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 878, Height = 430,
            Images = [hubServer, liveKit, redisLiveKit, redisHub, pgHub, registry],
            Children = [dataPool, computePool],
            Config = new() { ["domain"] = "xcord.net" },
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "http_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "https_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "upstream", Type = PortType.Network, Direction = PortDirection.Out, Side = PortSide.Bottom }
            ]
        };

        // DNS Zone on Linode
        var dnsContainer = new Container
        {
            Id = Guid.NewGuid(), Name = "XCord Net", Kind = ContainerKind.Dns,
            Width = 925, Height = 497,
            Children = [caddy],
            Config = new() { ["domain"] = "xcord.net", ["provider"] = "linode" },
            Ports = [new() { Id = Guid.NewGuid(), Name = "records", Type = PortType.Network, Direction = PortDirection.In }]
        };

        // Wires
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
                new() { Id = "free", Name = "Free Tier", ImageSpecs = new() { ["FederationServer"] = new() { MemoryMb = 192, CpuMillicores = 100, DiskMb = 256 } } },
            ],
            Registry = "docker.xcord.net"
        };
    }

    [Fact]
    public void LinodeProvider_GenerateHclForContainers_WithDns_ProducesRecords()
    {
        var provider = new LinodeProvider();

        var dnsPort = new Port
        {
            Id = Guid.NewGuid(), Name = "records", Type = PortType.Network,
            Direction = PortDirection.In, Side = PortSide.Left
        };
        var hostPort = new Port
        {
            Id = Guid.NewGuid(), Name = "public", Type = PortType.Network,
            Direction = PortDirection.InOut, Side = PortSide.Right
        };

        var host = new Container
        {
            Id = Guid.NewGuid(),
            Name = "web-host",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Ports = [hostPort]
        };

        var dns = new Container
        {
            Id = Guid.NewGuid(),
            Name = "DNS",
            Kind = ContainerKind.Dns,
            Width = 300,
            Height = 160,
            Config = new Dictionary<string, string> { ["domain"] = "example.com" },
            Ports = [dnsPort]
        };

        var topology = new Topology
        {
            Name = "test",
            Provider = "linode",
            Containers = [host, dns],
            Wires =
            [
                new Wire
                {
                    FromNodeId = host.Id, FromPortId = hostPort.Id,
                    ToNodeId = dns.Id, ToPortId = dnsPort.Id
                }
            ]
        };

        var files = provider.GenerateHclForContainers(topology, [host, dns]);

        Assert.Contains("dns_linode.tf", files.Keys);
        var dnsFile = files["dns_linode.tf"];
        Assert.Contains("linode_domain_record", dnsFile);
        Assert.Contains("linode_domain", dnsFile);
    }

    [Fact]
    public void GenerateHcl_RegistryVariables_NoDuplicates()
    {
        var topology = CreateProductionSimpleTopology();
        var files = _generator.Generate(topology);

        // Registry variables are hardcoded per-provider in GenerateVariables(),
        // so GenerateServiceKeyVariables should NOT emit them again.
        // Count occurrences - each should appear exactly once.
        foreach (var key in files.Keys.Where(k => k.StartsWith("variables")))
        {
            var content = files[key];
            var count = System.Text.RegularExpressions.Regex.Matches(content, @"variable ""registry_url""").Count;
            Assert.True(count <= 1, $"registry_url variable appears {count} times in {key}");
        }
    }
}
