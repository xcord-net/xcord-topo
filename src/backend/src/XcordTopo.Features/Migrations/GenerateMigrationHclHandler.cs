using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Migrations;

public sealed record GenerateMigrationHclRequest(Guid Id);

public sealed record MigrationHclResult(
    Dictionary<string, string> TargetHclFiles,
    Dictionary<string, string> MigrationScripts
);

public sealed class GenerateMigrationHclHandler(
    IMigrationStore migrationStore,
    ITopologyStore topologyStore,
    ProviderRegistry providerRegistry)
    : IRequestHandler<GenerateMigrationHclRequest, Result<MigrationHclResult>>, IValidatable<GenerateMigrationHclRequest>
{
    public Error? Validate(GenerateMigrationHclRequest request)
    {
        if (request.Id == Guid.Empty)
            return Error.Validation("VALIDATION_ERROR", "Migration plan ID is required");
        return null;
    }

    public async Task<Result<MigrationHclResult>> Handle(GenerateMigrationHclRequest request, CancellationToken ct)
    {
        var plan = await migrationStore.GetAsync(request.Id, ct);
        if (plan is null)
            return Error.NotFound("PLAN_NOT_FOUND", $"Migration plan {request.Id} not found");

        var target = await topologyStore.GetAsync(plan.TargetTopologyId, ct);
        if (target is null)
            return Error.NotFound("TARGET_NOT_FOUND", $"Target topology {plan.TargetTopologyId} not found");

        // Generate target topology Terraform
        var provider = providerRegistry.Get(target.Provider);
        if (provider is null)
            return Error.BadRequest("PROVIDER_NOT_FOUND", $"Provider '{target.Provider}' not found");

        var targetHcl = provider.GenerateHcl(target);

        // Generate migration scripts from plan phases
        var migrationScripts = GenerateMigrationScripts(plan);

        return new MigrationHclResult(targetHcl, migrationScripts);
    }

    private static Dictionary<string, string> GenerateMigrationScripts(MigrationPlan plan)
    {
        var scripts = new Dictionary<string, string>();

        foreach (var phase in plan.Phases)
        {
            var scriptContent = $"#!/bin/bash\n# Phase: {phase.Name}\n# {phase.Description}\n\nset -euo pipefail\n\n";

            foreach (var step in phase.Steps)
            {
                scriptContent += $"echo \"=== Step {step.Order}: {step.Description} ===\"\n";
                if (step.CausesDowntime)
                    scriptContent += "echo \"WARNING: This step causes downtime\"\n";
                if (!string.IsNullOrEmpty(step.Script))
                    scriptContent += $"\n{step.Script}\n";
                scriptContent += "\n";
            }

            if (phase.Steps.Count > 0)
            {
                scripts[$"migrate-{phase.Type.ToString().ToLowerInvariant()}.sh"] = scriptContent;
            }
        }

        return scripts;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/migrations/{id:guid}/hcl", async (
            Guid id,
            GenerateMigrationHclHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GenerateMigrationHclRequest(id), ct);
        })
        .WithName("GenerateMigrationHcl")
        .WithTags("Migrations");
    }
}
