using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record CreateTopologyRequest(
    string Name,
    string? Description = null,
    string Provider = "linode"
);

public sealed class CreateTopologyHandler(ITopologyStore store)
    : IRequestHandler<CreateTopologyRequest, Result<Topology>>, IValidatable<CreateTopologyRequest>
{
    public Error? Validate(CreateTopologyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("VALIDATION_ERROR", "Topology name is required");
        if (request.Name.Length > 100)
            return Error.Validation("VALIDATION_ERROR", "Topology name must not exceed 100 characters");
        return null;
    }

    public async Task<Result<Topology>> Handle(CreateTopologyRequest request, CancellationToken ct)
    {
        var topology = new Topology
        {
            Name = request.Name,
            Description = request.Description,
            Provider = request.Provider
        };

        await store.SaveAsync(topology, ct);
        return topology;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies", async (
            [FromBody] CreateTopologyRequest request,
            CreateTopologyHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct,
                success => Results.Created($"/api/v1/topologies/{success.Id}", success));
        })
        .WithName("CreateTopology")
        .WithTags("Topologies");
    }
}
