using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Plugins;
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

public sealed class ExecuteTerraformHandler(
    ITerraformExecutor executor,
    ITopologyStore topologyStore,
    ICredentialStore credentialStore,
    RegistryClient registryClient,
    ImagePluginRegistry pluginRegistry)
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

        // Service keys are stored separately from cloud provider credentials
        if (!providerKeys.Contains("service-keys", StringComparer.OrdinalIgnoreCase))
            providerKeys.Add("service-keys");

        // Ensure credential files have migrated keys before Terraform reads them
        foreach (var key in providerKeys)
            await credentialStore.GetRawVariablesAsync(request.TopologyId, key, ct);

        var command = Enum.Parse<TerraformCommand>(request.Command, ignoreCase: true);
        Dictionary<string, string>? extraVars = null;

        // Always pass image versions when available - Terraform variables exist in all phases
        if (request.ImageVersions != null)
        {
            extraVars = new();
            foreach (var (kind, version) in request.ImageVersions)
            {
                if (ImageKindToVersionVar.TryGetValue(kind, out var varName))
                    extraVars[varName] = version;
            }
        }

        if (request.DeployApps)
        {
            extraVars ??= new();
            extraVars["deploy_apps"] = "true";

            // Verify all private-registry images exist before deploying containers
            if (request.ImageVersions is { Count: > 0 })
            {
                var buildSpecs = new List<ImageBuildSpec>();
                foreach (var (kind, version) in request.ImageVersions)
                {
                    var plugin = pluginRegistry.Get(kind);
                    var docker = plugin?.GetDockerBehavior();
                    if (docker is { RequiresPrivateRegistry: true, RegistryName: not null })
                        buildSpecs.Add(new ImageBuildSpec(docker.GitRepoUrl, version, docker.RegistryName));
                }

                if (buildSpecs.Count > 0)
                {
                    var variables = await credentialStore.GetRawVariablesAsync(request.TopologyId, "service-keys", ct);
                    if (variables.TryGetValue("registry_url", out var registryUrl) && !string.IsNullOrWhiteSpace(registryUrl))
                    {
                        var missing = await registryClient.FindMissingImagesAsync(registryUrl, buildSpecs, ct);
                        if (missing.Count > 0)
                        {
                            var names = string.Join(", ", missing.Select(m => $"{m.RegistryName}:{m.GitRef}"));
                            return Error.Validation("IMAGES_NOT_IN_REGISTRY",
                                $"The following images are missing from the registry: {names}. Push images before deploying.");
                        }
                    }
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
