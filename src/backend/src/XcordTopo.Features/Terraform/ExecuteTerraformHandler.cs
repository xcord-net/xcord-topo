using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record ExecuteTerraformRequest(
    Guid TopologyId,
    string Command,
    bool DeployApps = false,
    Dictionary<string, string>? ImageVersions = null);

public sealed record ExecuteTerraformResponse(string Status);

public sealed record ExecuteTerraformBody(
    bool? DeployApps = null,
    Dictionary<string, string>? ImageVersions = null);

public sealed class ExecuteTerraformHandler(ITerraformExecutor executor, ITopologyStore topologyStore, ICredentialStore credentialStore)
    : IRequestHandler<ExecuteTerraformRequest, Result<ExecuteTerraformResponse>>, IValidatable<ExecuteTerraformRequest>
{
    private static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "init", "plan", "apply", "destroy"
    };

    private static readonly Dictionary<string, string> ImageKindToVersionVar = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HubServer"] = "hub_version",
        ["FederationServer"] = "fed_version",
    };

    public Error? Validate(ExecuteTerraformRequest request)
    {
        if (!ValidCommands.Contains(request.Command))
            return Error.Validation("INVALID_COMMAND", $"Invalid terraform command: {request.Command}. Must be one of: init, plan, apply, destroy");
        return null;
    }

    public async Task<Result<ExecuteTerraformResponse>> Handle(ExecuteTerraformRequest request, CancellationToken ct)
    {
        if (executor.IsRunning(request.TopologyId))
            return Error.Conflict("ALREADY_RUNNING", "Terraform is already running for this topology");

        // Look up topology to determine all active provider keys
        var topology = await topologyStore.GetAsync(request.TopologyId, ct);
        var providerKeys = topology != null
            ? TopologyHelpers.CollectActiveProviderKeys(topology)
            : new List<string> { "linode" };

        // Ensure credential files have migrated keys before Terraform reads them
        foreach (var key in providerKeys)
            await credentialStore.GetRawVariablesAsync(request.TopologyId, key, ct);

        var command = Enum.Parse<TerraformCommand>(request.Command, ignoreCase: true);
        Dictionary<string, string>? extraVars = null;

        if (request.DeployApps)
        {
            extraVars = new() { ["deploy_apps"] = "true" };

            // Map image kind names to Terraform version variable names
            if (request.ImageVersions != null)
            {
                foreach (var (kind, version) in request.ImageVersions)
                {
                    if (ImageKindToVersionVar.TryGetValue(kind, out var varName))
                        extraVars[varName] = version;
                }
            }
        }

        await executor.ExecuteAsync(request.TopologyId, command, providerKeys, extraVars, ct);

        return new ExecuteTerraformResponse("started");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/{command}", async (
            Guid topologyId, string command, ExecuteTerraformBody? body, ExecuteTerraformHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(
                new ExecuteTerraformRequest(topologyId, command, body?.DeployApps ?? false, body?.ImageVersions), ct);
        })
        .WithName("ExecuteTerraform")
        .WithTags("Terraform");
    }
}
