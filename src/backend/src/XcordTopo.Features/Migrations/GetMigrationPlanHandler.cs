using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Models;

namespace XcordTopo.Features.Migrations;

public sealed record GetMigrationPlanRequest(Guid Id);

public sealed class GetMigrationPlanHandler(IMigrationStore store)
    : IRequestHandler<GetMigrationPlanRequest, Result<MigrationPlan>>, IValidatable<GetMigrationPlanRequest>
{
    public Error? Validate(GetMigrationPlanRequest request)
    {
        if (request.Id == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Migration plan ID is required");
        return null;
    }

    public async Task<Result<MigrationPlan>> Handle(GetMigrationPlanRequest request, CancellationToken ct)
    {
        var plan = await store.GetAsync(request.Id, ct);
        if (plan is null)
            return Error.NotFound("PLAN_NOT_FOUND", $"Migration plan {request.Id} not found");
        return plan;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/migrations/{id:guid}", async (
            Guid id,
            GetMigrationPlanHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetMigrationPlanRequest(id), ct);
        })
        .WithName("GetMigrationPlan")
        .WithTags("Migrations");
    }
}
