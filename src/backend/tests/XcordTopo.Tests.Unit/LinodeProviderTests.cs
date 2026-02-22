using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class LinodeProviderTests
{
    private readonly LinodeProvider _provider = new();

    [Fact]
    public void GetInfo_ReturnsLinodeProvider()
    {
        var info = _provider.GetInfo();

        Assert.Equal("linode", info.Key);
        Assert.Equal("Linode (Akamai)", info.Name);
        Assert.NotEmpty(info.SupportedContainerKinds);
    }

    [Fact]
    public void GetRegions_ReturnsNonEmptyList()
    {
        var regions = _provider.GetRegions();

        Assert.NotEmpty(regions);
        Assert.All(regions, r =>
        {
            Assert.NotEmpty(r.Id);
            Assert.NotEmpty(r.Label);
        });
    }

    [Fact]
    public void GetPlans_ReturnsNonEmptyList()
    {
        var plans = _provider.GetPlans();

        Assert.NotEmpty(plans);
        Assert.All(plans, p =>
        {
            Assert.NotEmpty(p.Id);
            Assert.True(p.VCpus > 0);
            Assert.True(p.MemoryMb > 0);
        });
    }

    [Fact]
    public void GenerateHcl_WithSingleContainer_GeneratesExpectedFiles()
    {
        var topology = new Topology
        {
            Name = "test-topo",
            Provider = "linode"
        };
        topology.Containers.Add(new Container
        {
            Name = "web-server",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("main.tf", files.Keys);
        Assert.Contains("variables.tf", files.Keys);
        Assert.Contains("instances.tf", files.Keys);
        Assert.Contains("firewall.tf", files.Keys);
        Assert.Contains("outputs.tf", files.Keys);
        Assert.Contains("secrets.tf", files.Keys);

        // Verify main.tf has provider config
        Assert.Contains("linode/linode", files["main.tf"]);
        Assert.Contains("hashicorp/random", files["main.tf"]);

        // Verify instances.tf has the container
        Assert.Contains("web_server", files["instances.tf"]);
        Assert.Contains("linode_instance", files["instances.tf"]);

        // Verify outputs.tf has IP output
        Assert.Contains("web_server_ip", files["outputs.tf"]);
    }

    [Fact]
    public void GenerateHcl_WithContainerAndImages_GeneratesProvisioning()
    {
        var topology = CreateSingleHostTopology("app-server", new Image
        {
            Name = "redis",
            Kind = ImageKind.Redis,
            Width = 120,
            Height = 50
        });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("provisioning.tf", files.Keys);
        Assert.Contains("docker", files["provisioning.tf"]);
        Assert.Contains("redis", files["provisioning.tf"]);
        // Verify bridge network
        Assert.Contains("xcord-bridge", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_WithReplicas_GeneratesReplicaProvisioning()
    {
        var topology = CreateSingleHostTopology("app-host", new Image
        {
            Name = "Hub Server",
            Kind = ImageKind.HubServer,
            Width = 140,
            Height = 60,
            Config = new Dictionary<string, string> { ["replicas"] = "3" }
        });

        var files = _provider.GenerateHcl(topology);

        // nodebalancers.tf is no longer generated; verify volumes are still present
        Assert.DoesNotContain("nodebalancers.tf", files.Keys);
        Assert.Contains("instances.tf", files.Keys);
    }

    [Fact]
    public void GenerateHcl_WithFederationGroup_GeneratesCountedInstances()
    {
        var topology = new Topology { Name = "test-topo" };
        var network = new Container
        {
            Name = "xcord-net",
            Kind = ContainerKind.Network,
            Width = 1000,
            Height = 700
        };

        var fedGroup = new Container
        {
            Name = "federation",
            Kind = ContainerKind.FederationGroup,
            Width = 800,
            Height = 400,
            Config = new Dictionary<string, string> { ["instanceCount"] = "3" }
        };

        var fedHost = new Container
        {
            Name = "fed-host",
            Kind = ContainerKind.Host,
            Width = 700,
            Height = 300
        };
        fedHost.Images.Add(new Image
        {
            Name = "Fed Server",
            Kind = ImageKind.FederationServer,
            Width = 140,
            Height = 60,
            Config = new Dictionary<string, string> { ["replicas"] = "1" }
        });

        fedGroup.Children.Add(fedHost);
        network.Children.Add(fedGroup);
        topology.Containers.Add(network);

        var files = _provider.GenerateHcl(topology);

        // Instances should have count
        Assert.Contains("count", files["instances.tf"]);
        Assert.Contains("var.federation_instance_count", files["instances.tf"]);

        // Variables should have instance_count
        Assert.Contains("federation_instance_count", files["variables.tf"]);

        // Outputs should use splat for public IPs
        Assert.Contains("[*]", files["outputs.tf"]);

        // Federation host should have Docker install only, no docker run
        Assert.Contains("docker", files["provisioning.tf"]);
        Assert.DoesNotContain("docker run", files["provisioning.tf"]);

        // No secrets for federation hosts
        Assert.DoesNotContain("random_password", files["secrets.tf"]);
    }

    [Fact]
    public void GenerateHcl_SkipsCaddyContainersAsCompute()
    {
        var topology = new Topology { Name = "test-topo" };
        var host = new Container
        {
            Name = "proxy-host",
            Kind = ContainerKind.Host,
            Width = 400,
            Height = 300
        };
        var caddy = new Container
        {
            Name = "Caddy",
            Kind = ContainerKind.Caddy,
            Width = 350,
            Height = 200
        };
        host.Children.Add(caddy);
        topology.Containers.Add(host);

        var files = _provider.GenerateHcl(topology);

        // proxy-host should be an instance
        Assert.Contains("proxy_host", files["instances.tf"]);

        // Caddy should NOT be a separate instance
        var instanceCount = files["instances.tf"].Split("linode_instance").Length - 1;
        Assert.Equal(1, instanceCount);
    }

    [Fact]
    public void GenerateHcl_WithVolumeSize_GeneratesVolumeResources()
    {
        var topology = CreateSingleHostTopology("db-host", new Image
        {
            Name = "PostgreSQL",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string> { ["replicas"] = "1", ["volumeSize"] = "50" }
        });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("linode_volume", files["volumes.tf"]);
        Assert.Contains("50", files["volumes.tf"]);
        Assert.Contains("postgresql", files["volumes.tf"].ToLowerInvariant());
    }

    // --- New tests for wire-driven features ---

    [Fact]
    public void GenerateHcl_SecretGeneration_CreatesRandomPasswordsForStaticHosts()
    {
        var topology = CreateSingleHostTopology("db-host",
            new Image { Name = "PostgreSQL", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50 },
            new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50 },
            new Image { Name = "MinIO", Kind = ImageKind.MinIO, Width = 120, Height = 50 },
            new Image { Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50 });

        var files = _provider.GenerateHcl(topology);

        var secrets = files["secrets.tf"];
        // PG password
        Assert.Contains("db_host_postgresql_password", secrets);
        // Redis password
        Assert.Contains("db_host_redis_password", secrets);
        // MinIO access + secret
        Assert.Contains("db_host_minio_access_key", secrets);
        Assert.Contains("db_host_minio_secret_key", secrets);
        // LiveKit api key + secret
        Assert.Contains("db_host_livekit_api_key", secrets);
        Assert.Contains("db_host_livekit_api_secret", secrets);

        Assert.Contains("random_password", secrets);
    }

    [Fact]
    public void GenerateHcl_ComputePlanAutoSelection_SmallHost()
    {
        // Single Redis (256MB) should get Nanode 1GB
        var topology = CreateSingleHostTopology("small-host", new Image
        {
            Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("g6-nanode-1", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_ComputePlanAutoSelection_MediumHost()
    {
        // PG(512) + Redis(256) + HubServer(512) = 1280MB → needs 2GB
        var topology = CreateSingleHostTopology("med-host",
            new Image { Name = "PostgreSQL", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50 },
            new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50 },
            new Image { Name = "Hub Server", Kind = ImageKind.HubServer, Width = 140, Height = 60 });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("g6-standard-1", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_ComputePlanAutoSelection_LargeHost()
    {
        // PG(512) + Redis(256) + MinIO(512) + FedServer(512) + LiveKit(1024) = 2816MB → needs 4GB
        var topology = CreateSingleHostTopology("big-host",
            new Image { Name = "PostgreSQL", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50 },
            new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50 },
            new Image { Name = "MinIO", Kind = ImageKind.MinIO, Width = 120, Height = 50 },
            new Image { Name = "Fed Server", Kind = ImageKind.FederationServer, Width = 140, Height = 60 },
            new Image { Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50 });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("g6-standard-2", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_WireDrivenProvisioning_HasDockerNetwork()
    {
        var topology = CreateWiredHubTopology();
        var files = _provider.GenerateHcl(topology);

        Assert.Contains("docker network create xcord-bridge", files["provisioning.tf"]);
        Assert.Contains("--network xcord-bridge", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_WireDrivenProvisioning_HasEnvVars()
    {
        var topology = CreateWiredHubTopology();
        var files = _provider.GenerateHcl(topology);

        var provisioning = files["provisioning.tf"];
        // Hub Server should have connection strings
        Assert.Contains("ConnectionStrings__DefaultConnection", provisioning);
        Assert.Contains("ConnectionStrings__Redis", provisioning);
        // PG should have env vars
        Assert.Contains("POSTGRES_PASSWORD", provisioning);
        Assert.Contains("POSTGRES_DB", provisioning);
    }

    [Fact]
    public void GenerateHcl_WireDrivenProvisioning_HasVolumeMounts()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "PostgreSQL", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50,
            Config = new() { ["volumeSize"] = "50" }
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("-v postgresql_data:/var/lib/postgresql/data", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_WireDrivenProvisioning_LiveKitHasPortMapping()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("-p 7880:7880", files["provisioning.tf"]);
        Assert.Contains("-p 7881:7881", files["provisioning.tf"]);
        Assert.Contains("-p 7882:7882", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_CaddyfileGeneration_HasReverseProxy()
    {
        var hubImage = new Image
        {
            Id = Guid.NewGuid(), Name = "Hub Server", Kind = ImageKind.HubServer,
            Width = 140, Height = 60,
            Ports =
            [
                new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In }
            ],
            Config = new() { ["upstreamPath"] = "/hub/*" }
        };

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 400, Height = 300,
            Images = [hubImage],
            Config = new() { ["domain"] = "example.com" }
        };

        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "server", Kind = ContainerKind.Host,
            Children = [caddy], Width = 500, Height = 500
        };
        var topology = new Topology { Name = "test", Containers = [host] };

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("Caddyfile", provisioning);
        Assert.Contains("caddy:2-alpine", provisioning);
        Assert.Contains("-p 80:80", provisioning);
        Assert.Contains("-p 443:443", provisioning);
    }

    [Fact]
    public void GenerateHcl_FederationHost_OnlyDockerInstall()
    {
        var topology = new Topology { Name = "test-topo" };
        var fedGroup = new Container
        {
            Name = "federation",
            Kind = ContainerKind.FederationGroup,
            Width = 800, Height = 400,
            Config = new() { ["instanceCount"] = "3" }
        };
        var fedHost = new Container
        {
            Name = "fed-host",
            Kind = ContainerKind.Host,
            Width = 700, Height = 300
        };
        fedHost.Images.Add(new Image
        {
            Name = "Fed Server", Kind = ImageKind.FederationServer,
            Width = 140, Height = 60
        });
        fedHost.Images.Add(new Image
        {
            Name = "PostgreSQL", Kind = ImageKind.PostgreSQL,
            Width = 120, Height = 50
        });
        fedGroup.Children.Add(fedHost);
        topology.Containers.Add(fedGroup);

        var files = _provider.GenerateHcl(topology);

        // Should have Docker install
        Assert.Contains("get.docker.com", files["provisioning.tf"]);
        // Should NOT have docker run (hub provisions at runtime)
        Assert.DoesNotContain("docker run", files["provisioning.tf"]);
        // Should NOT have bridge network creation
        Assert.DoesNotContain("docker network create", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_Variables_HasFourCoreVariables()
    {
        var topology = new Topology { Name = "test-topo" };
        topology.Containers.Add(new Container
        {
            Name = "host", Kind = ContainerKind.Host, Width = 300, Height = 200
        });

        var files = _provider.GenerateHcl(topology);
        var vars = files["variables.tf"];

        Assert.Contains("variable \"linode_token\"", vars);
        Assert.Contains("variable \"region\"", vars);
        Assert.Contains("variable \"domain\"", vars);
        Assert.Contains("variable \"ssh_public_key\"", vars);
    }

    [Fact]
    public void GenerateHcl_Variables_HasFedGroupInstanceCounts()
    {
        var topology = new Topology { Name = "test-topo" };
        var fedGroup = new Container
        {
            Name = "federation",
            Kind = ContainerKind.FederationGroup,
            Width = 500, Height = 350,
            Config = new() { ["instanceCount"] = "5" }
        };
        fedGroup.Children.Add(new Container
        {
            Name = "fed-host", Kind = ContainerKind.Host, Width = 400, Height = 300
        });
        topology.Containers.Add(fedGroup);

        var files = _provider.GenerateHcl(topology);
        var vars = files["variables.tf"];

        Assert.Contains("federation_instance_count", vars);
        Assert.Contains("default = 5", vars);
    }

    [Fact]
    public void GenerateHcl_FederationOutputs_HasPublicAndPrivateIps()
    {
        var topology = new Topology { Name = "test-topo" };
        var fedGroup = new Container
        {
            Name = "federation", Kind = ContainerKind.FederationGroup,
            Width = 500, Height = 350,
            Config = new() { ["instanceCount"] = "2" }
        };
        fedGroup.Children.Add(new Container
        {
            Name = "fed-host", Kind = ContainerKind.Host,
            Width = 400, Height = 300
        });
        topology.Containers.Add(fedGroup);

        var files = _provider.GenerateHcl(topology);
        var outputs = files["outputs.tf"];

        Assert.Contains("fed_host_ips", outputs);
        Assert.Contains("fed_host_private_ips", outputs);
        Assert.Contains("private_ip_address", outputs);
    }

    [Fact]
    public void GenerateHcl_FirewallWithLiveKit_IncludesLiveKitPorts()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        var firewall = files["firewall.tf"];

        Assert.Contains("allow-livekit-tcp", firewall);
        Assert.Contains("allow-livekit-udp", firewall);
        Assert.Contains("7880-7882", firewall);
    }

    [Fact]
    public void GenerateHcl_FirewallWithoutLiveKit_NoLiveKitPorts()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        var firewall = files["firewall.tf"];

        Assert.DoesNotContain("livekit", firewall);
        Assert.DoesNotContain("7880", firewall);
    }

    [Fact]
    public void GenerateHcl_MainTf_HasRandomProvider()
    {
        var topology = new Topology { Name = "test" };
        topology.Containers.Add(new Container
        {
            Name = "host", Kind = ContainerKind.Host, Width = 300, Height = 200
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("hashicorp/random", files["main.tf"]);
    }

    [Fact]
    public void GenerateHcl_RedisCommandOverride()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "Redis", Kind = ImageKind.Redis, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("redis-server --requirepass", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_MinIOCommandOverride()
    {
        var topology = CreateSingleHostTopology("host", new Image
        {
            Name = "MinIO", Kind = ImageKind.MinIO, Width = 120, Height = 50
        });

        var files = _provider.GenerateHcl(topology);
        Assert.Contains("server /data --console-address :9001", files["provisioning.tf"]);
    }

    [Fact]
    public void DeriveDbName_HubServerConsumer_ReturnsXcordHub()
    {
        var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var hubPgPort = new Port { Id = Guid.NewGuid(), Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };

        var pg = new Image { Id = Guid.NewGuid(), Name = "PG", Kind = ImageKind.PostgreSQL, Ports = [pgPort], Width = 100, Height = 50 };
        var hub = new Image { Id = Guid.NewGuid(), Name = "Hub", Kind = ImageKind.HubServer, Ports = [hubPgPort], Width = 100, Height = 50 };

        var host = new Container { Id = Guid.NewGuid(), Name = "host", Kind = ContainerKind.Host, Images = [hub, pg], Width = 300, Height = 200 };
        var topology = new Topology
        {
            Containers = [host],
            Wires = [new Wire { FromNodeId = hub.Id, FromPortId = hubPgPort.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id }]
        };
        var resolver = new WireResolver(topology);

        Assert.Equal("xcord_hub", LinodeProvider.DeriveDbName(pg, resolver));
    }

    [Fact]
    public void DeriveDbName_FedServerConsumer_ReturnsXcord()
    {
        var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var fedPgPort = new Port { Id = Guid.NewGuid(), Name = "pg_connection", Type = PortType.Database, Direction = PortDirection.Out };

        var pg = new Image { Id = Guid.NewGuid(), Name = "PG", Kind = ImageKind.PostgreSQL, Ports = [pgPort], Width = 100, Height = 50 };
        var fed = new Image { Id = Guid.NewGuid(), Name = "Fed", Kind = ImageKind.FederationServer, Ports = [fedPgPort], Width = 100, Height = 50 };

        var host = new Container { Id = Guid.NewGuid(), Name = "host", Kind = ContainerKind.Host, Images = [fed, pg], Width = 300, Height = 200 };
        var topology = new Topology
        {
            Containers = [host],
            Wires = [new Wire { FromNodeId = fed.Id, FromPortId = fedPgPort.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id }]
        };
        var resolver = new WireResolver(topology);

        Assert.Equal("xcord", LinodeProvider.DeriveDbName(pg, resolver));
    }

    [Fact]
    public void DeriveDbName_NoConsumer_ReturnsApp()
    {
        var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pg = new Image { Id = Guid.NewGuid(), Name = "PG", Kind = ImageKind.PostgreSQL, Ports = [pgPort], Width = 100, Height = 50 };
        var host = new Container { Id = Guid.NewGuid(), Name = "host", Kind = ContainerKind.Host, Images = [pg], Width = 300, Height = 200 };
        var topology = new Topology { Containers = [host] };
        var resolver = new WireResolver(topology);

        Assert.Equal("app", LinodeProvider.DeriveDbName(pg, resolver));
    }

    [Fact]
    public void CalculateHostRam_SumsImageMinRam()
    {
        var host = new Container
        {
            Name = "test", Kind = ContainerKind.Host, Width = 300, Height = 200,
            Images =
            [
                new Image { Name = "PG", Kind = ImageKind.PostgreSQL, Width = 100, Height = 50 },     // 512
                new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 100, Height = 50 },       // 256
            ]
        };

        Assert.Equal(768, LinodeProvider.CalculateHostRam(host));
    }

    [Fact]
    public void CalculateHostRam_IncludesCaddyOverhead()
    {
        var host = new Container
        {
            Name = "test", Kind = ContainerKind.Host, Width = 300, Height = 200,
            Images =
            [
                new Image { Name = "Redis", Kind = ImageKind.Redis, Width = 100, Height = 50 }, // 256
            ],
            Children =
            [
                new Container { Name = "Caddy", Kind = ContainerKind.Caddy, Width = 350, Height = 200 } // +128
            ]
        };

        Assert.Equal(384, LinodeProvider.CalculateHostRam(host));
    }

    // --- Host replica tests ---

    [Fact]
    public void GenerateHcl_HostWithReplicas_GeneratesCountedInstances()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2");

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("count = 2", files["instances.tf"]);
        Assert.Contains("${count.index}", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithReplicas_ProvisioningHasCount()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2",
            images: new Image { Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50 });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("count = 2", files["provisioning.tf"]);
        Assert.Contains("[count.index].ip_address", files["provisioning.tf"]);
        // Still gets full provisioning (not federation docker-install-only)
        Assert.Contains("docker run", files["provisioning.tf"]);
        Assert.Contains("docker network create xcord-bridge", files["provisioning.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithReplicas_VolumesHaveCount()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2",
            images: new Image
            {
                Name = "PostgreSQL", Kind = ImageKind.PostgreSQL, Width = 120, Height = 50,
                Config = new() { ["volumeSize"] = "50" }
            });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("count = 2", files["volumes.tf"]);
        Assert.Contains("[count.index].id", files["volumes.tf"]);
        Assert.Contains("${count.index}", files["volumes.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithReplicas_OutputsUseSplat()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2");

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("media_host_ips", files["outputs.tf"]);
        Assert.Contains("[*].ip_address", files["outputs.tf"]);
        Assert.Contains("media_host_private_ips", files["outputs.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithReplicas_FirewallUsesSplat()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2",
            images: new Image { Name = "LiveKit", Kind = ImageKind.LiveKit, Width = 120, Height = 50 });

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("media_host[*].id", files["firewall.tf"]);
        Assert.Contains("concat(", files["firewall.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithVariableReplicas_GeneratesVariable()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "$MEDIA_HOSTS");

        var files = _provider.GenerateHcl(topology);

        Assert.Contains("variable \"media_hosts\"", files["variables.tf"]);
        Assert.Contains("type = \"number\"", files["variables.tf"]);
        Assert.Contains("var.media_hosts", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithVariableReplicas_MinMaxValidation()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "$MEDIA_HOSTS",
            minReplicas: "1", maxReplicas: "5");

        var files = _provider.GenerateHcl(topology);

        var vars = files["variables.tf"];
        Assert.Contains("validation", vars);
        Assert.Contains("var.media_hosts >= 1 && var.media_hosts <= 5", vars);
        Assert.Contains("between 1 and 5", vars);
    }

    [Fact]
    public void GenerateHcl_HostWithMinReplicasOnly_MinValidation()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "$MEDIA_HOSTS",
            minReplicas: "2");

        var files = _provider.GenerateHcl(topology);

        var vars = files["variables.tf"];
        Assert.Contains("validation", vars);
        Assert.Contains("var.media_hosts >= 2", vars);
        Assert.Contains("at least 2", vars);
    }

    [Fact]
    public void GenerateHcl_HostWithMaxReplicasOnly_MaxValidation()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "$MEDIA_HOSTS",
            maxReplicas: "10");

        var files = _provider.GenerateHcl(topology);

        var vars = files["variables.tf"];
        Assert.Contains("validation", vars);
        Assert.Contains("var.media_hosts <= 10", vars);
        Assert.Contains("at most 10", vars);
    }

    [Fact]
    public void IsReplicatedHost_FedGroup_ReturnsTrue()
    {
        var host = new Container { Name = "fed-host", Kind = ContainerKind.Host, Width = 300, Height = 200 };
        var fedGroup = new Container { Name = "federation", Kind = ContainerKind.FederationGroup, Width = 500, Height = 350 };
        var entry = new LinodeProvider.HostEntry(host, fedGroup);

        Assert.True(LinodeProvider.IsReplicatedHost(entry));
    }

    [Fact]
    public void IsReplicatedHost_HostReplicas_ReturnsTrue()
    {
        var host = new Container
        {
            Name = "media-host", Kind = ContainerKind.Host, Width = 300, Height = 200,
            Config = new() { ["replicas"] = "3" }
        };
        var entry = new LinodeProvider.HostEntry(host, null);

        Assert.True(LinodeProvider.IsReplicatedHost(entry));
    }

    [Fact]
    public void IsReplicatedHost_HostVariableReplicas_ReturnsTrue()
    {
        var host = new Container
        {
            Name = "media-host", Kind = ContainerKind.Host, Width = 300, Height = 200,
            Config = new() { ["replicas"] = "$MEDIA_HOSTS" }
        };
        var entry = new LinodeProvider.HostEntry(host, null);

        Assert.True(LinodeProvider.IsReplicatedHost(entry));
    }

    [Fact]
    public void IsReplicatedHost_SingleHost_ReturnsFalse()
    {
        var host = new Container
        {
            Name = "web-host", Kind = ContainerKind.Host, Width = 300, Height = 200,
            Config = new() { ["replicas"] = "1" }
        };
        var entry = new LinodeProvider.HostEntry(host, null);

        Assert.False(LinodeProvider.IsReplicatedHost(entry));
    }

    [Fact]
    public void IsReplicatedHost_NoReplicasConfig_ReturnsFalse()
    {
        var host = new Container { Name = "web-host", Kind = ContainerKind.Host, Width = 300, Height = 200 };
        var entry = new LinodeProvider.HostEntry(host, null);

        Assert.False(LinodeProvider.IsReplicatedHost(entry));
    }

    [Fact]
    public void GenerateHcl_HostWithLiteralReplicas_NoVariable()
    {
        // Literal replicas (e.g. "2") should NOT generate a variable — just `count = 2`
        var topology = CreateReplicatedHostTopology("media-host", replicas: "2");

        var files = _provider.GenerateHcl(topology);

        Assert.DoesNotContain("media_host_replicas", files["variables.tf"]);
        Assert.Contains("count = 2", files["instances.tf"]);
    }

    [Fact]
    public void GenerateHcl_HostWithLiteralReplicasAndMinMax_GeneratesVariable()
    {
        var topology = CreateReplicatedHostTopology("media-host", replicas: "3",
            minReplicas: "1", maxReplicas: "10");

        var files = _provider.GenerateHcl(topology);

        var vars = files["variables.tf"];
        Assert.Contains("variable \"media_host_replicas\"", vars);
        Assert.Contains("default = 3", vars);
        Assert.Contains("validation", vars);
        Assert.Contains("var.media_host_replicas", files["instances.tf"]);
    }

    // --- Backup tests ---

    [Fact]
    public void GenerateHcl_PostgreSQLWithBackup_GeneratesCronJobAndRetention()
    {
        var topology = CreateSingleHostTopology("db-host", new Image
        {
            Name = "PostgreSQL",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string>
            {
                ["volumeSize"] = "50",
                ["backupFrequency"] = "daily",
                ["backupRetention"] = "14"
            }
        });

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Cron schedule for daily
        Assert.Contains("0 2 * * *", provisioning);
        // Backup script path
        Assert.Contains("/opt/backups/postgresql/backup.sh", provisioning);
        // pg_dumpall command
        Assert.Contains("pg_dumpall", provisioning);
        // Retention cleanup with 14 days
        Assert.Contains("-mtime +14", provisioning);
        // Empty dir cleanup
        Assert.Contains("-type d -empty -delete", provisioning);
    }

    [Fact]
    public void GenerateHcl_RedisWithBackup_GeneratesBgsaveBackup()
    {
        var topology = CreateSingleHostTopology("cache-host", new Image
        {
            Name = "Redis",
            Kind = ImageKind.Redis,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string>
            {
                ["volumeSize"] = "10",
                ["backupFrequency"] = "hourly"
            }
        });

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("0 * * * *", provisioning);
        Assert.Contains("redis-cli BGSAVE", provisioning);
        Assert.Contains("docker cp", provisioning);
        // Default retention of 7
        Assert.Contains("-mtime +7", provisioning);
    }

    [Fact]
    public void GenerateHcl_MinIOWithBackup_GeneratesMirrorBackup()
    {
        var topology = CreateSingleHostTopology("storage-host", new Image
        {
            Name = "MinIO",
            Kind = ImageKind.MinIO,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string>
            {
                ["volumeSize"] = "100",
                ["backupFrequency"] = "weekly",
                ["backupRetention"] = "30"
            }
        });

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("0 2 * * 0", provisioning);
        Assert.Contains("minio/mc mirror", provisioning);
        Assert.Contains("-mtime +30", provisioning);
    }

    [Fact]
    public void GenerateHcl_ImageWithoutBackupFrequency_NoBackupCommands()
    {
        var topology = CreateSingleHostTopology("db-host", new Image
        {
            Name = "PostgreSQL",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string> { ["volumeSize"] = "50" }
        });

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.DoesNotContain("/opt/backups", provisioning);
        Assert.DoesNotContain("crontab", provisioning);
    }

    [Fact]
    public void GenerateHcl_HostBackupFrequencyFallback_UsedWhenImageHasNone()
    {
        var topology = new Topology { Name = "test-topo" };
        var host = new Container
        {
            Name = "db-host",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Config = new Dictionary<string, string>
            {
                ["backupFrequency"] = "daily",
                ["backupRetention"] = "21"
            }
        };
        host.Images.Add(new Image
        {
            Name = "PostgreSQL",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string> { ["volumeSize"] = "50" }
        });
        topology.Containers.Add(host);

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        Assert.Contains("0 2 * * *", provisioning);
        Assert.Contains("-mtime +21", provisioning);
        Assert.Contains("pg_dumpall", provisioning);
    }

    [Fact]
    public void GenerateHcl_ImageBackupOverridesHostBackup()
    {
        var topology = new Topology { Name = "test-topo" };
        var host = new Container
        {
            Name = "db-host",
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Config = new Dictionary<string, string>
            {
                ["backupFrequency"] = "daily",
                ["backupRetention"] = "7"
            }
        };
        host.Images.Add(new Image
        {
            Name = "PostgreSQL",
            Kind = ImageKind.PostgreSQL,
            Width = 120,
            Height = 50,
            Config = new Dictionary<string, string>
            {
                ["volumeSize"] = "50",
                ["backupFrequency"] = "weekly",
                ["backupRetention"] = "30"
            }
        });
        topology.Containers.Add(host);

        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Image-level weekly overrides host-level daily
        Assert.Contains("0 2 * * 0", provisioning);
        Assert.DoesNotContain("0 2 * * *", provisioning);
        Assert.Contains("-mtime +30", provisioning);
    }

    // --- Helper methods ---

    private static Topology CreateReplicatedHostTopology(
        string hostName, string replicas,
        string? minReplicas = null, string? maxReplicas = null,
        params Image[] images)
    {
        var topology = new Topology { Name = "test-topo" };
        var config = new Dictionary<string, string> { ["replicas"] = replicas };
        if (minReplicas != null) config["minReplicas"] = minReplicas;
        if (maxReplicas != null) config["maxReplicas"] = maxReplicas;

        var container = new Container
        {
            Name = hostName,
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200,
            Config = config
        };
        foreach (var image in images)
            container.Images.Add(image);
        topology.Containers.Add(container);
        return topology;
    }

    private static Topology CreateSingleHostTopology(string hostName, params Image[] images)
    {
        var topology = new Topology { Name = "test-topo" };
        var container = new Container
        {
            Name = hostName,
            Kind = ContainerKind.Host,
            Width = 300,
            Height = 200
        };
        foreach (var image in images)
            container.Images.Add(image);
        topology.Containers.Add(container);
        return topology;
    }

    private static Topology CreateWiredHubTopology()
    {
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

        return new Topology
        {
            Name = "test-topo",
            Containers = [host],
            Wires =
            [
                new Wire { FromNodeId = hub.Id, FromPortId = hubPgPort.Id, ToNodeId = pg.Id, ToPortId = pgPort.Id },
                new Wire { FromNodeId = hub.Id, FromPortId = hubRedisPort.Id, ToNodeId = redis.Id, ToPortId = redisPort.Id }
            ]
        };
    }
}
