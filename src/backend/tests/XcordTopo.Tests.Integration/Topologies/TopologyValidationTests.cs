using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Topologies;

public sealed class TopologyValidationTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public TopologyValidationTests(TopoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidateTopology_EmptyTopology_ReturnsValidationResult()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Empty Validate"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        var response = await _client.PostAsync($"/api/v1/topologies/{id}/validate", null);

        // Validate always returns 200 - pass/fail is in the body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.TryGetProperty("isValid", out _));
        Assert.True(body.TryGetProperty("canDeploy", out _));
        Assert.True(body.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
    }

    [Fact]
    public async Task ValidateTopology_NotFound_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/v1/topologies/{Guid.NewGuid()}/validate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ValidateTopology_WithContainers_ReturnsItems()
    {
        // Create topology, then add a container via update
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Validation Items Test"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        var topology = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            topology.GetRawText(), JsonOptions)!;

        // Add a Host container with no name (should trigger validation warning/error)
        var container = new
        {
            id = Guid.NewGuid(),
            name = "",
            kind = "Host",
            x = 0,
            y = 0,
            width = 300,
            height = 200,
            ports = Array.Empty<object>(),
            images = Array.Empty<object>(),
            children = Array.Empty<object>(),
            config = new Dictionary<string, string>()
        };

        dict["containers"] = JsonSerializer.SerializeToElement(new[] { container }, JsonOptions);
        await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", dict, JsonOptions);

        var validateResponse = await _client.PostAsync($"/api/v1/topologies/{id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);

        var body = await validateResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = body.GetProperty("items");
        // An unnamed Host should produce at least one validation item
        Assert.True(items.GetArrayLength() >= 1);
    }
}
