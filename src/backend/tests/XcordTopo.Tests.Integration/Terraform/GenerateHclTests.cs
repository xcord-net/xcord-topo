using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Terraform;

public sealed class GenerateHclTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public GenerateHclTests(TopoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GenerateHcl_ValidTopology_ReturnsFiles()
    {
        // Create a topology with a Host containing a PostgreSQL image — minimal valid for HCL gen
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "HCL Test"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        // Build a minimal topology with one host
        var getResponse = await _client.GetAsync($"/api/v1/topologies/{id}");
        var topology = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            topology.GetRawText(), JsonOptions)!;

        var pgImage = new
        {
            id = Guid.NewGuid(),
            name = "pg-main",
            kind = "PostgreSQL",
            x = 10,
            y = 10,
            width = 120,
            height = 60,
            ports = Array.Empty<object>(),
            config = new Dictionary<string, string>(),
            scaling = "Shared"
        };

        var host = new
        {
            id = Guid.NewGuid(),
            name = "web-1",
            kind = "Host",
            x = 50,
            y = 50,
            width = 400,
            height = 300,
            ports = Array.Empty<object>(),
            images = new[] { pgImage },
            children = Array.Empty<object>(),
            config = new Dictionary<string, string>
            {
                ["provider"] = "linode",
                ["linode_region"] = "us-east"
            }
        };

        dict["containers"] = JsonSerializer.SerializeToElement(new[] { host }, JsonOptions);
        dict["providerConfig"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, string> { ["linode_region"] = "us-east" }, JsonOptions);

        await _client.PutAsJsonAsync($"/api/v1/topologies/{id}", dict, JsonOptions);

        // Generate HCL
        var genResponse = await _client.PostAsync(
            $"/api/v1/topologies/{id}/terraform/generate", null);

        Assert.Equal(HttpStatusCode.OK, genResponse.StatusCode);

        var genBody = await genResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var files = genBody.GetProperty("files");
        Assert.True(files.EnumerateObject().Count() > 0);

        // Summary is now included in the generate response
        var summary = genBody.GetProperty("summary");
        Assert.True(summary.TryGetProperty("resources", out _));
        Assert.True(summary.TryGetProperty("endpoints", out _));
        Assert.True(summary.TryGetProperty("totalMonthly", out _));
    }

    [Fact]
    public async Task GenerateHcl_NotFound_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/v1/topologies/{Guid.NewGuid()}/terraform/generate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadHcl_NoFilesYet_ReturnsEmptyFiles()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new
        {
            name = "Read HCL Empty"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString();

        var response = await _client.GetAsync($"/api/v1/topologies/{id}/terraform/hcl");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var files = body.GetProperty("files");
        Assert.Empty(files.EnumerateObject().ToList());
    }

}
