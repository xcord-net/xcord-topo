using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class CollectPublicEndpointsTests
{
    /// <summary>
    /// Builds a topology with a Caddy → Host containing HubServer, LiveKit, and a Registry image.
    /// The DNS container provides the domain. This mirrors the real xcord topology structure.
    /// </summary>
    private static Topology BuildTopologyWithCaddyAndRegistry(
        string domain = "xcord.net",
        string registryName = "registry")
    {
        var hubHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };
        var lkHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };

        var hub = new Image
        {
            Id = Guid.NewGuid(), Name = "hub_server", Kind = ImageKind.HubServer,
            Ports = [hubHttpPort], Width = 120, Height = 50
        };
        var liveKit = new Image
        {
            Id = Guid.NewGuid(), Name = "live_kit", Kind = ImageKind.LiveKit,
            Ports = [lkHttpPort], Width = 120, Height = 50
        };
        var registry = new Image
        {
            Id = Guid.NewGuid(), Name = registryName, Kind = ImageKind.Registry,
            Ports = [], Width = 120, Height = 50
        };

        var hubHost = new Container
        {
            Id = Guid.NewGuid(), Name = "hub_server", Kind = ContainerKind.Host,
            Images = [hub], Width = 300, Height = 200
        };
        var lkHost = new Container
        {
            Id = Guid.NewGuid(), Name = "live_kit", Kind = ContainerKind.Host,
            Images = [liveKit], Width = 300, Height = 200
        };
        var regHost = new Container
        {
            Id = Guid.NewGuid(), Name = "registry_host", Kind = ContainerKind.Host,
            Images = [registry], Width = 300, Height = 200
        };

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Children = [hubHost, lkHost, regHost], Width = 800, Height = 600
        };

        var dns = new Container
        {
            Id = Guid.NewGuid(), Name = "DNS", Kind = ContainerKind.Dns,
            Config = new Dictionary<string, string> { ["domain"] = domain },
            Width = 200, Height = 100
        };

        return new Topology
        {
            Name = "test",
            Provider = "aws",
            Containers = [caddy, dns],
            Wires = []
        };
    }

    [Fact]
    public void CollectPublicEndpoints_IncludesCaddyRoutedEndpoints()
    {
        var topology = BuildTopologyWithCaddyAndRegistry();
        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);

        var urls = endpoints.Select(e => e.Url).ToList();
        Assert.Contains("https://www.xcord.net", urls);
        Assert.Contains("https://live-kit.xcord.net", urls);
    }

    [Fact]
    public void CollectPublicEndpoints_IncludesApexDomain()
    {
        var topology = BuildTopologyWithCaddyAndRegistry();
        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);

        var apex = endpoints.Single(e => e.Url == "https://xcord.net");
        Assert.Equal("apex", apex.Kind);
    }

    [Fact]
    public void CollectPublicEndpoints_RegistrySubdomainDerivedFromName()
    {
        // Registry gets its subdomain from the image name, same as other images
        var topology = BuildTopologyWithCaddyAndRegistry();
        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);

        var reg = endpoints.Single(e => e.Url == "https://registry.xcord.net");
        Assert.Equal("reverse_proxy", reg.Kind);
        Assert.Contains("registry:5000", reg.Backend!);
    }

    [Fact]
    public void CollectPublicEndpoints_RegistryNameControlsSubdomain()
    {
        // If registry is named "docker", its subdomain should be "docker"
        var topology = BuildTopologyWithCaddyAndRegistry(registryName: "docker");
        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);

        Assert.Contains(endpoints, e => e.Url == "https://docker.xcord.net");
    }

    [Fact]
    public void CollectPublicEndpoints_NoDuplicateUrls()
    {
        var topology = BuildTopologyWithCaddyAndRegistry();
        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);

        var urls = endpoints.Select(e => e.Url).ToList();
        Assert.Equal(urls.Distinct(StringComparer.OrdinalIgnoreCase).Count(), urls.Count);
    }

    [Fact]
    public void CollectPublicEndpoints_NonPublicImages_NotIncluded()
    {
        // PostgreSQL has IsPublicEndpoint = false - should never appear in endpoints
        var pg = new Image
        {
            Id = Guid.NewGuid(), Name = "pg", Kind = ImageKind.PostgreSQL,
            Ports = [], Width = 120, Height = 50
        };

        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "db-host", Kind = ContainerKind.Host,
            Images = [pg], Width = 300, Height = 200
        };

        var topology = new Topology
        {
            Name = "test",
            Provider = "aws",
            Containers = [host],
            Wires = []
        };

        var endpoints = TopologyHelpers.CollectPublicEndpoints(topology);
        Assert.DoesNotContain(endpoints, e => e.Url.Contains("pg"));
    }
}
