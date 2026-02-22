using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed class GetActiveDeploymentsHandler(
    IHclFileManager hclFileManager,
    ITopologyStore topologyStore)
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/deploy/active", async (
            GetActiveDeploymentsHandler handler,
            CancellationToken ct) =>
        {
            var deployments = await handler.GetActiveAsync(ct);
            return Results.Ok(deployments);
        })
        .WithName("GetActiveDeployments")
        .WithTags("Deploy");
    }

    private async Task<List<DeployedTopology>> GetActiveAsync(CancellationToken ct)
    {
        var topologies = await topologyStore.ListAsync(ct);
        var result = new List<DeployedTopology>();

        foreach (var topology in topologies)
        {
            var stateJson = await hclFileManager.ReadStateAsync(topology.Id, ct);
            if (string.IsNullOrWhiteSpace(stateJson))
                continue;

            var resourceCount = CountResources(stateJson);
            if (resourceCount == 0) continue;

            result.Add(new DeployedTopology
            {
                TopologyId = topology.Id,
                TopologyName = topology.Name,
                HasState = true,
                ResourceCount = resourceCount
            });
        }

        return result;
    }

    private static int CountResources(string stateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            if (doc.RootElement.TryGetProperty("resources", out var resources) &&
                resources.ValueKind == JsonValueKind.Array)
            {
                return resources.GetArrayLength();
            }
        }
        catch { }
        return 0;
    }
}
