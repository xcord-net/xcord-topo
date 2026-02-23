using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Terraform;

public sealed record GenerateHclRequest(Guid TopologyId);

public sealed record GenerateHclResponse(Dictionary<string, string> Files);

public sealed class GenerateHclHandler(
    ITopologyStore store,
    ProviderRegistry registry,
    IHclFileManager hclFileManager)
    : IRequestHandler<GenerateHclRequest, Result<GenerateHclResponse>>
{
    public async Task<Result<GenerateHclResponse>> Handle(GenerateHclRequest request, CancellationToken ct)
    {
        var topology = await store.GetAsync(request.TopologyId, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.TopologyId} not found");

        var provider = registry.Get(topology.Provider);
        if (provider is null)
            return Error.BadRequest("UNKNOWN_PROVIDER", $"Provider '{topology.Provider}' is not registered");

        var files = provider.GenerateHcl(topology);
        await hclFileManager.WriteFilesAsync(request.TopologyId, files, ct);

        return new GenerateHclResponse(files);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/generate", async (
            Guid topologyId, GenerateHclHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GenerateHclRequest(topologyId), ct);
        })
        .WithName("GenerateHcl")
        .WithTags("Terraform");
    }
}
