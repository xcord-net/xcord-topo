using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record UpdateTopologyRequest(Topology Topology);

public sealed class UpdateTopologyHandler(ITopologyStore store)
    : IRequestHandler<UpdateTopologyRequest, Result<Topology>>
{
    public async Task<Result<Topology>> Handle(UpdateTopologyRequest request, CancellationToken ct)
    {
        var existing = await store.GetAsync(request.Topology.Id, ct);
        if (existing is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Topology.Id} not found");

        request.Topology.CreatedAt = existing.CreatedAt;
        await store.SaveAsync(request.Topology, ct);
        return request.Topology;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPut("/api/v1/topologies/{id:guid}", async (
            Guid id,
            [FromBody] Topology topology,
            UpdateTopologyHandler handler,
            CancellationToken ct) =>
        {
            topology.Id = id;
            return await handler.ExecuteAsync(new UpdateTopologyRequest(topology), ct);
        })
        .WithName("UpdateTopology")
        .WithTags("Topologies");
    }
}
