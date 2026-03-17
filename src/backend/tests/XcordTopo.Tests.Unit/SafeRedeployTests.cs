using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class SafeRedeployTests
{
    private readonly AwsProvider _awsProvider = new();
    private readonly LinodeProvider _linodeProvider = new();

    private static Topology CreateMinimalTopology(string provider = "aws") => new()
    {
        Name = "Lifecycle Test",
        Provider = provider,
        ProviderConfig = provider == "aws"
            ? new() { ["aws_region"] = "us-east-1" }
            : new() { ["linode_region"] = "us-east" },
        Registry = "docker.xcord.net",
        Containers =
        [
            new Container
            {
                Name = "Caddy",
                Kind = ContainerKind.Caddy,
                Width = 600, Height = 400,
                Config = new() { ["domain"] = "test.xcord.net" },
                Images =
                [
                    new Image
                    {
                        Name = "PostgreSQL",
                        Kind = ImageKind.PostgreSQL,
                        Ports = [new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                        Config = new() { ["volumeSize"] = "20" },
                    },
                    new Image
                    {
                        Name = "Redis",
                        Kind = ImageKind.Redis,
                        Ports = [new Port { Name = "redis", Type = PortType.Database, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                        Config = new(),
                    }
                ]
            }
        ]
    };

    [Fact]
    public void Aws_AllInstances_HaveLifecycleIgnoreChanges()
    {
        var topology = CreateMinimalTopology("aws");
        var files = _awsProvider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        var instanceBlocks = instances.Split("resource \"aws_instance\"").Skip(1);
        Assert.NotEmpty(instanceBlocks);
        foreach (var block in instanceBlocks)
        {
            Assert.Contains("ignore_changes = all", block);
        }
    }

    [Fact]
    public void Linode_AllInstances_HaveLifecycleIgnoreChanges()
    {
        var topology = CreateMinimalTopology("linode");
        var files = _linodeProvider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        var instanceBlocks = instances.Split("resource \"linode_instance\"").Skip(1);
        Assert.NotEmpty(instanceBlocks);
        foreach (var block in instanceBlocks)
        {
            Assert.Contains("ignore_changes = all", block);
        }
    }
}
