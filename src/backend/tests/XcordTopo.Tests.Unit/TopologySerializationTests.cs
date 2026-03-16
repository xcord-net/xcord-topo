using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XcordTopo.Features.Topologies;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class TopologySerializationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"xcord-topo-test-{Guid.NewGuid()}");

    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private UpdateTopologyHandler CreateHandler(FileTopologyStore store)
    {
        var registry = new ProviderRegistry([new AwsProvider(), new LinodeProvider()]);
        var generator = new MultiProviderHclGenerator(registry, DefaultPlugins.CreateRegistry());
        var hclFileManager = new HclFileManager(
            Options.Create(new DataOptions { BasePath = _tempDir }),
            NullLogger<HclFileManager>.Instance);
        return new UpdateTopologyHandler(
            store, generator, hclFileManager,
            NullLogger<UpdateTopologyHandler>.Instance);
    }

    private static string LoadFixture(string name)
    {
        var assembly = typeof(TopologySerializationTests).Assembly;
        var resourceName = $"XcordTopo.Tests.Unit.Fixtures.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Fixture not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Topology DeserializeFixture(string name)
    {
        var json = LoadFixture(name);
        return JsonSerializer.Deserialize<Topology>(json, JsonOptions)
            ?? throw new InvalidOperationException("Deserialized topology was null");
    }

    [Fact]
    public void ProductionRobust_DeserializesWithoutError()
    {
        var topology = DeserializeFixture("production-robust.json");

        Assert.Equal("Production - Robust", topology.Name);
        Assert.Equal("aws", topology.Provider);
        Assert.Single(topology.Containers); // DNS wrapper
        Assert.Equal(15, topology.Wires.Count);
        Assert.Equal(4, topology.TierProfiles.Count);
    }

    [Fact]
    public void ProductionRobust_RoundTripsCorrectly()
    {
        var topology = DeserializeFixture("production-robust.json");

        // Serialize back and deserialize again - should not lose data
        var json = JsonSerializer.Serialize(topology, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<Topology>(json, JsonOptions)!;

        Assert.Equal(topology.Name, roundTripped.Name);
        Assert.Equal(topology.Containers.Count, roundTripped.Containers.Count);
        Assert.Equal(topology.Wires.Count, roundTripped.Wires.Count);
        Assert.Equal(topology.TierProfiles.Count, roundTripped.TierProfiles.Count);
        Assert.Equal(topology.ServiceKeys.Count, roundTripped.ServiceKeys.Count);
    }

    [Fact]
    public async Task ProductionRobust_SaveViaHandler_PersistsToDisk()
    {
        // This is the exact code path: PUT /api/v1/topologies/{id} → UpdateTopologyHandler → FileTopologyStore
        var topology = DeserializeFixture("production-robust.json");
        var store = new FileTopologyStore(
            Options.Create(new DataOptions { BasePath = _tempDir }),
            NullLogger<FileTopologyStore>.Instance);
        var handler = CreateHandler(store);

        var result = await handler.Handle(new UpdateTopologyRequest(topology), CancellationToken.None);

        var saved = result.Match(t => t, _ => null);
        Assert.NotNull(saved);

        // Verify it persisted and can be read back
        var loaded = await store.GetAsync(topology.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Production - Robust", loaded.Name);
        Assert.Equal(15, loaded.Wires.Count);
        Assert.Equal(4, loaded.TierProfiles.Count);
    }

    [Fact]
    public async Task ProductionRobust_SaveUpdatesFileOnDisk()
    {
        // The actual bug: clicking Save in the UI must update the JSON file on the host filesystem.
        // This failed in Docker because the container user couldn't write to the bind-mounted volume.
        var topology = DeserializeFixture("production-robust.json");
        var store = new FileTopologyStore(
            Options.Create(new DataOptions { BasePath = _tempDir }),
            NullLogger<FileTopologyStore>.Instance);

        // Seed the file (simulates the pre-existing topology on disk)
        await store.SaveAsync(topology);
        var originalTimestamp = topology.UpdatedAt;

        // Modify something and save again (simulates clicking Save in the UI)
        topology.Name = "Modified Name";
        await store.SaveAsync(topology);

        // Read the raw file from disk - not through the store, to prove the file actually changed
        var filePath = Path.Combine(_tempDir, "topologies", $"{topology.Id}.json");
        var rawJson = await File.ReadAllTextAsync(filePath);

        Assert.Contains("Modified Name", rawJson);
        Assert.True(topology.UpdatedAt > originalTimestamp);
    }

    [Fact]
    public async Task ProductionRobust_SaveAlsoWritesHclFiles()
    {
        var topology = DeserializeFixture("production-robust.json");
        var store = new FileTopologyStore(
            Options.Create(new DataOptions { BasePath = _tempDir }),
            NullLogger<FileTopologyStore>.Instance);
        var handler = CreateHandler(store);

        await handler.Handle(new UpdateTopologyRequest(topology), CancellationToken.None);

        // HCL files should have been written alongside the topology JSON
        var terraformDir = Path.Combine(_tempDir, "deployments", topology.Id.ToString(), "terraform");
        Assert.True(Directory.Exists(terraformDir), $"Expected terraform dir at {terraformDir}");

        var tfFiles = Directory.GetFiles(terraformDir, "*.tf");
        Assert.NotEmpty(tfFiles);
        Assert.Contains(tfFiles, f => Path.GetFileName(f) == "instances_aws.tf");
    }

    [Fact]
    public void ProductionRobust_GenerateHcl_DoesNotThrow()
    {
        var topology = DeserializeFixture("production-robust.json");
        var provider = new AwsProvider();

        var files = provider.GenerateHcl(topology);

        Assert.NotEmpty(files);
        Assert.Contains("instances.tf", files.Keys);
        Assert.Contains("provisioning.tf", files.Keys);
        Assert.Contains("variables.tf", files.Keys);
    }

    [Fact]
    public void ProductionRobust_MultiProviderGenerate_DoesNotThrow()
    {
        var topology = DeserializeFixture("production-robust.json");
        var aws = new AwsProvider();
        var linode = new LinodeProvider();
        var registry = new ProviderRegistry([linode, aws]);
        var generator = new MultiProviderHclGenerator(registry, DefaultPlugins.CreateRegistry());

        // This is the code path the API uses - multi-provider since DNS is on linode
        var files = generator.Generate(topology);

        Assert.NotEmpty(files);
        // Should have AWS instance files
        Assert.Contains("instances_aws.tf", files.Keys);
        // Should have Linode DNS files
        Assert.Contains("dns_linode.tf", files.Keys);
    }

    // ── HCL output quality tests ─────────────────────────────────────

    private static Dictionary<string, string> GenerateMultiProviderHcl(string fixtureName = "production-robust.json")
    {
        var topology = DeserializeFixture(fixtureName);
        var registry = new ProviderRegistry([new LinodeProvider(), new AwsProvider()]);
        var generator = new MultiProviderHclGenerator(registry, DefaultPlugins.CreateRegistry());
        return generator.Generate(topology);
    }

    [Fact]
    public void Hcl_ConnectionBlocks_HavePrivateKeyAuth()
    {
        // Terraform can't SSH into instances without authentication.
        // Every connection block must reference the SSH private key.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Every connection block must have a private_key or agent attribute
        var connectionBlocks = provisioning.Split("connection {")
            .Skip(1); // skip text before first connection

        foreach (var block in connectionBlocks)
        {
            var blockEnd = block.IndexOf('}');
            var content = block[..blockEnd];
            Assert.True(
                content.Contains("private_key") || content.Contains("agent"),
                $"Connection block lacks authentication:\n{content}");
        }
    }

    [Fact]
    public void Hcl_DnsLinode_CreatesRecordsForWiredContainers()
    {
        // The DNS file must create actual DNS records, not just a data source.
        var files = GenerateMultiProviderHcl();
        var dns = files["dns_linode.tf"];

        // Must have at least one linode_domain_record resource
        Assert.Contains("linode_domain_record", dns);
    }

    [Fact]
    public void Hcl_Caddyfile_UsesTerraformDomainVariable()
    {
        // Caddyfile domains must use ${var.domain} so Terraform interpolates them.
        // Hardcoded domains break when users change their domain in credentials.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Should NOT contain hardcoded domain references from the topology
        Assert.DoesNotContain("xcord.net", provisioning);

        // Should contain var.domain reference for Terraform interpolation
        Assert.Contains("var.domain", provisioning);
    }

    [Fact]
    public void Hcl_TlsPrivateKey_GeneratedAutomatically()
    {
        // SSH keys are auto-generated by Terraform using tls_private_key,
        // so no user-provided ssh_private_key variable should exist.
        var files = GenerateMultiProviderHcl();
        var variables = files["variables.tf"];

        Assert.DoesNotContain("ssh_private_key", variables);
        Assert.DoesNotContain("ssh_public_key", variables);
    }

    [Fact]
    public void Hcl_ElasticImages_HaveProvisioningResources()
    {
        // Elastic images (hub_server, live_kit with replicas > 1) get their own
        // EC2 instances. Those instances must also have provisioning resources
        // to install Docker and run the container image on them.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // hub_server is a private-registry image - deployed via deploy_apps phase
        Assert.Contains("deploy_hub_server", provisioning);
        // live_kit is a public image - provisioned directly
        Assert.Contains("provision_live_kit", provisioning);
    }

    [Fact]
    public void Hcl_Caddyfile_ElasticUpstreams_UseIpReferences()
    {
        // The Caddy reverse proxy routes to hub_server and live_kit, but these
        // are elastic images on separate instances - not Docker containers on
        // the Caddy host. Caddyfile must use Terraform IP references, not bare
        // container names like "hub_server:80".
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Should NOT contain bare "hub_server:80" - that implies Docker DNS
        Assert.DoesNotContain("hub_server:80", provisioning);
        // Should NOT contain bare "live_kit:7880"
        Assert.DoesNotContain("live_kit:7880", provisioning);

        // Should reference the instance IPs instead
        Assert.Contains("aws_instance.hub_server", provisioning);
        Assert.Contains("aws_instance.live_kit", provisioning);
    }

    [Fact]
    public void Hcl_DnsRecords_IncludeWildcard()
    {
        // The Caddy handles hub.domain, livekit.domain, and *.domain routing.
        // DNS must have a wildcard record so all subdomains resolve to the Caddy IP.
        var files = GenerateMultiProviderHcl();
        var dns = files["dns_linode.tf"];

        // A wildcard record covers all tenant subdomains
        Assert.Contains("\"*\"", dns);
    }

    [Fact]
    public void Hcl_StandaloneCaddy_ProvisionesCoLocatedImages()
    {
        // Non-elastic images directly on a standalone Caddy host (redis_hub, pg_hub,
        // redis_livekit) must be deployed on the Caddy instance alongside the Caddy
        // container. Without this, the backing services have no provisioning at all.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // The Caddy provisioner should start these Docker containers
        Assert.Contains("pg_hub", provisioning);
        Assert.Contains("redis_hub", provisioning);
        Assert.Contains("redis_livekit", provisioning);
    }

    [Fact]
    public void Hcl_Outputs_IncludeElasticImageIps()
    {
        // Elastic images get their own instances and need output blocks
        // so operators can see their IPs after terraform apply.
        var files = GenerateMultiProviderHcl();
        var outputs = files["outputs_aws.tf"];

        Assert.Contains("hub_server", outputs);
        Assert.Contains("live_kit", outputs);
    }

    [Fact]
    public void Hcl_SecurityGroup_IncludesLiveKitPorts()
    {
        // LiveKit needs ports 7880-7882 open for WebRTC. The security group must
        // include these even when LiveKit is an elastic image (not inside a Host).
        var files = GenerateMultiProviderHcl();
        var sg = files["security_groups_aws.tf"];

        Assert.Contains("7880", sg);
    }

    [Fact]
    public void Hcl_Secrets_IncludeCaddyHostedImages()
    {
        // Images directly on a standalone Caddy (pg_hub, redis_hub, redis_livekit)
        // need random_password resources. Without these, Terraform fails on unknown
        // resource references during provisioning.
        var files = GenerateMultiProviderHcl();
        var secrets = files["secrets.tf"];

        Assert.Contains("pg_hub", secrets);
        Assert.Contains("redis_hub", secrets);
        Assert.Contains("redis_livekit", secrets);
    }

    [Fact]
    public void Hcl_StandaloneCaddy_CoLocatedImages_HaveEnvVars()
    {
        // Co-located non-elastic images on a standalone Caddy host must be started
        // with their environment variables (DB passwords, etc). Without -e flags,
        // PostgreSQL starts without a password and Redis has no auth.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Find the standalone Caddy provisioning block specifically
        var caddyProvisionIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyProvisionIdx >= 0, "Expected provision_caddy resource");
        var caddySection = provisioning[caddyProvisionIdx..];

        // pg_hub docker run command must include POSTGRES_PASSWORD env var
        Assert.Contains("POSTGRES_PASSWORD", caddySection);
    }

    [Fact]
    public void Hcl_ElasticImages_HaveEnvVars()
    {
        // Elastic images (hub_server, live_kit) get their own EC2 instances.
        // They must be started with environment variables - hub_server needs
        // connection strings, live_kit needs API keys. Without these, the
        // containers start but immediately crash.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // hub_server provisioning must include connection string env vars
        Assert.Contains("Database__ConnectionString", provisioning);
        // live_kit provisioning must include LiveKit API key env vars
        Assert.Contains("LIVEKIT_KEYS", provisioning);
    }

    [Fact]
    public void Hcl_AwsDns_IncludesWildcard()
    {
        // When DNS is on AWS (single-provider path), the DNS file must include
        // a wildcard record for Caddy to route all tenant subdomains.
        var topology = DeserializeFixture("production-robust.json");
        var provider = new AwsProvider();
        var files = provider.GenerateHcl(topology);

        Assert.True(files.ContainsKey("dns.tf"), "Expected dns.tf file in single-provider output");
        var dns = files["dns.tf"];
        Assert.Contains("wildcard", dns);
        Assert.Contains("*.", dns);
    }

    [Fact]
    public void Hcl_StandaloneCaddy_DependsOnSecrets()
    {
        // The standalone Caddy provisioner references random_password resources
        // in its co-located image env vars. It must declare those as dependencies
        // so Terraform processes them in the correct order.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Find the standalone Caddy provisioning block's depends_on
        var caddyProvisionIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyProvisionIdx >= 0, "Expected provision_caddy resource");

        var afterCaddy = provisioning[caddyProvisionIdx..];
        var dependsOnIdx = afterCaddy.IndexOf("depends_on");
        Assert.True(dependsOnIdx >= 0, "Expected depends_on in caddy provisioner");

        var dependsOnBlock = afterCaddy[dependsOnIdx..afterCaddy.IndexOf(']', dependsOnIdx)];
        Assert.Contains("random_password", dependsOnBlock);
    }

    [Fact]
    public void Hcl_Secrets_NoDuplicatePoolSecrets()
    {
        // The standalone Caddy contains compute pools as children. When generating
        // secrets for Caddy's co-located images, pool images must be excluded to
        // avoid duplicate secrets (e.g. caddy_redis_pro_password vs pro_tier_redis_password).
        var files = GenerateMultiProviderHcl();
        var secrets = files["secrets.tf"];

        // Pool images should NOT have caddy-prefixed secrets - they already have pool-prefixed ones
        Assert.DoesNotContain("caddy_redis_pro", secrets);
        Assert.DoesNotContain("caddy_pg_pro", secrets);
        Assert.DoesNotContain("caddy_mio_pro", secrets);
        Assert.DoesNotContain("caddy_redis_enterprise", secrets);
    }

    [Fact]
    public void Hcl_ElasticImages_UseCaddyPrivateIpForServices()
    {
        // Elastic images (hub_server, live_kit) run on their own EC2 instances.
        // Their connection strings must reference the Caddy instance's private IP,
        // not Docker container names like "pg_hub" which can't resolve across hosts.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        // Find hub_server deploy resource (private-registry images use deploy_apps phase)
        var hubIdx = provisioning.IndexOf("deploy_hub_server");
        Assert.True(hubIdx >= 0, "Expected deploy_hub_server resource");
        var hubSection = provisioning[hubIdx..];

        // Connection string must use Caddy's private IP, not "pg_hub" container name
        Assert.Contains("aws_instance.caddy.private_ip", hubSection);
        // Should NOT use bare container name in connection string Host=
        Assert.DoesNotContain("Host=pg_hub", hubSection);

        // Redis connection must use the correct mapped host port (redis_hub may be
        // offset due to port conflicts with redis_livekit on the same Caddy host)
        // The connection string must match the actual published port, not the default 6379
        var redisConnIdx = hubSection.IndexOf("Redis__ConnectionString=");
        Assert.True(redisConnIdx >= 0, "Expected Redis connection string");
        var redisConn = hubSection[redisConnIdx..hubSection.IndexOf(' ', redisConnIdx)];
        // Must contain the Caddy private IP (not a container name)
        Assert.Contains("aws_instance.caddy.private_ip", redisConn);
    }

    [Fact]
    public void Hcl_CoLocatedServices_PublishPortsForCrossHostAccess()
    {
        // Co-located services on the Caddy host (pg_hub, redis_hub) are consumed by
        // elastic images on separate EC2 instances. Docker doesn't expose unpublished
        // container ports to the host network, so these services need -p flags.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0, "Expected provision_caddy resource");
        var caddySection = provisioning[caddyIdx..];

        // pg_hub must publish port 5432 for hub_server to connect
        Assert.Contains("-p 5432:5432", caddySection);
        // At least one redis must publish 6379
        Assert.Contains(":6379", caddySection);
    }

    [Fact]
    public void Hcl_CoLocatedServices_NoHostPortConflicts()
    {
        // Two Redis containers (redis_livekit, redis_hub) both use container port 6379.
        // They can't both publish to the same host port. The second must get an offset.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0, "Expected provision_caddy resource");
        var caddySection = provisioning[caddyIdx..];

        // Count occurrences of "-p 6379:6379" - should be exactly 1, not 2
        var count = 0;
        var searchIdx = 0;
        while ((searchIdx = caddySection.IndexOf("-p 6379:6379", searchIdx)) >= 0)
        {
            count++;
            searchIdx += 12;
        }
        Assert.Equal(1, count);

        // The second Redis should use a different host port (6380:6379)
        Assert.Contains("-p 6380:6379", caddySection);
    }

    [Fact]
    public void Hcl_LiveKitKeys_NoSpaceInValue()
    {
        // LIVEKIT_KEYS is passed as a Docker -e flag. The value "key: secret" with a
        // space causes shell word-splitting, breaking the docker run command. The format
        // must use "key:secret" (no space) to be safe in unquoted shell context.
        var files = GenerateMultiProviderHcl();
        var provisioning = files["provisioning_aws.tf"];

        var liveKitIdx = provisioning.IndexOf("provision_live_kit");
        Assert.True(liveKitIdx >= 0, "Expected provision_live_kit resource");
        var liveKitSection = provisioning[liveKitIdx..];

        // LIVEKIT_KEYS value must not contain "}: ${" (space after colon between key/secret)
        Assert.DoesNotContain(".result)}: ${nonsensitive(random_password", liveKitSection);
        // Verify the format is key:secret with no space
        Assert.Contains(".result)}:${nonsensitive(random_password", liveKitSection);
    }

    // ── Elastic topology fixture tests ───────────────────────────────

    [Fact]
    public void ElasticFixture_Deserializes()
    {
        var topology = DeserializeFixture("production-elastic.json");
        Assert.Equal("Production - Elastic", topology.Name);
    }

    [Fact]
    public void ElasticFixture_RoundTrips()
    {
        var topology = DeserializeFixture("production-elastic.json");
        var json = JsonSerializer.Serialize(topology, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<Topology>(json, JsonOptions)!;
        Assert.Equal(topology.Name, roundTripped.Name);
        Assert.Equal(topology.Containers.Count, roundTripped.Containers.Count);
    }

    [Fact]
    public void ElasticFixture_HasDataPool()
    {
        var topology = DeserializeFixture("production-elastic.json");
        var dataPool = FindContainer(topology.Containers, ContainerKind.DataPool);
        Assert.NotNull(dataPool);
        Assert.Equal("Data Pool", dataPool.Name);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.PostgreSQL);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.Redis);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.MinIO);
    }

    [Fact]
    public void ElasticFixture_ComputePoolHasNoTierProfile()
    {
        var topology = DeserializeFixture("production-elastic.json");
        var pool = FindContainer(topology.Containers, ContainerKind.ComputePool);
        Assert.NotNull(pool);
        Assert.False(pool.Config.ContainsKey("tierProfile"));
    }

    [Fact]
    public void ElasticFixture_GeneratesHcl()
    {
        var files = GenerateMultiProviderHcl("production-elastic.json");
        Assert.Contains("instances_aws.tf", files.Keys);
        Assert.Contains("provisioning_aws.tf", files.Keys);
        Assert.Contains("secrets.tf", files.Keys);
    }

    [Fact]
    public void ElasticFixture_DataPoolGeneratesInstance()
    {
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var instances = files["instances_aws.tf"];
        Assert.Contains("aws_instance", instances);
        Assert.Contains("data_pool", instances);
    }

    [Fact]
    public void ElasticFixture_DnsReferencesExistingInstances()
    {
        // DNS records must reference instance resources that actually exist.
        // Pool Caddy runs as a Swarm service inside compute_pool - the DNS record
        // must reference aws_instance.compute_pool, not a non-existent pool_caddy instance.
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var dns = files["dns_linode.tf"];
        var instances = files["instances_aws.tf"];

        // Every aws_instance.X referenced in DNS must exist in instances
        Assert.DoesNotContain("aws_instance.pool_caddy", dns);
    }

    [Fact]
    public void ElasticFixture_CaddyfileNoStaticWildcardRoute()
    {
        // Pool infrastructure is deferred (count=0 on initial deploy).
        // Caddy must NOT have a static wildcard route - compute_pool[0].private_ip
        // would be an invalid Terraform reference when count=0.
        // Hub configures wildcard tenant routing via Caddy admin API at runtime.
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0);
        var caddySection = provisioning[caddyIdx..];

        // Must NOT have a wildcard route - pool IPs don't exist at initial deploy
        Assert.DoesNotContain("*.${var.domain}", caddySection);
    }

    [Fact]
    public void ElasticFixture_NoSmtpEnvVarsWithoutServiceKeys()
    {
        // When a topology has no service keys defined, no SMTP env vars should be emitted.
        // A hardcoded Email__SmtpPort=587 fallback is wrong when there's no SMTP config at all.
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var provisioning = files["provisioning_aws.tf"];

        var hubIdx = provisioning.IndexOf("deploy_hub_server");
        Assert.True(hubIdx >= 0);
        var hubSection = provisioning[hubIdx..];

        Assert.DoesNotContain("Email__SmtpPort=587", hubSection);
        Assert.DoesNotContain("Email__SmtpHost", hubSection);
    }

    [Fact]
    public void ElasticFixture_DataPoolHasSecrets()
    {
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var secrets = files["secrets.tf"];
        // DataPool images should have secrets for PG, Redis, MinIO
        Assert.Contains("pg_tenants", secrets);
        Assert.Contains("redis_tenants", secrets);
        Assert.Contains("mio_tenants", secrets);
    }

    [Fact]
    public void ElasticFixture_ComputePoolNoHardcodedSharedServices()
    {
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var provisioning = files["provisioning_aws.tf"];
        // Pool provisioning should NOT contain hardcoded "shared-postgres" etc.
        // The pool has no images, so no shared services should be deployed
        var poolIdx = provisioning.IndexOf("provision_compute_pool_manager");
        if (poolIdx >= 0)
        {
            var poolSection = provisioning[poolIdx..];
            Assert.DoesNotContain("shared-postgres", poolSection);
            Assert.DoesNotContain("shared-redis", poolSection);
            Assert.DoesNotContain("shared-minio", poolSection);
        }
    }

    [Fact]
    public void ElasticFixture_ProductionRobustStillWorks()
    {
        // Regression test: the original fixture must still generate valid HCL
        var files = GenerateMultiProviderHcl("production-robust.json");
        Assert.Contains("instances_aws.tf", files.Keys);
        Assert.Contains("provisioning_aws.tf", files.Keys);
        var provisioning = files["provisioning_aws.tf"];
        // Pool provisioning should still deploy data services (now data-driven from images)
        Assert.Contains("postgres", provisioning);
        Assert.Contains("redis", provisioning);
        Assert.Contains("minio", provisioning);
    }

    // ── Production-Simple fixture tests ─────────────────────────────

    [Fact]
    public void SimpleFixture_Deserializes()
    {
        var topology = DeserializeFixture("production-simple.json");

        Assert.Equal("Production - Simple", topology.Name);
        Assert.Equal("aws", topology.Provider);
        Assert.Single(topology.Containers); // DNS wrapper
        Assert.Equal(15, topology.Wires.Count);
        Assert.Equal(4, topology.TierProfiles.Count);
        Assert.Equal(6, topology.ServiceKeys.Count);
        Assert.Equal("docker.xcord.net", topology.Registry);
    }

    [Fact]
    public void SimpleFixture_RoundTrips()
    {
        var topology = DeserializeFixture("production-simple.json");
        var json = JsonSerializer.Serialize(topology, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<Topology>(json, JsonOptions)!;

        Assert.Equal(topology.Name, roundTripped.Name);
        Assert.Equal(topology.Containers.Count, roundTripped.Containers.Count);
        Assert.Equal(topology.Wires.Count, roundTripped.Wires.Count);
        Assert.Equal(topology.TierProfiles.Count, roundTripped.TierProfiles.Count);
        Assert.Equal(topology.ServiceKeys.Count, roundTripped.ServiceKeys.Count);
    }

    [Fact]
    public void SimpleFixture_HasDataPool()
    {
        var topology = DeserializeFixture("production-simple.json");
        var dataPool = FindContainer(topology.Containers, ContainerKind.DataPool);
        Assert.NotNull(dataPool);
        Assert.Equal("Data Pool", dataPool.Name);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.PostgreSQL);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.Redis);
        Assert.Contains(dataPool.Images, i => i.Kind == ImageKind.MinIO);
    }

    [Fact]
    public void SimpleFixture_ComputePoolHasFederationServer()
    {
        var topology = DeserializeFixture("production-simple.json");
        var pool = FindContainer(topology.Containers, ContainerKind.ComputePool);
        Assert.NotNull(pool);
        Assert.Contains(pool.Images, i => i.Kind == ImageKind.FederationServer);
    }

    [Fact]
    public void SimpleFixture_HasTierProfiles()
    {
        var topology = DeserializeFixture("production-simple.json");
        Assert.Equal(4, topology.TierProfiles.Count);
        Assert.Contains(topology.TierProfiles, p => p.Id == "free");
        Assert.Contains(topology.TierProfiles, p => p.Id == "basic");
        Assert.Contains(topology.TierProfiles, p => p.Id == "pro");
        Assert.Contains(topology.TierProfiles, p => p.Id == "enterprise");
    }

    [Fact]
    public void SimpleFixture_GeneratesHcl()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        Assert.Contains("instances_aws.tf", files.Keys);
        Assert.Contains("provisioning_aws.tf", files.Keys);
        Assert.Contains("secrets.tf", files.Keys);
        Assert.Contains("dns_linode.tf", files.Keys);
    }

    [Fact]
    public void SimpleFixture_DataPoolGeneratesInstance()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var instances = files["instances_aws.tf"];
        Assert.Contains("aws_instance", instances);
        Assert.Contains("data_pool", instances);
    }

    [Fact]
    public void SimpleFixture_DataPoolHasSecrets()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var secrets = files["secrets.tf"];
        Assert.Contains("data_pool_pg", secrets);
        Assert.Contains("data_pool_redis", secrets);
        Assert.Contains("data_pool_mio", secrets);
    }

    [Fact]
    public void SimpleFixture_ElasticImagesHaveProvisioning()
    {
        // hub_server (1-3) and live_kit (1-10) are elastic - they get their own AWS instances
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];
        // hub_server is a private-registry image - deployed via deploy_apps phase
        Assert.Contains("deploy_hub_server", provisioning);
        // live_kit is a public image - provisioned directly
        Assert.Contains("provision_live_kit", provisioning);
    }

    [Fact]
    public void SimpleFixture_CaddyCoLocatedServicesHaveEnvVars()
    {
        // pg_hub, redis_hub, redis_livekit are co-located on the Caddy AWS host
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0, "Expected provision_caddy resource");
        var caddySection = provisioning[caddyIdx..];

        Assert.Contains("POSTGRES_PASSWORD", caddySection);
        Assert.Contains("pg_hub", caddySection);
        Assert.Contains("redis_hub", caddySection);
        Assert.Contains("redis_livekit", caddySection);
    }

    [Fact]
    public void SimpleFixture_PortConflictsHandled()
    {
        // redis_hub and redis_livekit both use 6379 - one must get an offset
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0, "Expected provision_caddy resource");
        var caddySection = provisioning[caddyIdx..];

        // Exactly one redis at 6379, the other at 6380
        var count6379 = 0;
        var searchIdx = 0;
        while ((searchIdx = caddySection.IndexOf("-p 6379:6379", searchIdx)) >= 0)
        {
            count6379++;
            searchIdx += 12;
        }
        Assert.Equal(1, count6379);
        Assert.Contains("-p 6380:6379", caddySection);
    }

    [Fact]
    public void SimpleFixture_DataPoolNotDuplicatedOnCaddy()
    {
        // DataPool images (pg, redis, mio) live on the DataPool's own AWS instance.
        // They should NOT also be provisioned on the Caddy host.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0, "Expected provision_caddy resource");
        // Get only the caddy section (stop at next resource block)
        var afterCaddy = provisioning[caddyIdx..];
        var nextResource = afterCaddy.IndexOf("resource \"null_resource\"", 10);
        var caddySection = nextResource >= 0 ? afterCaddy[..nextResource] : afterCaddy;

        // DataPool images should NOT be on the Caddy host
        Assert.DoesNotContain("mio ", caddySection);
    }

    [Fact]
    public void SimpleFixture_DataPoolHasProvisioning()
    {
        // DataPool is on AWS - its images should be provisioned there
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];
        Assert.Contains("provision_data_pool", provisioning);
        Assert.Contains("docker run", provisioning);
    }

    [Fact]
    public void SimpleFixture_SecretsUseDataPoolPrefix()
    {
        // DataPool images must have data_pool_* prefixed secrets, not caddy_*
        var files = GenerateMultiProviderHcl("production-simple.json");
        var secrets = files["secrets.tf"];
        Assert.Contains("data_pool_pg", secrets);
        Assert.Contains("data_pool_redis", secrets);
        Assert.Contains("data_pool_mio", secrets);
        // Must NOT have caddy-prefixed secrets for DataPool images
        Assert.DoesNotContain("caddy_pg\"", secrets);
        Assert.DoesNotContain("caddy_redis\"", secrets);
        Assert.DoesNotContain("caddy_mio\"", secrets);
    }

    [Fact]
    public void SimpleFixture_DataPoolImagesPublishPorts()
    {
        // FederationServer on ComputePool connects to DataPool images cross-host.
        // DataPool images (pg_free, redis_free) must publish ports on the DataPool host.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // Extract ONLY the data_pool provisioning block (stop before the next resource)
        var dataPoolIdx = provisioning.IndexOf("provision_data_pool");
        Assert.True(dataPoolIdx >= 0, "Expected provision_data_pool resource");
        var afterDataPool = provisioning[dataPoolIdx..];
        var nextResourceIdx = afterDataPool.IndexOf("resource \"null_resource\"", 10);
        var dataPoolSection = nextResourceIdx >= 0 ? afterDataPool[..nextResourceIdx] : afterDataPool;

        // pg_free must publish 5432 for cross-host access, bound to private IP
        Assert.Contains("5432:5432", dataPoolSection);
        // redis_free must publish 6379
        Assert.Contains("6379", dataPoolSection);
    }

    [Fact]
    public void SimpleFixture_PoolCaddyHasCaddyfileMount()
    {
        // Pool Caddy must mount a Caddyfile from the host so the hub can update
        // routing configuration at runtime when FederationServer containers are deployed.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // Pool provisioner name is tier-qualified: provision_compute_pool_<tier_id>_manager
        var poolIdx = provisioning.IndexOf("provision_compute_pool_free_manager");
        Assert.True(poolIdx >= 0);
        var poolSection = provisioning[poolIdx..];

        Assert.Contains("Caddyfile", poolSection);
        Assert.Contains("/etc/caddy/Caddyfile", poolSection);
    }

    [Fact]
    public void SimpleFixture_PoolHasNoHardcodedDataServices()
    {
        // Data services live on the separate DataPool - the ComputePool's Swarm provisioning
        // must NOT deploy them. FederationServer connection strings are hub-provisioned at runtime.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // Pool provisioner name is tier-qualified: provision_compute_pool_<tier_id>_manager
        var poolIdx = provisioning.IndexOf("provision_compute_pool_free_manager");
        Assert.True(poolIdx >= 0, "Expected provision_compute_pool_free_manager resource");
        var poolSection = provisioning[poolIdx..];

        Assert.DoesNotContain("shared-postgres", poolSection);
        Assert.DoesNotContain("shared-redis", poolSection);
        Assert.DoesNotContain("shared-minio", poolSection);
    }

    [Fact]
    public void SimpleFixture_CaddyfileUpstreamsUseElasticIps()
    {
        // hub_server and live_kit are elastic AWS instances.
        // Caddyfile must use aws_instance IP references for upstreams.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0);
        var caddySection = provisioning[caddyIdx..];

        // Must reference elastic AWS instances, not Docker container names
        Assert.Contains("aws_instance.hub_server", caddySection);
        Assert.Contains("aws_instance.live_kit", caddySection);
        Assert.DoesNotContain("hub_server:80", caddySection);
    }

    [Fact]
    public void SimpleFixture_VariablesIncludeReplicaCounts()
    {
        // hub_server (1-3), live_kit (1-100) are elastic with replica variables
        var files = GenerateMultiProviderHcl("production-simple.json");
        var variables = files["variables.tf"];
        Assert.Contains("hub_server_replicas", variables);
        Assert.Contains("live_kit_replicas", variables);
        // Pool host counts are tier-qualified: compute_pool_<tier_id>_host_count
        Assert.Contains("compute_pool_free_host_count", variables);
    }

    [Fact]
    public void SimpleFixture_DnsHasWildcardRecord()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var dns = files["dns_linode.tf"];
        Assert.Contains("linode_domain_record", dns);
        Assert.Contains("\"*\"", dns);
    }

    [Fact]
    public void SimpleFixture_NoLatestTagInProvisioning()
    {
        // Production HCL must use pinned image versions, never :latest.
        // Private images (hub, fed) use ${var.app_version} via GetDockerImageForHcl.
        // Third-party images use whatever tag the topology specifies.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var awsProvisioning = files["provisioning_aws.tf"];

        Assert.DoesNotContain(":latest", awsProvisioning);
    }

    [Fact]
    public void RobustFixture_NoLatestTagInProvisioning()
    {
        var files = GenerateMultiProviderHcl("production-robust.json");
        var provisioning = files["provisioning_aws.tf"];

        Assert.DoesNotContain(":latest", provisioning);
    }

    [Fact]
    public void ElasticFixture_NoLatestTagInProvisioning()
    {
        var files = GenerateMultiProviderHcl("production-elastic.json");
        var provisioning = files["provisioning_aws.tf"];

        Assert.DoesNotContain(":latest", provisioning);
    }

    [Fact]
    public void SimpleFixture_InstanceCountBounded()
    {
        // Safety: verify the total number of instance resources is bounded.
        // All on AWS: caddy (1), hub_server (var), live_kit (var), data_pool (1), compute_pool (var)
        var files = GenerateMultiProviderHcl("production-simple.json");
        var awsInstances = files["instances_aws.tf"];

        // Count aws_instance resources
        var awsCount = awsInstances.Split("aws_instance").Length - 1;

        // There should be a predictable number of instance resources
        Assert.InRange(awsCount, 4, 8); // caddy + hub_server + live_kit + data_pool + compute_pool
    }

    [Fact]
    public void SimpleFixture_ProductionRobustStillWorks()
    {
        // Regression: existing fixture must still generate valid HCL
        var files = GenerateMultiProviderHcl("production-robust.json");
        Assert.Contains("instances_aws.tf", files.Keys);
        Assert.Contains("provisioning_aws.tf", files.Keys);
    }

    [Fact]
    public void SimpleFixture_HubServerProvisioning_IncludesSmtpPassword()
    {
        // smtp_password is not in topology.ServiceKeys (stored in credential store)
        // but the variable IS declared in variables.tf - provisioning must reference it
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];
        Assert.Contains("Email__SmtpPassword=${nonsensitive(var.smtp_password)}", provisioning);
    }

    [Fact]
    public void SimpleFixture_HubServerProvisioning_SmtpPortUsesVariable()
    {
        // smtp_port is not in topology.ServiceKeys but variable IS declared -
        // provisioning must use ${var.smtp_port}, not hardcode 587
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];
        Assert.Contains("Email__SmtpPort=${var.smtp_port}", provisioning);
    }

    [Fact]
    public void SimpleFixture_SecurityGroups_IncludeElasticInstances()
    {
        // hub_server and live_kit are elastic AWS instances. Security groups must
        // protect ALL AWS instances, not just the Caddy host.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var securityGroups = files["security_groups_aws.tf"];

        Assert.Contains("aws_security_group", securityGroups);
    }

    [Fact]
    public void SimpleFixture_HubServerImage_UsesRegistryVariable()
    {
        // HubServer is an xcord image - it must pull from the configurable registry,
        // not a hardcoded ghcr.io URL. Deployed via deploy_apps phase (post-push).
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var hubIdx = provisioning.IndexOf("deploy_hub_server");
        Assert.True(hubIdx >= 0);
        var hubSection = provisioning[hubIdx..];

        // Must use the registry variable, not hardcoded ghcr.io
        Assert.Contains("${var.registry_url}", hubSection);
        Assert.DoesNotContain("ghcr.io", hubSection);
    }

    [Fact]
    public void RobustFixture_PoolFedServer_UsesRegistryVariable()
    {
        // FederationServer is an xcord image - pool provisioning must use ${var.registry_url}
        var files = GenerateMultiProviderHcl("production-robust.json");
        var provisioning = files["provisioning_aws.tf"];
        Assert.DoesNotContain("ghcr.io", provisioning);
        Assert.Contains("${var.registry_url}/fed:${var.fed_version}", provisioning);
    }

    [Fact]
    public void RobustFixture_PoolFedServer_HasResolvedConnectionStrings()
    {
        // FederationServer env vars must have real Terraform refs, not {pg}/{redis} placeholders
        var files = GenerateMultiProviderHcl("production-robust.json");
        var provisioning = files["provisioning_aws.tf"];

        // Should NOT contain unresolved placeholders
        Assert.DoesNotContain("={pg}", provisioning);
        Assert.DoesNotContain("={redis}", provisioning);
        Assert.DoesNotContain("={minio_endpoint}", provisioning);
    }

    // ══════════════════════════════════════════════════════════════════
    // Phase 1 TDD: Critical HCL issues (all must FAIL before code fix)
    // ══════════════════════════════════════════════════════════════════

    // --- Issue 4: Pool Caddy has admin API for runtime updates ---

    [Fact]
    public void SimpleFixture_PoolCaddyHasAdminApi()
    {
        // Pool Caddy starts empty - the hub updates it at runtime when tenants are provisioned.
        // It must have the admin API enabled so the hub can reload config via API.
        // Without admin API, there's no way to update routing without container restart.
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // Pool provisioner name is tier-qualified: provision_compute_pool_<tier_id>_manager
        var poolIdx = provisioning.IndexOf("provision_compute_pool_free_manager");
        Assert.True(poolIdx >= 0, "Expected provision_compute_pool_free_manager resource");
        var poolSection = provisioning[poolIdx..];

        // The Caddy container must expose the admin API port (2019) for runtime config updates
        Assert.Contains("2019", poolSection);
    }

    // --- Issue 5 (fixture): Caddyfile upstreams use private_ip ---

    [Fact]
    public void SimpleFixture_CaddyfileUpstreamsUsePrivateIp()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        var caddyIdx = provisioning.IndexOf("provision_caddy");
        Assert.True(caddyIdx >= 0);
        var caddySection = provisioning[caddyIdx..];

        // Extract the Caddyfile heredoc content (between CADDYEOF markers)
        var caddyStart = caddySection.IndexOf("CADDYEOF");
        Assert.True(caddyStart >= 0, "Expected CADDYEOF start");
        var caddyEnd = caddySection.IndexOf("CADDYEOF", caddyStart + 8);
        Assert.True(caddyEnd >= 0, "Expected CADDYEOF end");
        var caddyfileContent = caddySection[(caddyStart + 8)..caddyEnd];

        // Upstreams to elastic instances (hub_server, live_kit) must use private_ip
        // for same-VPC communication, not public_ip
        Assert.DoesNotContain("public_ip", caddyfileContent);
    }

    // --- Issue 7: No DNS record for docker.xcord.net ---

    [Fact]
    public void SimpleFixture_DnsIncludesRegistryRecord()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var dns = files["dns_linode.tf"];

        // Registry needs a DNS A record using its name-derived subdomain
        Assert.Contains("registry", dns);
    }

    // --- Issue 14: iptables rate limit 100/min too restrictive ---

    [Fact]
    public void SimpleFixture_RateLimitNotTooRestrictive()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // 100/min is too restrictive for normal browsing - a page with 50 assets hits it in 2 requests
        Assert.DoesNotContain("100/min", provisioning);
    }

    // --- Issues 16-18: DB ports bound to all interfaces ---

    [Fact]
    public void SimpleFixture_DataPoolPortsBoundToPrivateIp()
    {
        var files = GenerateMultiProviderHcl("production-simple.json");
        var provisioning = files["provisioning_aws.tf"];

        // Extract ONLY the data_pool provisioning block
        var dataPoolIdx = provisioning.IndexOf("provision_data_pool");
        Assert.True(dataPoolIdx >= 0, "Expected provision_data_pool resource");
        var afterDataPool = provisioning[dataPoolIdx..];
        var nextResourceIdx = afterDataPool.IndexOf("resource \"null_resource\"", 10);
        var dataPoolSection = nextResourceIdx >= 0 ? afterDataPool[..nextResourceIdx] : afterDataPool;

        // Ports should NOT be bound to all interfaces (0.0.0.0)
        // -p 5432:5432 means bind to all interfaces - should be -p $PRIVATE_IP:5432:5432 instead
        Assert.DoesNotContain("-p 5432:5432", dataPoolSection);
        Assert.DoesNotContain("-p 6379:6379", dataPoolSection);
        Assert.DoesNotContain("-p 9000:9000", dataPoolSection);
    }

    private static Container? FindContainer(List<Container> containers, ContainerKind kind)
    {
        foreach (var c in containers)
        {
            if (c.Kind == kind) return c;
            var found = FindContainer(c.Children, kind);
            if (found != null) return found;
        }
        return null;
    }
}
