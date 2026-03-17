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

        // Map image versions to Terraform variable names dynamically via plugin registry
        if (request.ImageVersions != null)
        {
            extraVars = new();
            foreach (var (kind, version) in request.ImageVersions)
            {
                var plugin = pluginRegistry.Get(kind);
                var versionVar = plugin?.GetDockerBehavior()?.VersionVariableName;
                if (versionVar != null)
                    extraVars[versionVar] = version;
            }
        }

        if (request.DeployApps)
        {
            extraVars ??= new();
            extraVars["deploy_apps"] = "true";

            // Verify ALL private-registry images in the topology exist before deploying
            if (topology != null)
            {
                var buildSpecs = new List<ImageBuildSpec>();
                CollectPrivateRegistrySpecs(topology.Containers, request.ImageVersions, buildSpecs);
                var uniqueSpecs = buildSpecs.DistinctBy(s => s.RegistryName).ToList();

                if (uniqueSpecs.Count > 0)
                {
                    var variables = await credentialStore.GetRawVariablesAsync(request.TopologyId, "service-keys", ct);
                    if (variables.TryGetValue("registry_url", out var registryUrl) && !string.IsNullOrWhiteSpace(registryUrl))
                    {
                        var missing = await registryClient.FindMissingImagesAsync(registryUrl, uniqueSpecs, ct);
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

    /// <summary>
    /// Walks all containers in the topology and collects ImageBuildSpec for each private-registry image.
    /// Version is resolved from explicit ImageVersions or defaults to "0.1" (matching Terraform variable default).
    /// </summary>
    private void CollectPrivateRegistrySpecs(
        List<Container> containers,
        Dictionary<string, string>? imageVersions,
        List<ImageBuildSpec> specs)
    {
        foreach (var c in containers)
        {
            foreach (var img in c.Images)
            {
                var typeId = img.ResolveTypeId();
                var plugin = pluginRegistry.Get(typeId);
                var docker = plugin?.GetDockerBehavior();
                if (docker is not { RequiresPrivateRegistry: true, RegistryName: not null }) continue;

                var version = imageVersions?.GetValueOrDefault(typeId) ?? "0.1";
                specs.Add(new ImageBuildSpec(docker.GitRepoUrl, version, docker.RegistryName));
            }
            CollectPrivateRegistrySpecs(c.Children, imageVersions, specs);
        }
    }
}
