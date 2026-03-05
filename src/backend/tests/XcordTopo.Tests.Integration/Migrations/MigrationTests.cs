using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Migrations;

public sealed class MigrationTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public MigrationTests(TopoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DiffTopologies_TwoDistinctTopologies_ReturnsDiff()
    {
        var source = await CreateTopology("Diff Source");
        var target = await CreateTopology("Diff Target");

        var response = await _client.PostAsJsonAsync("/api/v1/migrations/diff", new
        {
            sourceTopologyId = source,
            targetTopologyId = target
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        // The diff result should have a structure even if topologies are empty
        Assert.NotEqual(JsonValueKind.Null, body.ValueKind);
    }

    [Fact]
    public async Task DiffTopologies_SameId_Returns400()
    {
        var id = await CreateTopology("Same ID Diff");

        var response = await _client.PostAsJsonAsync("/api/v1/migrations/diff", new
        {
            sourceTopologyId = id,
            targetTopologyId = id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DiffTopologies_SourceNotFound_Returns404()
    {
        var target = await CreateTopology("Diff Target Exists");

        var response = await _client.PostAsJsonAsync("/api/v1/migrations/diff", new
        {
            sourceTopologyId = Guid.NewGuid(),
            targetTopologyId = target
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DiffTopologies_EmptyGuid_Returns400()
    {
        var target = await CreateTopology("Empty Guid Diff");

        var response = await _client.PostAsJsonAsync("/api/v1/migrations/diff", new
        {
            sourceTopologyId = Guid.Empty,
            targetTopologyId = target
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMigrationPlan_EmptyTopologies_Returns201()
    {
        var source = await CreateTopology("Plan Source");
        var target = await CreateTopology("Plan Target");

        // For empty topologies, there should be no required decisions
        var response = await _client.PostAsJsonAsync("/api/v1/migrations/plan", new
        {
            sourceTopologyId = source,
            targetTopologyId = target,
            decisions = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task GetMigrationPlan_AfterCreation_ReturnsPlan()
    {
        var source = await CreateTopology("Get Plan Source");
        var target = await CreateTopology("Get Plan Target");

        var createResponse = await _client.PostAsJsonAsync("/api/v1/migrations/plan", new
        {
            sourceTopologyId = source,
            targetTopologyId = target,
            decisions = Array.Empty<object>()
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var planId = created.GetProperty("id").GetString();

        var response = await _client.GetAsync($"/api/v1/migrations/{planId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(planId, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetMigrationPlan_NotFound_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/migrations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> CreateTopology(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/topologies", new { name });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetString()!;
    }
}
