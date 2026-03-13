using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Topologies;

public sealed record UpdateTopologyRequest(Topology Topology);

public sealed class UpdateTopologyHandler(
    ITopologyStore store,
    MultiProviderHclGenerator hclGenerator,
    IHclFileManager hclFileManager,
    ILogger<UpdateTopologyHandler> logger)
    : IRequestHandler<UpdateTopologyRequest, Result<Topology>>
{
    public async Task<Result<Topology>> Handle(UpdateTopologyRequest request, CancellationToken ct)
    {
        var existing = await store.GetAsync(request.Topology.Id, ct);
        // Upsert - preserve CreatedAt if topology already exists, otherwise set it now
        request.Topology.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        await store.SaveAsync(request.Topology, ct);

        // Best-effort HCL generation - write .tf files alongside the topology JSON
        // so the full state is on disk and doesn't depend on browser storage.
        // Silently skip if generation fails (e.g. incomplete topology).
        try
        {
            var files = hclGenerator.Generate(request.Topology);
            await hclFileManager.WriteFilesAsync(request.Topology.Id, files, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping HCL generation on save for topology {Id}", request.Topology.Id);
        }

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
