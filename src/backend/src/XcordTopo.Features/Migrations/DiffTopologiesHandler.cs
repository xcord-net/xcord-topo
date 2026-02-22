using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Migrations;

public sealed record DiffTopologiesRequest(
    Guid SourceTopologyId,
    Guid TargetTopologyId
);

public sealed class DiffTopologiesHandler(ITopologyStore store, TopologyMatcher matcher)
    : IRequestHandler<DiffTopologiesRequest, Result<MigrationDiffResult>>, IValidatable<DiffTopologiesRequest>
{
    public Error? Validate(DiffTopologiesRequest request)
    {
        if (request.SourceTopologyId == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Source topology ID is required");
        if (request.TargetTopologyId == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Target topology ID is required");
        if (request.SourceTopologyId == request.TargetTopologyId)
            return Error.Validation("VALIDATION_ERROR", "Source and target topologies must be different");
        return null;
    }

    public async Task<Result<MigrationDiffResult>> Handle(DiffTopologiesRequest request, CancellationToken ct)
    {
        var source = await store.GetAsync(request.SourceTopologyId, ct);
        if (source is null)
            return Error.NotFound("SOURCE_NOT_FOUND", $"Source topology {request.SourceTopologyId} not found");

        var target = await store.GetAsync(request.TargetTopologyId, ct);
        if (target is null)
            return Error.NotFound("TARGET_NOT_FOUND", $"Target topology {request.TargetTopologyId} not found");

        var result = matcher.Match(source, target);
        return result;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/migrations/diff", async (
            [FromBody] DiffTopologiesRequest request,
            DiffTopologiesHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct);
        })
        .WithName("DiffTopologies")
        .WithTags("Migrations");
    }
}
