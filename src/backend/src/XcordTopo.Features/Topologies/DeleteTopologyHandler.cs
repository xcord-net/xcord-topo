using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;

namespace XcordTopo.Features.Topologies;

public sealed record DeleteTopologyRequest(Guid Id);

public sealed record DeleteTopologyResponse(bool Deleted);

public sealed class DeleteTopologyHandler(ITopologyStore store)
    : IRequestHandler<DeleteTopologyRequest, Result<DeleteTopologyResponse>>
{
    public async Task<Result<DeleteTopologyResponse>> Handle(DeleteTopologyRequest request, CancellationToken ct)
    {
        var existing = await store.GetAsync(request.Id, ct);
        if (existing is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Id} not found");

        await store.DeleteAsync(request.Id, ct);
        return new DeleteTopologyResponse(true);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapDelete("/api/v1/topologies/{id:guid}", async (
            Guid id, DeleteTopologyHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new DeleteTopologyRequest(id), ct);
        })
        .WithName("DeleteTopology")
        .WithTags("Topologies");
    }
}
