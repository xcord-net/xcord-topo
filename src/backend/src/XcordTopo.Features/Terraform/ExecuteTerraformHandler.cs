using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record ExecuteTerraformRequest(Guid TopologyId, string Command);

public sealed record ExecuteTerraformResponse(string Status);

public sealed class ExecuteTerraformHandler(ITerraformExecutor executor, ITopologyStore topologyStore)
    : IRequestHandler<ExecuteTerraformRequest, Result<ExecuteTerraformResponse>>, IValidatable<ExecuteTerraformRequest>
{
    private static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "init", "plan", "apply", "destroy"
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

        var command = Enum.Parse<TerraformCommand>(request.Command, ignoreCase: true);
        await executor.ExecuteAsync(request.TopologyId, command, providerKeys, ct);

        return new ExecuteTerraformResponse("started");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/{command}", async (
            Guid topologyId, string command, ExecuteTerraformHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ExecuteTerraformRequest(topologyId, command), ct);
        })
        .WithName("ExecuteTerraform")
        .WithTags("Terraform");
    }
}
