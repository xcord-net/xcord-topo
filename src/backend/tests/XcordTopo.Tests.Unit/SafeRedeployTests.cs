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

    /// Topology where a host has ONLY private-registry images (Hub Server).
    private static Topology CreateTopologyWithPrivateRegistryOnlyHost()
    {
        var hubId = Guid.NewGuid();
        var pgId = Guid.NewGuid();

        return new Topology
        {
            Name = "Provision Test",
            Provider = "aws",
            ProviderConfig = new() { ["aws_region"] = "us-east-1" },
            Registry = "docker.xcord.net",
            Containers =
            [
                new Container
                {
                    Name = "Hub Server",
                    Kind = ContainerKind.Host,
                    Width = 600, Height = 400,
                    Images =
                    [
                        new Image
                        {
                            Id = hubId,
                            Name = "Hub Server",
                            Kind = ImageKind.HubServer,
                            Ports = [new Port { Name = "http", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                            Config = new(),
                        }
                    ]
                },
                new Container
                {
                    Name = "Data",
                    Kind = ContainerKind.Host,
                    Width = 600, Height = 400,
                    Images =
                    [
                        new Image
                        {
                            Id = pgId,
                            Name = "PostgreSQL",
                            Kind = ImageKind.PostgreSQL,
                            Ports = [new Port { Name = "pg", Type = PortType.Database, Direction = PortDirection.In, Side = PortSide.Left, Offset = 0.5 }],
                            Config = new() { ["volumeSize"] = "20" },
                        }
                    ]
                }
            ],
            Wires = []
        };
    }

    [Fact]
    public void Aws_PrivateRegistryOnlyHost_GetsProvisionWithDockerInstall()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        var files = _awsProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // hub_server MUST have a provision_* resource in phase 1
        Assert.Contains("null_resource\" \"provision_hub_server\"", provisioning);

        // Extract the provision_hub_server block specifically
        var provisionStart = provisioning.IndexOf("provision_hub_server");
        Assert.True(provisionStart >= 0, "provision_hub_server block must exist");
        var provisionBlock = provisioning.Substring(provisionStart,
            Math.Min(1500, provisioning.Length - provisionStart));

        // It must include Docker install
        Assert.Contains("get.docker.com", provisionBlock);
    }

    [Fact]
    public void Aws_DeployResource_DoesNotInstallDocker()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        var files = _awsProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Find the deploy_hub_server block
        var deployStart = provisioning.IndexOf("deploy_hub_server");
        Assert.True(deployStart >= 0, "deploy_hub_server block must exist");
        var deployBlock = provisioning.Substring(deployStart);

        // The deploy block must NOT contain Docker install
        Assert.DoesNotContain("get.docker.com", deployBlock);
    }

    [Fact]
    public void Aws_DeployResource_DependsOnProvisionResource()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        var files = _awsProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Find the deploy_hub_server block
        var deployStart = provisioning.IndexOf("deploy_hub_server");
        Assert.True(deployStart >= 0, "deploy_hub_server block must exist");
        var deployBlock = provisioning.Substring(deployStart);

        // Deploy must depend on the provision resource, not the instance directly
        Assert.Contains("null_resource.provision_hub_server", deployBlock);
    }

    [Fact]
    public void Linode_PrivateRegistryOnlyHost_GetsProvisionWithDockerInstall()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        topology.Provider = "linode";
        topology.ProviderConfig = new() { ["linode_region"] = "us-east" };
        var files = _linodeProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // hub_server MUST have a provision_* resource in phase 1
        Assert.Contains("null_resource\" \"provision_hub_server\"", provisioning);

        var provisionStart = provisioning.IndexOf("provision_hub_server");
        Assert.True(provisionStart >= 0, "provision_hub_server block must exist");
        var provisionBlock = provisioning.Substring(provisionStart,
            Math.Min(1500, provisioning.Length - provisionStart));

        Assert.Contains("get.docker.com", provisionBlock);
    }

    [Fact]
    public void Linode_DeployResource_DoesNotInstallDocker()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        topology.Provider = "linode";
        topology.ProviderConfig = new() { ["linode_region"] = "us-east" };
        var files = _linodeProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var deployStart = provisioning.IndexOf("deploy_hub_server");
        Assert.True(deployStart >= 0, "deploy_hub_server block must exist");
        var deployBlock = provisioning.Substring(deployStart);

        Assert.DoesNotContain("get.docker.com", deployBlock);
    }

    [Fact]
    public void Linode_DeployResource_DependsOnProvisionResource()
    {
        var topology = CreateTopologyWithPrivateRegistryOnlyHost();
        topology.Provider = "linode";
        topology.ProviderConfig = new() { ["linode_region"] = "us-east" };
        var files = _linodeProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var deployStart = provisioning.IndexOf("deploy_hub_server");
        Assert.True(deployStart >= 0, "deploy_hub_server block must exist");
        var deployBlock = provisioning.Substring(deployStart);

        Assert.Contains("null_resource.provision_hub_server", deployBlock);
    }

    [Fact]
    public void GenerateContainerHealthCheck_ReturnsInspectCommand()
    {
        var check = TopologyHelpers.GenerateContainerHealthCheck("my_container");
        Assert.Contains("docker inspect my_container", check);
        Assert.Contains(".State.Running", check);
        Assert.Contains("RestartCount", check);
    }

    [Fact]
    public void GenerateRegistryRetryBlock_ContainsRetryLoopAndFinalCheck()
    {
        var block = TopologyHelpers.GenerateRegistryRetryBlock("registry", "registry:2.8", "-p 5000:5000");
        Assert.Contains("for i in 1 2 3", block);
        Assert.Contains("docker rm -f registry", block);
        Assert.Contains("docker run -d --name registry", block);
        // Must have final verification AFTER the loop
        var afterDone = block.Substring(block.LastIndexOf("done"));
        Assert.Contains("docker inspect registry", afterDone);
    }

    [Fact]
    public void Aws_Provisioning_ContainsHealthChecksAfterDockerRun()
    {
        var topology = CreateMinimalTopology("aws");
        var files = _awsProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("docker inspect", provisioning);
        Assert.Contains("State.Running", provisioning);
    }

    [Fact]
    public void Linode_Provisioning_ContainsHealthChecksAfterDockerRun()
    {
        var topology = CreateMinimalTopology("linode");
        var files = _linodeProvider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("docker inspect", provisioning);
        Assert.Contains("State.Running", provisioning);
    }
}
