using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordTopo.Tests.Integration.Terraform;

public sealed class StreamTerraformEndpointTests : IClassFixture<TopoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;
    private readonly TopoWebApplicationFactory _factory;

    public StreamTerraformEndpointTests(TopoWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StreamTerraform_NoActiveExecution_ReturnsSseErrorAndDone()
    {
        var topologyId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/topologies/{topologyId}/terraform/stream");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        // Should contain error message about no active execution
        Assert.Contains("No active execution", body);
        Assert.Contains("isError", body);
        // Should contain the done sentinel
        Assert.Contains("[DONE]", body);
    }

    [Fact]
    public async Task StreamImagePush_NoActiveExecution_ReturnsSseErrorAndDone()
    {
        var topologyId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/topologies/{topologyId}/images/stream");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("No active image push", body);
        Assert.Contains("isError", body);
        Assert.Contains("[DONE]", body);
    }

    [Fact]
    public async Task ExecuteTerraform_ThenStreamOutput_FullPipelineTest()
    {
        // Create a topology first
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new { name = "Stream Test" });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString()!;

        // Trigger terraform execution (will fail because terraform isn't installed —
        // that's the exact scenario we're testing)
        var execResponse = await _client.PostAsync(
            $"/api/v1/topologies/{id}/terraform/init", null);

        // Should get 200 with "started" status
        Assert.Equal(HttpStatusCode.OK, execResponse.StatusCode);

        // Give the background task time to fail
        await Task.Delay(500);

        // Now connect to the SSE stream — the reader should still be available
        // even though the process already exited (race condition fix)
        var streamRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/topologies/{id}/terraform/stream");
        streamRequest.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var streamResponse = await _client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);

        var body = await streamResponse.Content.ReadAsStringAsync();

        // Should contain the error from the failed process (terraform not installed)
        Assert.Contains("data:", body);
        Assert.Contains("[DONE]", body);

        // The error output should be present (not empty like the original bug)
        var dataLines = body.Split('\n')
            .Where(l => l.StartsWith("data:") && !l.Contains("[DONE]"))
            .ToList();

        Assert.NotEmpty(dataLines);
    }

    [Fact]
    public async Task StreamTerraform_SseFormat_HasCorrectHeaders()
    {
        var topologyId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/topologies/{topologyId}/terraform/stream");

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task ExecuteTerraform_InvalidCommand_Returns400()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/topologies", new { name = "Invalid Cmd" });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.PostAsync(
            $"/api/v1/topologies/{id}/terraform/bogus", null);

        // Should reject invalid commands
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
