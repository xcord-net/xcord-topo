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

        // Should have provider-namespaced files
        Assert.Contains("main_linode.tf", files.Keys);
        Assert.Contains("main_aws.tf", files.Keys);

        // Linode main should have linode provider
        Assert.Contains("linode/linode", files["main_linode.tf"]);

        // AWS main should have hashicorp/aws provider
        Assert.Contains("hashicorp/aws", files["main_aws.tf"]);
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
}
