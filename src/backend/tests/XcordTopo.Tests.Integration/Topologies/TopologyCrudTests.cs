using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Topologies;

public sealed class TopologyCrudTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public TopologyCrudTests(TopoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTopology_Returns201WithLocationHeader()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Test Topology",
            description = "Integration test"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Test Topology", body.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty.ToString(), body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task CreateTopology_BlankName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListTopologies_ReturnsCreatedTopology()
    {
        await _client.PostAsJsonAsync("/api/v1/topologies", new { name = "Listed Topo" });

        var response = await _client.GetAsync("/api/v1/topologies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var topologies = body.GetProperty("topologies");
        Assert.True(topologies.GetArrayLength() >= 1);

        var found = topologies.EnumerateArray()
            .Any(t => t.GetProperty("name").GetString() == "Listed Topo");
        Assert.True(found);
    }

    [Fact]
    public async Task GetTopology_ReturnsFullObject()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Get Test"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        var response = await _client.GetAsync($"/api/v1/topologies/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Get Test", body.GetProperty("name").GetString());
        Assert.Equal(id, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetTopology_NotFound_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/topologies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTopology_PersistsChanges()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Before Update"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        // Get the full topology to use as update body
        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        var topology = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // Modify the name by rebuilding the object
        var updatedJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            topology.GetRawText(), JsonOptions)!;
        updatedJson["name"] = JsonSerializer.SerializeToElement("After Update", JsonOptions);

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/topologies/{id}",
            updatedJson,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Verify persisted
        var verifyResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        var verified = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("After Update", verified.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateTopology_OverridesBodyIdWithRouteId()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Id Override Test"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var realId = created.GetProperty("id").GetString();

        var getResponse = await _client.GetAsync($"/api/v1/topologies/{realId}");
        var topology = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // Send update with a different id in the body
        var updatedJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            topology.GetRawText(), JsonOptions)!;
        updatedJson["id"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString(), JsonOptions);
        updatedJson["name"] = JsonSerializer.SerializeToElement("Updated Name", JsonOptions);

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/topologies/{realId}",
            updatedJson,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // The stored topology should have the route id, not the body id
        var verifyResponse = await _client.GetAsync($"/api/v1/topologies/{realId}");
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verified = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(realId, verified.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DeleteTopology_RemovesFromStore()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Delete Me"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        var deleteResponse = await _client.DeleteAsync($"/api/v1/topologies/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DuplicateTopology_CreatesNewCopyWithSuffix()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Original"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var originalId = created.GetProperty("id").GetString();

        var dupResponse = await _client.PostAsync(
            $"/api/v1/topologies/{originalId}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, dupResponse.StatusCode);
        Assert.NotNull(dupResponse.Headers.Location);

        var duplicate = await dupResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Original (Copy)", duplicate.GetProperty("name").GetString());
        Assert.NotEqual(originalId, duplicate.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DuplicateTopology_NotFound_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/v1/topologies/{Guid.NewGuid()}/duplicate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EnumsSerializeAsStrings()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Enum Test",
            provider = "linode"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        // Get the raw JSON to verify enum serialization
        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        var raw = await getResponse.Content.ReadAsStringAsync();

        // The provider field should be a string, not a number
        Assert.Contains("\"linode\"", raw);
    }
}
