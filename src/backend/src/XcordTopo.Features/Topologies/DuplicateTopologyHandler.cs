using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record DuplicateTopologyRequest(Guid Id);

public sealed class DuplicateTopologyHandler(ITopologyStore store)
    : IRequestHandler<DuplicateTopologyRequest, Result<Topology>>
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Result<Topology>> Handle(DuplicateTopologyRequest request, CancellationToken ct)
    {
        var existing = await store.GetAsync(request.Id, ct);
        if (existing is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.Id} not found");

        // Deep clone via JSON round-trip
        var json = JsonSerializer.Serialize(existing, CloneOptions);
        var clone = JsonSerializer.Deserialize<Topology>(json, CloneOptions)!;
        clone.Id = Guid.NewGuid();
        clone.Name = $"{existing.Name} (Copy)";
        clone.CreatedAt = DateTimeOffset.UtcNow;
        clone.UpdatedAt = DateTimeOffset.UtcNow;

        await store.SaveAsync(clone, ct);
        return clone;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{id:guid}/duplicate", async (
            Guid id, DuplicateTopologyHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new DuplicateTopologyRequest(id), ct,
                success => Results.Created($"/api/v1/topologies/{success.Id}", success));
        })
        .WithName("DuplicateTopology")
        .WithTags("Topologies");
    }
}
