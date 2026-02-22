using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Terraform;

public static class StreamTerraformHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies/{topologyId:guid}/terraform/stream", async (
            Guid topologyId,
            ITerraformExecutor executor,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            var reader = executor.GetOutputStream(topologyId);
            if (reader is null)
            {
                await httpContext.Response.WriteAsync($"data: {{\"text\":\"No active execution\",\"isError\":true}}\n\n", ct);
                await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
                return;
            }

            try
            {
                await foreach (var line in reader.ReadAllAsync(ct))
                {
                    var json = JsonSerializer.Serialize(line, JsonOptions);
                    await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }

            await httpContext.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        })
        .WithName("StreamTerraform")
        .WithTags("Terraform");
    }
}
