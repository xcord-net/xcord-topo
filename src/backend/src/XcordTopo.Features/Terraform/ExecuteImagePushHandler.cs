using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record ImageVersionSpec(string Kind, string Version);

public sealed record ExecuteImagePushRequest(Guid TopologyId, IReadOnlyList<ImageVersionSpec> Images);

public sealed record ExecuteImagePushResponse(string Status);

public sealed record ExecuteImagePushBody(List<ImageVersionSpec> Images);

public sealed class ExecuteImagePushHandler(IImagePushExecutor executor, ICredentialStore credentialStore)
    : IRequestHandler<ExecuteImagePushRequest, Result<ExecuteImagePushResponse>>
{
    private static readonly Dictionary<string, (string RepoUrl, string RegistryName)> ImageKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HubServer"] = ("https://github.com/xcord-net/xcord-hub.git", "hub"),
        ["FederationServer"] = ("https://github.com/xcord-net/xcord-fed.git", "fed"),
    };

    public async Task<Result<ExecuteImagePushResponse>> Handle(ExecuteImagePushRequest request, CancellationToken ct)
    {
        if (executor.IsRunning(request.TopologyId))
            return Error.Conflict("ALREADY_RUNNING", "Image build/push is already running for this topology");

        if (request.Images.Count == 0)
            return Error.Validation("NO_IMAGES", "At least one image must be specified");

        // Validate all image kinds are recognized
        var buildSpecs = new List<ImageBuildSpec>();
        foreach (var image in request.Images)
        {
            if (!ImageKindMap.TryGetValue(image.Kind, out var mapping))
                return Error.Validation("UNKNOWN_IMAGE_KIND", $"Unknown image kind: {image.Kind}");

            if (string.IsNullOrWhiteSpace(image.Version))
                return Error.Validation("MISSING_VERSION", $"Version is required for {image.Kind}");

            buildSpecs.Add(new ImageBuildSpec(mapping.RepoUrl, image.Version, mapping.RegistryName));
        }

        // Load registry credentials from the service-keys store server-side (sensitive values never sent from client)
        var variables = await credentialStore.GetRawVariablesAsync(request.TopologyId, "service-keys", ct);

        if (!variables.TryGetValue("registry_url", out var registryUrl) || string.IsNullOrWhiteSpace(registryUrl))
            return Error.Validation("MISSING_REGISTRY_URL", "registry_url is not set in service keys");

        if (!variables.TryGetValue("registry_username", out var registryUsername) || string.IsNullOrWhiteSpace(registryUsername))
            return Error.Validation("MISSING_REGISTRY_USERNAME", "registry_username is not set in service keys");

        if (!variables.TryGetValue("registry_password", out var registryPassword) || string.IsNullOrWhiteSpace(registryPassword))
            return Error.Validation("MISSING_REGISTRY_PASSWORD", "registry_password is not set in service keys");

        await executor.ExecuteAsync(request.TopologyId, registryUrl, registryUsername, registryPassword, buildSpecs, ct);

        return new ExecuteImagePushResponse("started");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/images/push", async (
            Guid topologyId,
            ExecuteImagePushBody body,
            ExecuteImagePushHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ExecuteImagePushRequest(topologyId, body.Images), ct);
        })
        .WithName("ExecuteImagePush")
        .WithTags("Images");
    }
}
