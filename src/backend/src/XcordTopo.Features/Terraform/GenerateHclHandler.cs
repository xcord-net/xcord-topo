using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Infrastructure.Validation;

namespace XcordTopo.Features.Terraform;

public sealed record GenerateHclRequest(
    Guid TopologyId, List<TopologyHelpers.PoolSelection>? PoolSelections = null);

public sealed record GenerateHclBody(List<TopologyHelpers.PoolSelection>? PoolSelections);

public sealed record GenerateHclResponse(Dictionary<string, string> Files);

public sealed class GenerateHclHandler(
    ITopologyStore store,
    MultiProviderHclGenerator hclGenerator,
    IHclFileManager hclFileManager,
    ITopologyValidator validator)
    : IRequestHandler<GenerateHclRequest, Result<GenerateHclResponse>>
{
    public async Task<Result<GenerateHclResponse>> Handle(GenerateHclRequest request, CancellationToken ct)
    {
        var topology = await store.GetAsync(request.TopologyId, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.TopologyId} not found");

        var validation = validator.ValidateFull(topology);
        if (!validation.CanDeploy)
        {
            var summary = string.Join("; ", validation.Errors.Select(e => e.Message));
            return Error.Validation("VALIDATION_FAILED",
                $"Topology has {validation.Errors.Count} validation error(s): {summary}");
        }

        var files = hclGenerator.Generate(topology, request.PoolSelections);
        await hclFileManager.WriteFilesAsync(request.TopologyId, files, ct);

        return new GenerateHclResponse(files);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/generate", async (
            Guid topologyId, HttpRequest httpRequest, GenerateHclHandler handler, CancellationToken ct) =>
        {
            List<TopologyHelpers.PoolSelection>? selections = null;
            if (httpRequest.ContentLength is > 0)
            {
                var body = await httpRequest.ReadFromJsonAsync<GenerateHclBody>(ct);
                selections = body?.PoolSelections;
            }
            return await handler.ExecuteAsync(
                new GenerateHclRequest(topologyId, selections), ct);
        })
        .WithName("GenerateHcl")
        .WithTags("Terraform");
    }
}
