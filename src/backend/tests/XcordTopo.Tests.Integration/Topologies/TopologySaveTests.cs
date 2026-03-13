using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Topologies;

public sealed class TopologySaveTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;
    private readonly TopoWebApplicationFactory _factory;

    public TopologySaveTests(TopoWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SaveProductionRobust_PersistsJsonToDisk()
    {
        // Load the exact config the UI sends
        var assembly = typeof(TopologySaveTests).Assembly;
        // The fixture lives in the unit test project — load it from the file system instead
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "XcordTopo.Tests.Unit", "Fixtures", "production-robust.json");
        var fixtureJson = await File.ReadAllTextAsync(Path.GetFullPath(fixturePath));
        var topology = JsonSerializer.Deserialize<JsonElement>(fixtureJson, JsonOptions);
        var id = topology.GetProperty("id").GetString()!;

        // PUT — same as clicking Save in the UI
        var response = await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", topology, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the file was written to the data directory on disk
        var savedPath = Path.Combine(_factory.DataPath, "topologies", $"{id}.json");
        Assert.True(File.Exists(savedPath), $"Expected file at {savedPath} but it doesn't exist");

        // Verify content is correct
        var savedJson = await File.ReadAllTextAsync(savedPath);
        var saved = JsonSerializer.Deserialize<JsonElement>(savedJson, JsonOptions);
        Assert.Equal("Production — Robust", saved.GetProperty("name").GetString());

        // updatedAt should have been refreshed (not the original static date)
        var updatedAt = saved.GetProperty("updatedAt").GetString()!;
        Assert.NotEqual("2026-02-20T00:00:00Z", updatedAt);
        Assert.NotEqual("2026-02-20T00:00:00+00:00", updatedAt);
    }

    [Fact]
    public async Task SaveProductionRobust_ThenModifyAndSave_UpdatesFileOnDisk()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "XcordTopo.Tests.Unit", "Fixtures", "production-robust.json");
        var fixtureJson = await File.ReadAllTextAsync(Path.GetFullPath(fixturePath));
        var topology = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fixtureJson, JsonOptions)!;
        var id = topology["id"].GetString()!;

        // First save
        var response1 = await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", topology, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var savedPath = Path.Combine(_factory.DataPath, "topologies", $"{id}.json");
        var firstContent = await File.ReadAllTextAsync(savedPath);
        var firstUpdatedAt = JsonSerializer.Deserialize<JsonElement>(firstContent, JsonOptions)
            .GetProperty("updatedAt").GetString();

        // Modify name and save again (simulates user editing + clicking Save)
        topology["name"] = JsonSerializer.SerializeToElement("Renamed Topology", JsonOptions);
        var response2 = await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", topology, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // File on disk must reflect the change
        var secondContent = await File.ReadAllTextAsync(savedPath);
        Assert.Contains("Renamed Topology", secondContent);
        Assert.DoesNotContain("Production — Robust", secondContent);
    }

    [Fact]
    public async Task SaveTopology_AllFieldsSurviveRoundTrip()
    {
        // Build a topology that exercises every model field, enum value, and nesting level.
        // This is the contract the UI depends on — if any field is silently dropped
        // during PUT → disk → GET, this test catches it.
        var id = Guid.NewGuid();
        var dnsId = Guid.NewGuid();
        var caddyId = Guid.NewGuid();
        var computePoolId = Guid.NewGuid();
        var dataPoolId = Guid.NewGuid();
        var hubId = Guid.NewGuid();
        var fedId = Guid.NewGuid();
        var pgId = Guid.NewGuid();
        var redisId = Guid.NewGuid();
        var minioId = Guid.NewGuid();
        var livekitId = Guid.NewGuid();
        var registryId = Guid.NewGuid();
        var hubPortId = Guid.NewGuid();
        var hubPgPortId = Guid.NewGuid();
        var pgPortId = Guid.NewGuid();
        var fedPortId = Guid.NewGuid();
        var fedPgPortId = Guid.NewGuid();
        var redisPortId = Guid.NewGuid();
        var minioPortId = Guid.NewGuid();
        var livekitPortId = Guid.NewGuid();
        var dnsPortId = Guid.NewGuid();
        var caddyHttpPortId = Guid.NewGuid();
        var caddyUpstreamPortId = Guid.NewGuid();
        var poolPublicPortId = Guid.NewGuid();
        var poolControlPortId = Guid.NewGuid();
        var dataPoolSshPortId = Guid.NewGuid();
        var dataPoolPublicPortId = Guid.NewGuid();

        var topology = new
        {
            id,
            name = "Round Trip Test",
            description = "Exercises every model field",
            provider = "aws",
            providerConfig = new Dictionary<string, string> { ["aws_region"] = "us-east-1" },
            serviceKeys = new Dictionary<string, string>
            {
                ["smtp_host"] = "mail.test.net",
                ["smtp_username"] = "admin@test.net",
                ["registry_url"] = "docker.test.net",
                ["registry_username"] = "docker_admin"
            },
            registry = "docker.test.net",
            schemaVersion = 1,
            deployedResourceCount = 5,
            lastDeployStatus = "Succeeded",
            lastDeployedAt = "2026-03-10T00:00:00+00:00",
            tierProfiles = new object[]
            {
                new
                {
                    id = "free", name = "Free Tier",
                    imageSpecs = new Dictionary<string, object>
                    {
                        ["FederationServer"] = new { memoryMb = 256, cpuMillicores = 250, diskMb = 512 }
                    }
                },
                new
                {
                    id = "pro", name = "Pro Tier",
                    imageSpecs = new Dictionary<string, object>
                    {
                        ["FederationServer"] = new { memoryMb = 1024, cpuMillicores = 1000, diskMb = 5120 }
                    }
                }
            },
            containers = new object[]
            {
                new
                {
                    id = dnsId,
                    name = "Test DNS",
                    kind = "Dns",
                    x = 10.5, y = 20.3, width = 900.0, height = 600.0,
                    config = new Dictionary<string, string> { ["domain"] = "test.net", ["provider"] = "linode" },
                    ports = new object[]
                    {
                        new { id = dnsPortId, name = "records", type = "Network", direction = "In", side = "Left", offset = 0.5 }
                    },
                    images = new object[0],
                    children = new object[]
                    {
                        new
                        {
                            id = caddyId,
                            name = "Caddy",
                            kind = "Caddy",
                            x = 20.0, y = 15.0, width = 860.0, height = 550.0,
                            config = new Dictionary<string, string> { ["domain"] = "test.net" },
                            ports = new object[]
                            {
                                new { id = caddyHttpPortId, name = "http_in", type = "Network", direction = "In", side = "Top", offset = 0.3 },
                                new { id = caddyUpstreamPortId, name = "upstream", type = "Network", direction = "Out", side = "Bottom", offset = 0.5 }
                            },
                            images = new object[]
                            {
                                new
                                {
                                    id = hubId, name = "hub_server", kind = "HubServer",
                                    x = 50.0, y = 50.0, width = 140.0, height = 60.0,
                                    dockerImage = "ghcr.io/xcord/hub:v0.1.0",
                                    config = new Dictionary<string, string> { ["replicas"] = "1-3" },
                                    scaling = "Shared",
                                    ports = new object[]
                                    {
                                        new { id = hubPortId, name = "http", type = "Network", direction = "In", side = "Left", offset = 0.5 },
                                        new { id = hubPgPortId, name = "pg", type = "Database", direction = "Out", side = "Right", offset = 0.33 }
                                    }
                                },
                                new
                                {
                                    id = pgId, name = "pg_hub", kind = "PostgreSQL",
                                    x = 250.0, y = 50.0, width = 120.0, height = 50.0,
                                    dockerImage = "postgres:17-alpine",
                                    config = new Dictionary<string, string>(),
                                    scaling = "Shared",
                                    ports = new object[]
                                    {
                                        new { id = pgPortId, name = "postgres", type = "Database", direction = "In", side = "Left", offset = 0.5 }
                                    }
                                },
                                new
                                {
                                    id = redisId, name = "redis_hub", kind = "Redis",
                                    x = 250.0, y = 130.0, width = 120.0, height = 50.0,
                                    dockerImage = "redis:7-alpine",
                                    config = new Dictionary<string, string>(),
                                    scaling = "Shared",
                                    ports = new object[]
                                    {
                                        new { id = redisPortId, name = "redis", type = "Database", direction = "In", side = "Left", offset = 0.5 }
                                    }
                                },
                                new
                                {
                                    id = livekitId, name = "livekit", kind = "LiveKit",
                                    x = 50.0, y = 200.0, width = 120.0, height = 50.0,
                                    dockerImage = "livekit/livekit-server:v1.8.3",
                                    config = new Dictionary<string, string> { ["replicas"] = "1-10" },
                                    scaling = "Shared",
                                    ports = new object[]
                                    {
                                        new { id = livekitPortId, name = "rtc", type = "Network", direction = "InOut", side = "Left", offset = 0.5 }
                                    }
                                },
                                new
                                {
                                    id = registryId, name = "registry", kind = "Registry",
                                    x = 300.0, y = 300.0, width = 140.0, height = 60.0,
                                    dockerImage = "registry:2",
                                    config = new Dictionary<string, string>(),
                                    scaling = "Shared",
                                    ports = new object[0]
                                }
                            },
                            children = new object[]
                            {
                                new
                                {
                                    id = dataPoolId,
                                    name = "Data Pool",
                                    kind = "DataPool",
                                    x = 50.0, y = 300.0, width = 380.0, height = 200.0,
                                    config = new Dictionary<string, string>(),
                                    ports = new object[]
                                    {
                                        new { id = dataPoolSshPortId, name = "ssh", type = "Control", direction = "In", side = "Left", offset = 0.5 },
                                        new { id = dataPoolPublicPortId, name = "public", type = "Network", direction = "InOut", side = "Right", offset = 0.3 }
                                    },
                                    images = new object[]
                                    {
                                        new
                                        {
                                            id = minioId, name = "mio", kind = "MinIO",
                                            x = 20.0, y = 30.0, width = 120.0, height = 50.0,
                                            dockerImage = "minio/minio:RELEASE.2025-02-28T09-55-16Z",
                                            config = new Dictionary<string, string>(),
                                            scaling = "Shared",
                                            ports = new object[]
                                            {
                                                new { id = minioPortId, name = "s3", type = "Storage", direction = "In", side = "Left", offset = 0.5 }
                                            }
                                        }
                                    },
                                    children = new object[0]
                                },
                                new
                                {
                                    id = computePoolId,
                                    name = "Compute Pool",
                                    kind = "ComputePool",
                                    x = 460.0, y = 300.0, width = 380.0, height = 200.0,
                                    config = new Dictionary<string, string>(),
                                    ports = new object[]
                                    {
                                        new { id = poolPublicPortId, name = "public", type = "Network", direction = "InOut", side = "Right", offset = 0.5 },
                                        new { id = poolControlPortId, name = "control", type = "Control", direction = "In", side = "Left", offset = 0.5 }
                                    },
                                    images = new object[]
                                    {
                                        new
                                        {
                                            id = fedId, name = "fed_free", kind = "FederationServer",
                                            x = 20.0, y = 20.0, width = 140.0, height = 60.0,
                                            dockerImage = "ghcr.io/xcord/fed:v0.1.0",
                                            config = new Dictionary<string, string>(),
                                            scaling = "PerTenant",
                                            ports = new object[]
                                            {
                                                new { id = fedPortId, name = "http", type = "Network", direction = "In", side = "Left", offset = 0.5 },
                                                new { id = fedPgPortId, name = "pg", type = "Database", direction = "Out", side = "Right", offset = 0.25 }
                                            }
                                        }
                                    },
                                    children = new object[0]
                                }
                            }
                        }
                    }
                }
            },
            wires = new object[]
            {
                new { id = Guid.NewGuid(), fromNodeId = hubId, fromPortId = hubPgPortId, toNodeId = pgId, toPortId = pgPortId },
                new { id = Guid.NewGuid(), fromNodeId = fedId, fromPortId = fedPgPortId, toNodeId = minioId, toPortId = minioPortId }
            }
        };

        // PUT — same as clicking Save in the UI
        var putResponse = await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", topology, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        // GET — same as UI reloading the topology
        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // --- Topology-level scalars ---
        Assert.Equal(id.ToString(), loaded.GetProperty("id").GetString());
        Assert.Equal("Round Trip Test", loaded.GetProperty("name").GetString());
        Assert.Equal("Exercises every model field", loaded.GetProperty("description").GetString());
        Assert.Equal("aws", loaded.GetProperty("provider").GetString());
        Assert.Equal("docker.test.net", loaded.GetProperty("registry").GetString());
        Assert.Equal(1, loaded.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(5, loaded.GetProperty("deployedResourceCount").GetInt32());
        Assert.Equal("Succeeded", loaded.GetProperty("lastDeployStatus").GetString());

        // --- Dictionaries ---
        Assert.Equal("us-east-1", loaded.GetProperty("providerConfig").GetProperty("aws_region").GetString());
        var keys = loaded.GetProperty("serviceKeys");
        Assert.Equal("mail.test.net", keys.GetProperty("smtp_host").GetString());
        Assert.Equal("admin@test.net", keys.GetProperty("smtp_username").GetString());
        Assert.Equal("docker.test.net", keys.GetProperty("registry_url").GetString());
        Assert.Equal("docker_admin", keys.GetProperty("registry_username").GetString());

        // --- TierProfiles with nested ImageSpecs ---
        var tiers = loaded.GetProperty("tierProfiles");
        Assert.Equal(2, tiers.GetArrayLength());
        var freeTier = tiers.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "free");
        Assert.Equal("Free Tier", freeTier.GetProperty("name").GetString());
        var freeSpec = freeTier.GetProperty("imageSpecs").GetProperty("FederationServer");
        Assert.Equal(256, freeSpec.GetProperty("memoryMb").GetInt32());
        Assert.Equal(250, freeSpec.GetProperty("cpuMillicores").GetInt32());
        Assert.Equal(512, freeSpec.GetProperty("diskMb").GetInt32());
        var proTier = tiers.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "pro");
        Assert.Equal(1024, proTier.GetProperty("imageSpecs").GetProperty("FederationServer").GetProperty("memoryMb").GetInt32());

        // --- Container hierarchy ---
        var containers = loaded.GetProperty("containers");
        Assert.Equal(1, containers.GetArrayLength());
        var dns = containers[0];
        Assert.Equal(dnsId.ToString(), dns.GetProperty("id").GetString());
        Assert.Equal("Test DNS", dns.GetProperty("name").GetString());
        Assert.Equal("Dns", dns.GetProperty("kind").GetString());
        Assert.Equal(10.5, dns.GetProperty("x").GetDouble(), 3);
        Assert.Equal(20.3, dns.GetProperty("y").GetDouble(), 3);
        Assert.Equal(900.0, dns.GetProperty("width").GetDouble(), 3);
        Assert.Equal(600.0, dns.GetProperty("height").GetDouble(), 3);
        Assert.Equal("test.net", dns.GetProperty("config").GetProperty("domain").GetString());
        Assert.Equal("linode", dns.GetProperty("config").GetProperty("provider").GetString());

        // DNS port
        var dnsPorts = dns.GetProperty("ports");
        Assert.Equal(1, dnsPorts.GetArrayLength());
        Assert.Equal("records", dnsPorts[0].GetProperty("name").GetString());
        Assert.Equal("Network", dnsPorts[0].GetProperty("type").GetString());
        Assert.Equal("In", dnsPorts[0].GetProperty("direction").GetString());
        Assert.Equal("Left", dnsPorts[0].GetProperty("side").GetString());
        Assert.Equal(0.5, dnsPorts[0].GetProperty("offset").GetDouble(), 3);

        // Caddy child
        var caddy = dns.GetProperty("children")[0];
        Assert.Equal(caddyId.ToString(), caddy.GetProperty("id").GetString());
        Assert.Equal("Caddy", caddy.GetProperty("kind").GetString());
        Assert.Equal("test.net", caddy.GetProperty("config").GetProperty("domain").GetString());

        // Caddy ports
        var caddyPorts = caddy.GetProperty("ports");
        Assert.Equal(2, caddyPorts.GetArrayLength());
        var httpPort = caddyPorts.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "http_in");
        Assert.Equal("Top", httpPort.GetProperty("side").GetString());
        var upstreamPort = caddyPorts.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "upstream");
        Assert.Equal("Out", upstreamPort.GetProperty("direction").GetString());
        Assert.Equal("Bottom", upstreamPort.GetProperty("side").GetString());

        // Caddy images — every ImageKind
        var caddyImages = caddy.GetProperty("images");
        Assert.Equal(5, caddyImages.GetArrayLength());

        // HubServer image
        var hub = caddyImages.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "HubServer");
        Assert.Equal(hubId.ToString(), hub.GetProperty("id").GetString());
        Assert.Equal("hub_server", hub.GetProperty("name").GetString());
        Assert.Equal("ghcr.io/xcord/hub:v0.1.0", hub.GetProperty("dockerImage").GetString());
        Assert.Equal("1-3", hub.GetProperty("config").GetProperty("replicas").GetString());
        Assert.Equal("Shared", hub.GetProperty("scaling").GetString());
        Assert.Equal(2, hub.GetProperty("ports").GetArrayLength());

        // Registry image
        var registry = caddyImages.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "Registry");
        Assert.Equal(registryId.ToString(), registry.GetProperty("id").GetString());
        Assert.Equal("registry:2", registry.GetProperty("dockerImage").GetString());
        Assert.Equal(0, registry.GetProperty("ports").GetArrayLength());

        // LiveKit image — InOut port direction
        var livekit = caddyImages.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "LiveKit");
        Assert.Equal("livekit/livekit-server:v1.8.3", livekit.GetProperty("dockerImage").GetString());
        var livekitRtc = livekit.GetProperty("ports")[0];
        Assert.Equal("InOut", livekitRtc.GetProperty("direction").GetString());

        // PostgreSQL and Redis images
        var pg = caddyImages.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "PostgreSQL");
        Assert.Equal("postgres:17-alpine", pg.GetProperty("dockerImage").GetString());
        var redis = caddyImages.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "Redis");
        Assert.Equal("redis:7-alpine", redis.GetProperty("dockerImage").GetString());

        // DataPool child with Control port and Storage port
        var children = caddy.GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        var dataPool = children.EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "DataPool");
        Assert.Equal(dataPoolId.ToString(), dataPool.GetProperty("id").GetString());
        Assert.Equal("Data Pool", dataPool.GetProperty("name").GetString());
        var dpPorts = dataPool.GetProperty("ports");
        Assert.Equal(2, dpPorts.GetArrayLength());
        var sshPort = dpPorts.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "ssh");
        Assert.Equal("Control", sshPort.GetProperty("type").GetString());
        var dpPublicPort = dpPorts.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "public");
        Assert.Equal("InOut", dpPublicPort.GetProperty("direction").GetString());

        // MinIO image in DataPool — Storage port
        var dpImages = dataPool.GetProperty("images");
        Assert.Equal(1, dpImages.GetArrayLength());
        var minio = dpImages[0];
        Assert.Equal("MinIO", minio.GetProperty("kind").GetString());
        Assert.Equal("minio/minio:RELEASE.2025-02-28T09-55-16Z", minio.GetProperty("dockerImage").GetString());
        Assert.Equal("Storage", minio.GetProperty("ports")[0].GetProperty("type").GetString());

        // ComputePool child with PerTenant FederationServer
        var computePool = children.EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "ComputePool");
        Assert.Equal(computePoolId.ToString(), computePool.GetProperty("id").GetString());
        var cpImages = computePool.GetProperty("images");
        Assert.Equal(1, cpImages.GetArrayLength());
        var fed = cpImages[0];
        Assert.Equal("FederationServer", fed.GetProperty("kind").GetString());
        Assert.Equal("PerTenant", fed.GetProperty("scaling").GetString());
        Assert.Equal("ghcr.io/xcord/fed:v0.1.0", fed.GetProperty("dockerImage").GetString());

        // --- Wires ---
        var wires = loaded.GetProperty("wires");
        Assert.Equal(2, wires.GetArrayLength());
        var wire1 = wires.EnumerateArray().Single(w =>
            w.GetProperty("fromNodeId").GetString() == hubId.ToString());
        Assert.Equal(hubPgPortId.ToString(), wire1.GetProperty("fromPortId").GetString());
        Assert.Equal(pgId.ToString(), wire1.GetProperty("toNodeId").GetString());
        Assert.Equal(pgPortId.ToString(), wire1.GetProperty("toPortId").GetString());

        var wire2 = wires.EnumerateArray().Single(w =>
            w.GetProperty("fromNodeId").GetString() == fedId.ToString());
        Assert.Equal(fedPgPortId.ToString(), wire2.GetProperty("fromPortId").GetString());
        Assert.Equal(minioId.ToString(), wire2.GetProperty("toNodeId").GetString());
        Assert.Equal(minioPortId.ToString(), wire2.GetProperty("toPortId").GetString());
    }
}
