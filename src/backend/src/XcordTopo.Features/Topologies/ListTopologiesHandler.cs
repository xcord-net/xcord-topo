using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record ListTopologiesRequest;

public sealed record TopologySummary(
    Guid Id,
    string Name,
    string? Description,
    string Provider,
    int ContainerCount,
    int WireCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record ListTopologiesResponse(List<TopologySummary> Topologies);

public sealed class ListTopologiesHandler(ITopologyStore store)
    : IRequestHandler<ListTopologiesRequest, Result<ListTopologiesResponse>>
{
    public async Task<Result<ListTopologiesResponse>> Handle(ListTopologiesRequest request, CancellationToken ct)
    {
        var topologies = await store.ListAsync(ct);
        var summaries = topologies.Select(t => new TopologySummary(
            t.Id, t.Name, t.Description, t.Provider,
            t.Containers.Count, t.Wires.Count,
            t.CreatedAt, t.UpdatedAt
        )).ToList();

        return new ListTopologiesResponse(summaries);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies", async (
            ListTopologiesHandler handler, CancellationToken ct) =>
        {
            var request = new ListTopologiesRequest();
            return await handler.ExecuteAsync(request, ct);
        })
        .WithName("ListTopologies")
        .WithTags("Topologies");
    }
}
