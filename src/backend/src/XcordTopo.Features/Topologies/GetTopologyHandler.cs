using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record GetTopologyRequest(Guid Id);

public sealed class GetTopologyHandler(ITopologyStore store)
    : IRequestHandler<GetTopologyRequest, Result<Topology>>
{
    public async Task<Result<Topology>> Handle(GetTopologyRequest request, CancellationToken ct)
    {
        var topology = await store.GetAsync(request.Id, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Id} not found");

        return topology;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies/{id:guid}", async (
            Guid id, GetTopologyHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetTopologyRequest(id), ct);
        })
        .WithName("GetTopology")
        .WithTags("Topologies");
    }
}
