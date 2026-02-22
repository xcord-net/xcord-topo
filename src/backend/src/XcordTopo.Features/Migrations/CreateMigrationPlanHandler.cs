using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Migrations;

public sealed record CreateMigrationPlanRequest(
    Guid SourceTopologyId,
    Guid TargetTopologyId,
    List<MigrationDecision> Decisions
);

public sealed class CreateMigrationPlanHandler(
    ITopologyStore topologyStore,
    IMigrationStore migrationStore,
    TopologyMatcher matcher,
    MigrationPlanGenerator generator)
    : IRequestHandler<CreateMigrationPlanRequest, Result<MigrationPlan>>, IValidatable<CreateMigrationPlanRequest>
{
    public Error? Validate(CreateMigrationPlanRequest request)
    {
        if (request.SourceTopologyId == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Source topology ID is required");
        if (request.TargetTopologyId == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Target topology ID is required");
        if (request.SourceTopologyId == request.TargetTopologyId)
            return Error.Validation("VALIDATION_ERROR", "Source and target topologies must be different");
        return null;
    }

    public async Task<Result<MigrationPlan>> Handle(CreateMigrationPlanRequest request, CancellationToken ct)
    {
        var source = await topologyStore.GetAsync(request.SourceTopologyId, ct);
        if (source is null)
            return Error.NotFound("SOURCE_NOT_FOUND", $"Source topology {request.SourceTopologyId} not found");

        var target = await topologyStore.GetAsync(request.TargetTopologyId, ct);
        if (target is null)
            return Error.NotFound("TARGET_NOT_FOUND", $"Target topology {request.TargetTopologyId} not found");

        var diff = matcher.Match(source, target);

        // Validate that all required decisions have been answered
        var requiredDecisionIds = diff.Decisions.Where(d => d.Required).Select(d => d.Id).ToHashSet();
        var answeredIds = request.Decisions
            .Where(d => !string.IsNullOrEmpty(d.SelectedOptionKey))
            .Select(d => d.Id)
            .ToHashSet();
        var unanswered = requiredDecisionIds.Except(answeredIds).ToList();
        if (unanswered.Count > 0)
            return Error.Validation("DECISIONS_INCOMPLETE",
                $"Required decisions not answered: {string.Join(", ", unanswered)}");

        var plan = generator.Generate(source, target, diff, request.Decisions);

        await migrationStore.SaveAsync(plan, ct);
        return plan;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/migrations/plan", async (
            [FromBody] CreateMigrationPlanRequest request,
            CreateMigrationPlanHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct,
                success => Results.Created($"/api/v1/migrations/{success.Id}", success));
        })
        .WithName("CreateMigrationPlan")
        .WithTags("Migrations");
    }
}
