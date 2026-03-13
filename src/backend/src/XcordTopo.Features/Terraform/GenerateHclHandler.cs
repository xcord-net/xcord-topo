using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Infrastructure.Validation;

namespace XcordTopo.Features.Terraform;

public sealed record GenerateHclRequest(
    Guid TopologyId,
    List<TopologyHelpers.PoolSelection>? PoolSelections = null,
    List<TopologyHelpers.InfraSelection>? InfraSelections = null);

public sealed record GenerateHclBody(
    List<TopologyHelpers.PoolSelection>? PoolSelections,
    List<TopologyHelpers.InfraSelection>? InfraSelections);

public sealed record GenerateHclResponse(
    Dictionary<string, string> Files,
    ResourceSummary Summary);

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
            var errorSummary = string.Join("; ", validation.Errors.Select(e => e.Message));
            return Error.Validation("VALIDATION_FAILED",
                $"Topology has {validation.Errors.Count} validation error(s): {errorSummary}");
        }

        var files = hclGenerator.Generate(topology, request.PoolSelections, request.InfraSelections);
        await hclFileManager.WriteFilesAsync(request.TopologyId, files, ct);

        var summary = hclGenerator.BuildResourceSummary(topology, request.PoolSelections, request.InfraSelections);
        return new GenerateHclResponse(files, summary);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/generate", async (
            Guid topologyId, HttpRequest httpRequest, GenerateHclHandler handler, CancellationToken ct) =>
        {
            List<TopologyHelpers.PoolSelection>? poolSelections = null;
            List<TopologyHelpers.InfraSelection>? infraSelections = null;
            if (httpRequest.ContentLength is > 0)
            {
                var body = await httpRequest.ReadFromJsonAsync<GenerateHclBody>(ct);
                poolSelections = body?.PoolSelections;
                infraSelections = body?.InfraSelections;
            }
            return await handler.ExecuteAsync(
                new GenerateHclRequest(topologyId, poolSelections, infraSelections), ct);
        })
        .WithName("GenerateHcl")
        .WithTags("Terraform");
    }
}
