using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Providers;

public sealed class ProviderTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public ProviderTests(TopoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListProviders_ReturnsLinodeAndAws()
    {
        var response = await _client.GetAsync("/api/v1/providers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var providers = body.GetProperty("providers");
        Assert.True(providers.GetArrayLength() >= 2);

        var keys = providers.EnumerateArray()
            .Select(p => p.GetProperty("key").GetString())
            .ToList();
        Assert.Contains("linode", keys);
        Assert.Contains("aws", keys);
    }

    [Fact]
    public async Task GetProviderRegions_Linode_ReturnsRegions()
    {
        var response = await _client.GetAsync("/api/v1/providers/linode/regions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var regions = body.GetProperty("regions");
        Assert.True(regions.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetProviderRegions_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/providers/nonexistent/regions");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProviderPlans_Linode_ReturnsPlans()
    {
        var response = await _client.GetAsync("/api/v1/providers/linode/plans");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var plans = body.GetProperty("plans");
        Assert.True(plans.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetCredentialSchema_Linode_ReturnsSchema()
    {
        var response = await _client.GetAsync("/api/v1/providers/linode/credential-schema");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var fields = body.GetProperty("fields");
        Assert.True(fields.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetCredentialSchema_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/providers/nonexistent/credential-schema");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
