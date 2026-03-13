using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class AwsProviderTests
{
    private readonly AwsProvider _provider = new();

    // --- Bug 1: Elastic images should get their own instances ---

    [Fact]
    public void GenerateHcl_ElasticImages_GetOwnInstanceResources()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // hub_server has replicas "1-3" → elastic → needs own aws_instance
        Assert.Contains("aws_instance\" \"hub_server\"", instances);
        // live_kit has replicas "1-100" → elastic → needs own aws_instance
        Assert.Contains("aws_instance\" \"live_kit\"", instances);
    }

    // --- Bug 2: Elastic image replica variables ---

    [Fact]
    public void GenerateHcl_ElasticImages_GenerateReplicaVariables()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var vars = files["variables.tf"];

        Assert.Contains("variable \"hub_server_replicas\"", vars);
        Assert.Contains("var.hub_server_replicas >= 1 && var.hub_server_replicas <= 3", vars);

        Assert.Contains("variable \"live_kit_replicas\"", vars);
        Assert.Contains("var.live_kit_replicas >= 1 && var.live_kit_replicas <= 100", vars);
    }

    // --- Bug 3: Caddy instance sized for co-located services ---

    [Fact]
    public void GenerateHcl_CaddyWithColocatedServices_SizedCorrectly()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // After elastic images (hub_server, live_kit) break out, Caddy host holds:
        // Caddy(128) + redis_livekit(256) + redis_hub(256) + pg_hub(512) = 1152MB
        // t3.micro (1024MB) is too small → must be t3.small (2048MB)
        Assert.Contains("aws_instance\" \"caddy\"", instances);

        // Extract the Caddy resource block and check its instance type
        var caddyBlockStart = instances.IndexOf("aws_instance\" \"caddy\"");
        var caddyBlockLen = Math.Min(500, instances.Length - caddyBlockStart);
        var caddyBlock = instances.Substring(caddyBlockStart, caddyBlockLen);
        Assert.Contains("t3.small", caddyBlock);
        Assert.DoesNotContain("t3.micro", caddyBlock);
    }

    // --- Bug 4: Pool provisioner gated on count > 0 ---

    [Fact]
    public void GenerateHcl_PoolProvisioner_GatedOnHostCount()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // The enterprise tier manager provisioner should have a count guard
        // so it doesn't crash when host_count = 0.
        // Resource names are tier-qualified: <pool_name>_<tier_id>_manager
        var managerBlock = ExtractResourceBlock(provisioning, "provision_enterprise_tier_enterprise_manager");
        Assert.NotNull(managerBlock);

        // Must have count conditional to prevent crash on host_count=0
        Assert.Contains("count", managerBlock);
        Assert.Contains("enterprise_tier_enterprise_host_count", managerBlock);
    }

    // --- Bug 5: Pool host_count defaults (no targetTenants leakage) ---

    [Fact]
    public void GenerateHcl_PoolHostCountDefaults_NoTargetTenantsLeakage()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var vars = files["variables.tf"];

        // targetTenants is UI-only — host_count defaults should be simple values (1),
        // not computed from targetTenants.
        // Resource names are now tier-qualified: <pool_name>_<tier_id>
        Assert.Contains("variable \"free_tier_free_host_count\"", vars);
        Assert.Contains("variable \"pro_tier_pro_host_count\"", vars);
        Assert.Contains("variable \"basic_tier_basic_host_count\"", vars);
        Assert.Contains("variable \"enterprise_tier_enterprise_host_count\"", vars);

        // Pool host counts default to 0 — pool infrastructure is deferred.
        // Hub sets these > 0 via terraform apply when it needs to provision tenants.
        foreach (var poolName in new[] { "free_tier_free", "basic_tier_basic", "pro_tier_pro", "enterprise_tier_enterprise" })
        {
            var varBlock = ExtractVariableBlock(vars, $"{poolName}_host_count");
            Assert.NotNull(varBlock);
            Assert.Contains("default = 0", varBlock);
        }
    }

    // --- Bug 6: All expected instance resources present ---

    [Fact]
    public void GenerateHcl_ProductionRobust_AllInstanceResourcesPresent()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // Instance resource types — pool instances are now tier-qualified: <pool_name>_<tier_id>
        Assert.Contains("aws_instance\" \"hub_server\"", instances);
        Assert.Contains("aws_instance\" \"live_kit\"", instances);
        Assert.Contains("aws_instance\" \"caddy\"", instances);
        // Each pool container generates one instance per tier profile
        Assert.Contains("aws_instance\" \"free_tier_free\"", instances);
        Assert.Contains("aws_instance\" \"basic_tier_basic\"", instances);
        Assert.Contains("aws_instance\" \"pro_tier_pro\"", instances);
        Assert.Contains("aws_instance\" \"enterprise_tier_enterprise\"", instances);
    }

    // --- DataPool generates infrastructure (host VM + data services) ---

    [Fact]
    public void GenerateHcl_DataPool_GeneratesInstance()
    {
        // DataPool hosts need to exist so hub-provisioned FederationServers can connect to them.
        // The HOST is Terraform-managed; only FederationServer containers are hub-provisioned.
        var topology = CreateTopologyWithDataPool();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        Assert.Contains("aws_instance\" \"data_pool\"", instances);
        Assert.Contains("aws_instance\" \"web_host\"", instances);
    }

    [Fact]
    public void GenerateHcl_DataPool_HasProvisioning()
    {
        var topology = CreateTopologyWithDataPool();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // DataPool host should be provisioned with its data services (PG, etc.)
        Assert.Contains("provision_data_pool", provisioning);
        Assert.Contains("postgres", provisioning);
    }

    [Fact]
    public void GenerateHcl_DataPool_HasSecrets()
    {
        var topology = CreateTopologyWithDataPool();
        var files = _provider.GenerateHcl(topology);
        var secrets = files["secrets.tf"];

        // DataPool PG image needs a password secret
        Assert.Contains("data_pool_pg_data_password", secrets);
    }

    // --- FederationServer excluded from pool provisioning ---

    [Fact]
    public void GenerateHcl_PoolProvisioning_ExcludesFederationServer()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Pool provisioning should deploy shared services (PG, Redis, MinIO)
        // but NOT FederationServer images (hub provisions those at runtime).
        // Resource names are tier-qualified: <pool_name>_<tier_id>_manager
        var proManagerBlock = ExtractResourceBlock(provisioning, "provision_pro_tier_pro_manager");
        Assert.NotNull(proManagerBlock);

        Assert.DoesNotContain("ghcr.io/xcord/fed", proManagerBlock);
        Assert.Contains("postgres", proManagerBlock);
        Assert.Contains("redis", proManagerBlock);
        Assert.Contains("minio", proManagerBlock);
    }

    // --- Pool secrets only for shared services ---

    [Fact]
    public void GenerateHcl_PoolSecrets_OnlySharedServices()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var secrets = files["secrets.tf"];

        // Should have secrets for PG, Redis, MinIO per pool
        Assert.Contains("pro_tier_pg_pro_password", secrets);
        Assert.Contains("pro_tier_redis_pro_password", secrets);
        Assert.Contains("pro_tier_mio_pro_access_key", secrets);
        Assert.Contains("pro_tier_mio_pro_secret_key", secrets);

        // Should NOT have secrets for FederationServer
        Assert.DoesNotContain("fed_pro", secrets);
        Assert.DoesNotContain("fed_free", secrets);
    }

    // --- Standalone Caddy deploys co-located images ---

    [Fact]
    public void GenerateHcl_StandaloneCaddy_DeploysColocatedImages()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Co-located non-elastic images should be deployed on the Caddy host
        Assert.Contains("redis_hub", caddyBlock);
        Assert.Contains("redis_livekit", caddyBlock);
        Assert.Contains("pg_hub", caddyBlock);

        // Caddyfile should be deployed
        Assert.Contains("Caddyfile", caddyBlock);
    }

    // --- Standalone Caddy excludes elastic images ---

    [Fact]
    public void GenerateHcl_StandaloneCaddy_ExcludesElasticImages()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Elastic images (hub_server replicas "1-3", live_kit replicas "1-100")
        // get their own instances — should NOT be deployed as docker containers on Caddy host.
        // Note: hub_server/live_kit may appear in the Caddyfile as reverse proxy upstreams — that's fine.
        // What should NOT happen is docker run/service create for these images' Docker images on the Caddy host.
        Assert.DoesNotContain("ghcr.io/xcord/hub", caddyBlock);
        Assert.DoesNotContain("livekit/livekit-server", caddyBlock);
    }

    // --- Elastic images get own provisioning ---

    [Fact]
    public void GenerateHcl_ElasticImages_GetOwnProvisioningBlocks()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Elastic images with private-registry images get deploy_* blocks (gated by deploy_apps)
        var hubBlock = ExtractResourceBlock(provisioning, "deploy_hub_server");
        Assert.NotNull(hubBlock);
        Assert.Contains("hub_server", hubBlock);

        var liveKitBlock = ExtractResourceBlock(provisioning, "provision_live_kit");
        Assert.NotNull(liveKitBlock);
        Assert.Contains("live_kit", liveKitBlock);
    }

    // --- DNS records for wired hosts ---

    [Fact]
    public void GenerateHcl_DnsContainer_GeneratesARecordsForWiredHosts()
    {
        // DNS is on Linode, hosts on AWS — need multi-provider generator
        var linode = new LinodeProvider();
        var aws = new AwsProvider();
        var registry = new ProviderRegistry([linode, aws]);
        var generator = new MultiProviderHclGenerator(registry);

        var topology = CreateProductionRobustTopology();

        // Wire caddy to DNS so A records are generated
        var dnsContainer = topology.Containers[0]; // XCord Net DNS
        var caddyContainer = dnsContainer.Children[0]; // Caddy
        topology.Wires.Add(new Wire
        {
            FromNodeId = caddyContainer.Id,
            FromPortId = caddyContainer.Ports[0].Id, // http_in
            ToNodeId = dnsContainer.Id,
            ToPortId = dnsContainer.Ports[0].Id // records
        });

        var files = generator.Generate(topology);

        // Should have a DNS file with Linode domain records
        Assert.Contains("dns_linode.tf", files.Keys);
        var dns = files["dns_linode.tf"];
        Assert.Contains("linode_domain_record", dns);
        Assert.Contains("linode_domain", dns);
    }

    // --- SSH private key variable defined and used ---

    [Fact]
    public void GenerateHcl_TlsPrivateKey_GeneratedAutomatically()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var main = files["main.tf"];

        // tls_private_key resource must be generated for SSH access
        Assert.Contains("resource \"tls_private_key\" \"deploy\"", main);
        Assert.Contains("ED25519", main);
    }

    [Fact]
    public void GenerateHcl_TlsPrivateKey_UsedInAllProvisioners()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Every provisioner connection block must use tls_private_key
        var connectionCount = CountOccurrences(provisioning, "connection {");
        var privateKeyCount = CountOccurrences(provisioning, "tls_private_key.deploy.private_key_pem");

        Assert.True(connectionCount > 0, "Should have at least one connection block");
        Assert.Equal(connectionCount, privateKeyCount);
    }

    // --- Pool shared services use correct Docker Swarm deployment mode ---

    [Fact]
    public void GenerateHcl_PoolSharedServices_HaveCorrectNames()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Resource names are tier-qualified: <pool_name>_<tier_id>_manager
        var proManagerBlock = ExtractResourceBlock(provisioning, "provision_pro_tier_pro_manager");
        Assert.NotNull(proManagerBlock);

        // Shared services should be prefixed with "shared-" to avoid name collision with tenant containers
        Assert.Contains("shared-pg_pro", proManagerBlock);
        Assert.Contains("shared-redis_pro", proManagerBlock);
        Assert.Contains("shared-mio_pro", proManagerBlock);
    }

    // --- Elastic images reference correct Docker images ---

    [Fact]
    public void GenerateHcl_ElasticImages_UseCorrectDockerImages()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var hubBlock = ExtractResourceBlock(provisioning, "deploy_hub_server");
        Assert.NotNull(hubBlock);
        Assert.Contains("${var.registry_url}/hub:${var.hub_version}", hubBlock);

        var liveKitBlock = ExtractResourceBlock(provisioning, "provision_live_kit");
        Assert.NotNull(liveKitBlock);
        Assert.Contains("livekit/livekit-server:v1.8.3", liveKitBlock);
    }

    // --- Caddy host deploys images with correct Docker images ---

    [Fact]
    public void GenerateHcl_StandaloneCaddy_ColocatedImagesUseCorrectDockerImages()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Co-located data services should use correct public Docker images
        Assert.Contains("postgres:17-alpine", caddyBlock);
        Assert.Contains("redis:7-alpine", caddyBlock);
        Assert.Contains("caddy:", caddyBlock); // Caddy reverse proxy image
    }

    // --- Pool instance count uses variable, not hardcoded ---

    [Fact]
    public void GenerateHcl_PoolInstances_CountFromVariable()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // Each pool instance should use var.<pool_name>_host_count for count.
        // Resource names are tier-qualified: <pool_name>_<tier_id>
        var proBlock = ExtractResourceBlock(instances, "pro_tier_pro");
        Assert.NotNull(proBlock);
        Assert.Contains("var.pro_tier_pro_host_count", proBlock);

        var freeBlock = ExtractResourceBlock(instances, "free_tier_free");
        Assert.NotNull(freeBlock);
        Assert.Contains("var.free_tier_free_host_count", freeBlock);
    }

    // --- VPC and networking resources present ---

    [Fact]
    public void GenerateHcl_Network_VpcAndSubnetPresent()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var network = files["network.tf"];

        Assert.Contains("aws_vpc", network);
        Assert.Contains("aws_subnet", network);
        Assert.Contains("aws_internet_gateway", network);
        Assert.Contains("aws_route_table", network);
    }

    // --- Security group allows required ports ---

    [Fact]
    public void GenerateHcl_SecurityGroup_AllowsRequiredPorts()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var sg = files["security_groups.tf"];

        Assert.Contains("aws_security_group", sg);
        // Must allow SSH, HTTP, HTTPS
        Assert.Contains("22", sg);
        Assert.Contains("80", sg);
        Assert.Contains("443", sg);
        // Must allow internal VPC traffic
        Assert.Contains("self = true", sg);
    }

    // --- Outputs present for all instance types ---

    [Fact]
    public void GenerateHcl_Outputs_PresentForAllResources()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var outputs = files["outputs.tf"];

        // Pool outputs — resource names are tier-qualified: <pool_name>_<tier_id>
        Assert.Contains("output \"pro_tier_pro_ips\"", outputs);
        Assert.Contains("output \"free_tier_free_ips\"", outputs);
        Assert.Contains("output \"basic_tier_basic_ips\"", outputs);
        Assert.Contains("output \"enterprise_tier_enterprise_ips\"", outputs);

        // Standalone Caddy output
        Assert.Contains("output \"caddy_ip\"", outputs);

        // Elastic image outputs
        Assert.Contains("output \"hub_server_ips\"", outputs);
        Assert.Contains("output \"live_kit_ips\"", outputs);
    }

    // --- Helper ---

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    // --- Helper: Build topology with DataPool ---

    private static Topology CreateTopologyWithDataPool()
    {
        var dataPool = new Container
        {
            Id = Guid.NewGuid(), Name = "Data Pool", Kind = ContainerKind.DataPool,
            Width = 400, Height = 300,
            Images =
            [
                new Image
                {
                    Id = Guid.NewGuid(), Name = "pg_data", Kind = ImageKind.PostgreSQL,
                    Width = 120, Height = 50,
                    Ports = [new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In }],
                    Scaling = ImageScaling.Shared
                }
            ],
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "ssh", Type = PortType.Control, Direction = PortDirection.In },
                new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut }
            ]
        };

        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "Web Host", Kind = ContainerKind.Host,
            Width = 300, Height = 200,
            Ports = [new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut }]
        };

        return new Topology
        {
            Name = "DataPool Test",
            Provider = "aws",
            Containers = [dataPool, host]
        };
    }

    // --- Helper: Build the Production — Robust topology ---

    private static Topology CreateProductionRobustTopology()
    {
        // Hub-level images (inside Caddy, outside pools)
        var hubServerPgPort = new Port { Id = Guid.NewGuid(), Name = "pg", Type = PortType.Database, Direction = PortDirection.Out, Offset = 0.33 };
        var hubServerRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.Out, Offset = 0.67 };
        var hubServerHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };

        var hubServer = new Image
        {
            Id = Guid.NewGuid(), Name = "hub_server", Kind = ImageKind.HubServer,
            Width = 140, Height = 60,
            Ports = [hubServerHttpPort, hubServerPgPort, hubServerRedisPort],
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
            Config = new() { ["replicas"] = "1-100" },
            Scaling = ImageScaling.Shared
        };

        var redisLiveKitPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redisLiveKit = new Image
        {
            Id = Guid.NewGuid(), Name = "redis_livekit", Kind = ImageKind.Redis,
            Width = 120, Height = 50,
            Ports = [redisLiveKitPort],
            Scaling = ImageScaling.Shared
        };

        var redisHubPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redisHub = new Image
        {
            Id = Guid.NewGuid(), Name = "redis_hub", Kind = ImageKind.Redis,
            Width = 120, Height = 50,
            Ports = [redisHubPort],
            Scaling = ImageScaling.Shared
        };

        var pgHubPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pgHub = new Image
        {
            Id = Guid.NewGuid(), Name = "pg_hub", Kind = ImageKind.PostgreSQL,
            Width = 120, Height = 50,
            Ports = [pgHubPort],
            Scaling = ImageScaling.Shared
        };

        // ComputePool helper
        static Container CreatePool(string name, string tierProfile)
        {
            var fedPgPort = new Port { Id = Guid.NewGuid(), Name = "pg", Type = PortType.Database, Direction = PortDirection.Out };
            var fedRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.Out };
            var fedMinioPort = new Port { Id = Guid.NewGuid(), Name = "minio", Type = PortType.Storage, Direction = PortDirection.Out };
            var fedHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };

            var fed = new Image
            {
                Id = Guid.NewGuid(), Name = $"fed_{tierProfile}", Kind = ImageKind.FederationServer,
                Width = 140, Height = 60,
                Ports = [fedHttpPort, fedPgPort, fedRedisPort, fedMinioPort],
                Scaling = ImageScaling.PerTenant
            };

            var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
            var pg = new Image
            {
                Id = Guid.NewGuid(), Name = $"pg_{tierProfile}", Kind = ImageKind.PostgreSQL,
                Width = 120, Height = 50, Ports = [pgPort], Scaling = ImageScaling.Shared
            };

            var redisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
            var redis = new Image
            {
                Id = Guid.NewGuid(), Name = $"redis_{tierProfile}", Kind = ImageKind.Redis,
                Width = 120, Height = 50, Ports = [redisPort], Scaling = ImageScaling.Shared
            };

            var minioPort = new Port { Id = Guid.NewGuid(), Name = "s3", Type = PortType.Storage, Direction = PortDirection.In };
            var minio = new Image
            {
                Id = Guid.NewGuid(), Name = $"mio_{tierProfile}", Kind = ImageKind.MinIO,
                Width = 120, Height = 50, Ports = [minioPort], Scaling = ImageScaling.Shared
            };

            return new Container
            {
                Id = Guid.NewGuid(), Name = name, Kind = ContainerKind.ComputePool,
                Width = 427, Height = 229,
                Images = [fed, pg, redis, minio],
                Ports =
                [
                    new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut },
                    new() { Id = Guid.NewGuid(), Name = "control", Type = PortType.Control, Direction = PortDirection.In }
                ],
                Config = new() { ["tierProfile"] = tierProfile, ["targetTenants"] = "50" }
            };
        }

        var freeTier = CreatePool("Free Tier", "free");
        var basicTier = CreatePool("Basic Tier", "basic");
        var proTier = CreatePool("Pro Tier", "pro");
        var enterpriseTier = CreatePool("Enterprise Tier", "enterprise");

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 925, Height = 792,
            Images = [hubServer, liveKit, redisLiveKit, redisHub, pgHub],
            Children = [freeTier, basicTier, proTier, enterpriseTier],
            Config = new() { ["domain"] = "xcord.net" },
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "http_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "https_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "upstream", Type = PortType.Network, Direction = PortDirection.Out, Side = PortSide.Bottom }
            ]
        };

        var dnsContainer = new Container
        {
            Id = Guid.NewGuid(), Name = "XCord Net", Kind = ContainerKind.Dns,
            Width = 971, Height = 859,
            Children = [caddy],
            Config = new() { ["domain"] = "xcord.net", ["provider"] = "linode" },
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "records", Type = PortType.Network, Direction = PortDirection.In }
            ]
        };

        // Wires: hub_server → pg_hub, hub_server → redis_hub, live_kit → redis_livekit
        var wires = new List<Wire>
        {
            new() { FromNodeId = hubServer.Id, FromPortId = hubServerPgPort.Id, ToNodeId = pgHub.Id, ToPortId = pgHubPort.Id },
            new() { FromNodeId = hubServer.Id, FromPortId = hubServerRedisPort.Id, ToNodeId = redisHub.Id, ToPortId = redisHubPort.Id },
            new() { FromNodeId = liveKit.Id, FromPortId = liveKitRedisPort.Id, ToNodeId = redisLiveKit.Id, ToPortId = redisLiveKitPort.Id },
        };

        return new Topology
        {
            Name = "Production — Robust",
            Provider = "aws",
            Containers = [dnsContainer],
            Wires = wires,
            Registry = "docker.xcord.net",
            ServiceKeys = new()
            {
                ["registry_url"] = "docker.xcord.net",
                ["registry_username"] = "admin",
            },
            TierProfiles =
            [
                new() { Id = "free", Name = "Free Tier", ImageSpecs = new() { ["FederationServer"] = new() { MemoryMb = 256, CpuMillicores = 250, DiskMb = 512 } } },
                new() { Id = "basic", Name = "Basic Tier", ImageSpecs = new() { ["FederationServer"] = new() { MemoryMb = 512, CpuMillicores = 500, DiskMb = 2048 } } },
                new() { Id = "pro", Name = "Pro Tier", ImageSpecs = new() { ["FederationServer"] = new() { MemoryMb = 1024, CpuMillicores = 1000, DiskMb = 5120 } } },
                new() { Id = "enterprise", Name = "Enterprise Tier", ImageSpecs = new() { ["FederationServer"] = new() { MemoryMb = 2048, CpuMillicores = 2000, DiskMb = 25600 } } },
            ]
        };
    }

    // --- Issue 2: Docker login called in all provisioning blocks ---

    [Fact]
    public void GenerateHcl_Provisioning_CallsDockerLoginOnHostsWithPrivateImages()
    {
        var topology = CreateProductionRobustTopology();
        topology.Registry = "docker.xcord.net";
        topology.ServiceKeys = new()
        {
            ["registry_url"] = "docker.xcord.net",
            ["registry_username"] = "admin",
        };
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Caddy host has hub_server (HubServer) → needs docker login
        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);
        Assert.Contains("docker login", caddyBlock);

        // Elastic hub_server instance needs docker login to pull hub image (in deploy phase)
        var hubBlock = ExtractResourceBlock(provisioning, "deploy_hub_server");
        Assert.NotNull(hubBlock);
        Assert.Contains("docker login", hubBlock);
    }

    [Fact]
    public void GenerateHcl_PoolProvisioning_CallsDockerLoginForFedImages()
    {
        var topology = CreateProductionRobustTopology();
        topology.Registry = "docker.xcord.net";
        topology.ServiceKeys = new()
        {
            ["registry_url"] = "docker.xcord.net",
            ["registry_username"] = "admin",
        };
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Pool manager needs docker login because FederationServer images come from private registry.
        // Resource names are tier-qualified: <pool_name>_<tier_id>_manager
        var proManagerBlock = ExtractResourceBlock(provisioning, "provision_pro_tier_pro_manager");
        Assert.NotNull(proManagerBlock);
        Assert.Contains("docker login", proManagerBlock);
    }

    // --- Issue 4: Registry on Caddy host merges route into main Caddyfile ---

    [Fact]
    public void GenerateHcl_RegistryColocatedWithCaddy_NoSeparateCaddySidecar()
    {
        // When registry image is inside a Caddy container, the registry domain
        // should be a route in the main Caddyfile, NOT a separate caddy_registry container
        var topology = CreateTopologyWithRegistryOnCaddy();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Should NOT deploy a separate caddy_registry container (port conflict)
        Assert.DoesNotContain("caddy_registry", caddyBlock);

        // The registry subdomain route should be in the main Caddyfile instead
        Assert.Contains("registry.${var.domain}", caddyBlock);
    }

    // --- Issue 5: Registry container has data volume mount ---

    [Fact]
    public void GenerateHcl_RegistryContainer_HasVolumeMount()
    {
        var topology = CreateTopologyWithRegistryOnCaddy();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Registry container must have a data volume to persist images across restarts
        Assert.Contains("/var/lib/registry", caddyBlock);
    }

    // --- Issue 6: Bare domain gets a Caddyfile route ---

    [Fact]
    public void GenerateHcl_CaddyDomain_HasBareDomainRoute()
    {
        // When Caddy has domain "xcord.net", the Caddyfile must handle
        // the bare domain (xcord.net) — wildcards don't match apex
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // The Caddyfile should have a standalone block for the bare domain (not *.domain or hub.domain)
        // In the HCL output, Caddyfile lines are joined with literal \n, so we use regex
        // to find "${var.domain} {" preceded by a newline (not by "*." or "hub." etc.)
        var pattern = @"(?<!\*\.)(?<!hub\.)(?<!livekit\.)\$\{var\.domain\} \{";
        Assert.Matches(pattern, caddyBlock);
    }

    // --- Issue 7: LiveKit provisioning includes Redis connection ---

    [Fact]
    public void GenerateHcl_LiveKitWithRedisWire_PassesRedisUrl()
    {
        // When LiveKit is wired to a Redis, it needs REDIS_URL env var
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // LiveKit is elastic → gets its own provisioning block
        var liveKitBlock = ExtractResourceBlock(provisioning, "provision_live_kit");
        Assert.NotNull(liveKitBlock);

        // Must pass Redis connection URL to LiveKit with authentication
        Assert.Contains("REDIS_URL", liveKitBlock);
        Assert.Contains("random_password.", liveKitBlock);
        Assert.Contains("_password.result", liveKitBlock);
    }

    // --- Helper: Topology with registry co-located on Caddy host ---

    private static Topology CreateTopologyWithRegistryOnCaddy()
    {
        var registry = new Image
        {
            Id = Guid.NewGuid(), Name = "registry", Kind = ImageKind.Registry,
            Width = 140, Height = 60, Ports = [],
            Scaling = ImageScaling.Shared
        };

        var pgHubPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pgHub = new Image
        {
            Id = Guid.NewGuid(), Name = "pg_hub", Kind = ImageKind.PostgreSQL,
            Width = 120, Height = 50, Ports = [pgHubPort], Scaling = ImageScaling.Shared
        };

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 400, Height = 300,
            Images = [pgHub, registry],
            Config = new() { ["domain"] = "xcord.net" },
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "http_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "https_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
            ]
        };

        return new Topology
        {
            Name = "Registry on Caddy",
            Provider = "aws",
            Containers = [caddy],
            Registry = "docker.xcord.net",
            ServiceKeys = new()
            {
                ["registry_url"] = "docker.xcord.net",
                ["registry_username"] = "admin",
            },
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Phase 1 TDD: Critical HCL issues (all must FAIL before code fix)
    // ══════════════════════════════════════════════════════════════════

    // --- Issue 1-2: :latest tags passed through for third-party images ---

    [Fact]
    public void GenerateHcl_ThirdPartyLatestTag_ReplacedWithPinnedVersion()
    {
        // If a topology image has :latest, HCL should use the pinned default instead
        var topology = CreateTopologyWithLatestTag();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];
        Assert.DoesNotContain(":latest", provisioning);
        Assert.Contains("minio/minio:RELEASE", provisioning);
    }

    // --- Issue 3: Registry container has no -p flag ---

    [Fact]
    public void GenerateHcl_RegistryContainer_PublishesPort5000()
    {
        // Registry must publish port 5000 for hosts to push/pull images
        var topology = CreateTopologyWithRegistryOnCaddy();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];
        // Registry container must publish port 5000 for image push/pull
        var registryIdx = provisioning.IndexOf("--name registry");
        Assert.True(registryIdx >= 0, "Expected registry container");
        // Find the docker run line containing registry
        var lineStart = provisioning.LastIndexOf('\n', registryIdx) + 1;
        var lineEnd = provisioning.IndexOf('\n', registryIdx);
        var registryLine = provisioning[lineStart..lineEnd];
        Assert.Contains("-p 5000:5000", registryLine);
    }

    // --- Issue 5: Caddy upstreams use public_ip instead of private_ip ---

    [Fact]
    public void GenerateHcl_CaddyfileUpstreams_UsePrivateIpForSameVpc()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Find the Caddyfile content within the caddy provisioning block
        var caddyfileIdx = caddyBlock.IndexOf("Caddyfile");
        Assert.True(caddyfileIdx >= 0, "Expected Caddyfile in caddy provisioning");
        var caddyfileSection = caddyBlock[caddyfileIdx..];

        // Upstreams to hub_server and live_kit should use private_ip, not public_ip
        // since they're all in the same VPC
        if (caddyfileSection.Contains("aws_instance.hub_server"))
            Assert.Contains("private_ip", caddyfileSection);
        if (caddyfileSection.Contains("aws_instance.live_kit"))
            Assert.DoesNotContain("public_ip", caddyfileSection.Substring(caddyfileSection.IndexOf("reverse_proxy")));
    }

    // --- Issue 6: Caddy has wildcard route for pool traffic ---

    [Fact]
    public void GenerateHcl_Caddyfile_NoStaticWildcardRouteForPools()
    {
        // Pool infrastructure is deferred (count=0 on initial deploy).
        // Caddy must NOT have a static wildcard route to pool instances — when count=0,
        // the compute_pool[0].private_ip reference would be invalid in Terraform.
        // Hub configures wildcard tenant routing via Caddy admin API at runtime.
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Must NOT have a wildcard route — pool IPs don't exist at initial deploy
        Assert.DoesNotContain("*.${var.domain}", caddyBlock);
    }

    // --- Issue 8: LiveKit UDP 50000-60000 missing from security group ---

    [Fact]
    public void GenerateHcl_SecurityGroup_LiveKitHasUdpMediaRange()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var sg = files["security_groups.tf"];

        // LiveKit WebRTC needs UDP 50000-60000 for media relay
        Assert.Contains("50000", sg);
        Assert.Contains("60000", sg);
    }

    // --- Issue 9: version variables don't default to "latest" ---

    [Fact]
    public void GenerateHcl_VersionVariables_NoLatestDefault()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var vars = files["variables.tf"];

        var hubVersionBlock = ExtractVariableBlock(vars, "hub_version");
        Assert.NotNull(hubVersionBlock);
        Assert.DoesNotContain("\"latest\"", hubVersionBlock);

        var fedVersionBlock = ExtractVariableBlock(vars, "fed_version");
        Assert.NotNull(fedVersionBlock);
        Assert.DoesNotContain("\"latest\"", fedVersionBlock);
    }

    // --- Issue 10: X-Frame-Options DENY blocks iframes ---

    [Fact]
    public void GenerateHcl_Caddyfile_XFrameOptionsNotDeny()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Hub uses iframes for instance embedding — DENY blocks all iframes
        Assert.DoesNotContain("DENY", caddyBlock);
    }

    // --- Issue 11: camera=() blocks video calling ---

    [Fact]
    public void GenerateHcl_Caddyfile_CameraNotBlocked()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Video calling needs camera access — camera=() blocks it entirely
        Assert.DoesNotContain("camera=()", caddyBlock);
    }

    // --- Issue 13: No Caddy route for docker.${var.domain} ---

    [Fact]
    public void GenerateHcl_RegistryColocatedWithCaddy_HasCaddyRoute()
    {
        var topology = CreateTopologyWithRegistryOnCaddy();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var caddyBlock = ExtractResourceBlock(provisioning, "provision_caddy");
        Assert.NotNull(caddyBlock);

        // Caddyfile should have a route for the registry subdomain
        Assert.Contains("registry.${var.domain}", caddyBlock);
        Assert.Contains("reverse_proxy", caddyBlock);
    }

    // --- SSH uses ssh_cidr_blocks with fallback to 0.0.0.0/0 for provisioners ---

    [Fact]
    public void GenerateHcl_SecurityGroup_SshUsesCidrBlocksWithFallback()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var sg = files["security_groups.tf"];

        // SSH ingress must exist (provisioners require it)
        var sshIdx = sg.IndexOf("\"SSH\"");
        Assert.True(sshIdx >= 0, "Expected SSH ingress rule");
        var sshBlockEnd = sg.IndexOf('}', sshIdx);
        var sshBlock = sg[sshIdx..sshBlockEnd];

        // Should use ssh_cidr_blocks when set, fall back to 0.0.0.0/0 for provisioners
        Assert.Contains("var.ssh_cidr_blocks", sshBlock);
        Assert.Contains("0.0.0.0/0", sshBlock);
    }

    // --- Issue 19: python3 -m http.server in /var ---

    [Fact]
    public void GenerateHcl_SwarmTokenNotServedFromVar()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        // Token server should not run in /var (exposes logs, caches, tokens)
        Assert.DoesNotContain("cd /var", provisioning);
    }

    // --- Issue 20: Unencrypted EBS volumes ---

    [Fact]
    public void GenerateHcl_AllInstances_HaveEncryptedVolumes()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // Every root_block_device should have encrypted = true
        var blockDeviceCount = CountOccurrences(instances, "root_block_device");
        var encryptedCount = CountOccurrences(instances, "encrypted");
        Assert.True(blockDeviceCount > 0, "Expected at least one root_block_device");
        Assert.Equal(blockDeviceCount, encryptedCount);
    }

    // --- Issue 21: No IMDSv2 enforcement ---

    [Fact]
    public void GenerateHcl_AllInstances_EnforceIMDSv2()
    {
        var topology = CreateProductionRobustTopology();
        var files = _provider.GenerateHcl(topology);
        var instances = files["instances.tf"];

        // Every aws_instance should have metadata_options with http_tokens = "required"
        var instanceCount = CountOccurrences(instances, "resource \"aws_instance\"");
        var metadataCount = CountOccurrences(instances, "http_tokens");
        Assert.True(instanceCount > 0, "Expected at least one aws_instance");
        Assert.Equal(instanceCount, metadataCount);
    }

    // --- Helper: Topology with :latest tag on a third-party image ---

    private static Topology CreateTopologyWithLatestTag()
    {
        var minioPort = new Port { Id = Guid.NewGuid(), Name = "s3", Type = PortType.Storage, Direction = PortDirection.In };
        var minio = new Image
        {
            Id = Guid.NewGuid(), Name = "minio", Kind = ImageKind.MinIO,
            Width = 120, Height = 50,
            Ports = [minioPort],
            DockerImage = "minio/minio:latest",  // Explicitly set :latest
            Scaling = ImageScaling.Shared
        };

        var host = new Container
        {
            Id = Guid.NewGuid(), Name = "Storage Host", Kind = ContainerKind.Host,
            Width = 300, Height = 200,
            Images = [minio],
            Ports = [new() { Id = Guid.NewGuid(), Name = "public", Type = PortType.Network, Direction = PortDirection.InOut }]
        };

        return new Topology
        {
            Name = "Latest Tag Test",
            Provider = "aws",
            Containers = [host]
        };
    }

    // --- HubServer deploy resource has complete env vars ---

    [Fact]
    public void GenerateHcl_DeployHubServer_HasCompleteEnvVars()
    {
        var topology = CreateHubServerTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var deployBlock = ExtractResourceBlock(provisioning, "deploy_hub_server");
        Assert.NotNull(deployBlock);

        // Database + Redis
        Assert.Contains("Database__ConnectionString", deployBlock);
        Assert.Contains("Redis__ConnectionString", deployBlock);

        // JWT (auto-generated secrets)
        Assert.Contains("Jwt__SecretKey", deployBlock);
        Assert.Contains("Jwt__Issuer", deployBlock);
        Assert.Contains("Jwt__Audience", deployBlock);

        // Encryption (auto-generated secret)
        Assert.Contains("Encryption__Key", deployBlock);

        // Storage/MinIO (wired)
        Assert.Contains("Storage__Endpoint", deployBlock);
        Assert.Contains("Storage__AccessKey", deployBlock);
        Assert.Contains("Storage__SecretKey", deployBlock);
        Assert.Contains("Storage__BucketName", deployBlock);
        Assert.Contains("Storage__UseSsl", deployBlock);

        // Admin (service key + auto-generated password)
        Assert.Contains("Admin__Username", deployBlock);
        Assert.Contains("Admin__Email", deployBlock);
        Assert.Contains("Admin__Password", deployBlock);

        // CORS (derived from hub_base_domain)
        Assert.Contains("Cors__AllowedOrigins", deployBlock);

        // Email extras
        Assert.Contains("Email__UseSsl", deployBlock);
        Assert.Contains("Email__DevMode", deployBlock);
        Assert.Contains("Email__HubBaseUrl", deployBlock);

        // Captcha
        Assert.Contains("Captcha__Enabled", deployBlock);
    }

    [Fact]
    public void GenerateHcl_Secrets_HasHubSpecificSecrets()
    {
        var topology = CreateHubServerTopology();
        var files = _provider.GenerateHcl(topology);
        var secrets = files["secrets.tf"];

        // Hub needs auto-generated secrets for JWT, encryption, admin password
        Assert.Contains("hub_jwt_secret", secrets);
        Assert.Contains("hub_encryption_key", secrets);
        Assert.Contains("hub_admin_password", secrets);
    }

    [Fact]
    public void GenerateHcl_DeployHubServer_PullBeforeRemove()
    {
        // Deploy should pull new image before removing old container to minimize downtime
        var topology = CreateHubServerTopology();
        var files = _provider.GenerateHcl(topology);
        var provisioning = files["provisioning.tf"];

        var deployBlock = ExtractResourceBlock(provisioning, "deploy_hub_server");
        Assert.NotNull(deployBlock);

        var pullIdx = deployBlock.IndexOf("docker pull");
        var rmIdx = deployBlock.IndexOf("docker rm -f hub_server");
        Assert.True(pullIdx >= 0, "Expected docker pull command");
        Assert.True(rmIdx >= 0, "Expected docker rm command");
        Assert.True(pullIdx < rmIdx, "docker pull must come before docker rm for zero-downtime deploy");
    }

    private static Topology CreateHubServerTopology()
    {
        // Hub server with pg, redis, and minio wires
        var hubPgPort = new Port { Id = Guid.NewGuid(), Name = "pg", Type = PortType.Database, Direction = PortDirection.Out };
        var hubRedisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.Out };
        var hubMinioPort = new Port { Id = Guid.NewGuid(), Name = "minio", Type = PortType.Storage, Direction = PortDirection.Out };
        var hubHttpPort = new Port { Id = Guid.NewGuid(), Name = "http", Type = PortType.Network, Direction = PortDirection.In };

        var hubServer = new Image
        {
            Id = Guid.NewGuid(), Name = "hub_server", Kind = ImageKind.HubServer,
            Width = 140, Height = 60,
            Ports = [hubHttpPort, hubPgPort, hubRedisPort, hubMinioPort],
            Config = new() { ["replicas"] = "1-3" },
            Scaling = ImageScaling.Shared
        };

        var pgPort = new Port { Id = Guid.NewGuid(), Name = "postgres", Type = PortType.Database, Direction = PortDirection.In };
        var pgHub = new Image
        {
            Id = Guid.NewGuid(), Name = "pg_hub", Kind = ImageKind.PostgreSQL,
            Width = 120, Height = 50, Ports = [pgPort], Scaling = ImageScaling.Shared
        };

        var redisPort = new Port { Id = Guid.NewGuid(), Name = "redis", Type = PortType.Database, Direction = PortDirection.In };
        var redisHub = new Image
        {
            Id = Guid.NewGuid(), Name = "redis_hub", Kind = ImageKind.Redis,
            Width = 120, Height = 50, Ports = [redisPort], Scaling = ImageScaling.Shared
        };

        var minioPort = new Port { Id = Guid.NewGuid(), Name = "s3", Type = PortType.Storage, Direction = PortDirection.In };
        var minioHub = new Image
        {
            Id = Guid.NewGuid(), Name = "mio_hub", Kind = ImageKind.MinIO,
            Width = 120, Height = 50, Ports = [minioPort], Scaling = ImageScaling.Shared
        };

        var caddy = new Container
        {
            Id = Guid.NewGuid(), Name = "Caddy", Kind = ContainerKind.Caddy,
            Width = 600, Height = 400,
            Images = [hubServer, pgHub, redisHub, minioHub],
            Config = new() { ["domain"] = "xcord.net" },
            Ports =
            [
                new() { Id = Guid.NewGuid(), Name = "http_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
                new() { Id = Guid.NewGuid(), Name = "https_in", Type = PortType.Network, Direction = PortDirection.In, Side = PortSide.Top },
            ]
        };

        var dns = new Container
        {
            Id = Guid.NewGuid(), Name = "XCord Net", Kind = ContainerKind.Dns,
            Width = 700, Height = 500,
            Children = [caddy],
            Config = new() { ["domain"] = "xcord.net", ["provider"] = "linode" },
            Ports = [new() { Id = Guid.NewGuid(), Name = "records", Type = PortType.Network, Direction = PortDirection.In }]
        };

        var wires = new List<Wire>
        {
            new() { FromNodeId = hubServer.Id, FromPortId = hubPgPort.Id, ToNodeId = pgHub.Id, ToPortId = pgPort.Id },
            new() { FromNodeId = hubServer.Id, FromPortId = hubRedisPort.Id, ToNodeId = redisHub.Id, ToPortId = redisPort.Id },
            new() { FromNodeId = hubServer.Id, FromPortId = hubMinioPort.Id, ToNodeId = minioHub.Id, ToPortId = minioPort.Id },
        };

        return new Topology
        {
            Name = "Hub Server Test",
            Provider = "aws",
            Containers = [dns],
            Wires = wires,
            Registry = "docker.xcord.net",
            ServiceKeys = new()
            {
                ["registry_url"] = "docker.xcord.net",
                ["registry_username"] = "admin",
                ["smtp_host"] = "smtp.sendgrid.net",
                ["smtp_username"] = "apikey",
                ["smtp_from_address"] = "noreply@xcord.net",
                ["hub_admin_username"] = "admin",
                ["hub_admin_email"] = "admin@xcord.net",
                ["hub_base_domain"] = "xcord.net",
            }
        };
    }

    // --- String extraction helpers ---

    private static string? ExtractResourceBlock(string hcl, string resourceName)
    {
        var marker = $"\"{resourceName}\"";
        var idx = hcl.IndexOf(marker);
        if (idx < 0) return null;

        // Find the opening brace
        var braceStart = hcl.IndexOf('{', idx);
        if (braceStart < 0) return null;

        // Count braces to find matching close
        var depth = 1;
        var pos = braceStart + 1;
        while (pos < hcl.Length && depth > 0)
        {
            if (hcl[pos] == '{') depth++;
            else if (hcl[pos] == '}') depth--;
            pos++;
        }

        return hcl.Substring(idx, pos - idx);
    }

    private static string? ExtractVariableBlock(string hcl, string varName)
    {
        return ExtractResourceBlock(hcl, varName);
    }
}
