using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record ExecuteImagePushRequest(Guid TopologyId, string ImageTag);

public sealed record ExecuteImagePushResponse(string Status);

public sealed record ExecuteImagePushBody(string ImageTag);

public sealed class ExecuteImagePushHandler(IImagePushExecutor executor, ICredentialStore credentialStore)
    : IRequestHandler<ExecuteImagePushRequest, Result<ExecuteImagePushResponse>>
{
    public async Task<Result<ExecuteImagePushResponse>> Handle(ExecuteImagePushRequest request, CancellationToken ct)
    {
        if (executor.IsRunning(request.TopologyId))
            return Error.Conflict("ALREADY_RUNNING", "Image push is already running for this topology");

        // Load registry credentials from the service-keys store server-side (sensitive values never sent from client)
        var variables = await credentialStore.GetRawVariablesAsync(request.TopologyId, "service-keys", ct);

        if (!variables.TryGetValue("registry_url", out var registryUrl) || string.IsNullOrWhiteSpace(registryUrl))
            return Error.Validation("MISSING_REGISTRY_URL", "registry_url is not set in service keys");

        if (!variables.TryGetValue("registry_username", out var registryUsername) || string.IsNullOrWhiteSpace(registryUsername))
            return Error.Validation("MISSING_REGISTRY_USERNAME", "registry_username is not set in service keys");

        if (!variables.TryGetValue("registry_password", out var registryPassword) || string.IsNullOrWhiteSpace(registryPassword))
            return Error.Validation("MISSING_REGISTRY_PASSWORD", "registry_password is not set in service keys");

        await executor.ExecuteAsync(request.TopologyId, registryUrl, registryUsername, registryPassword, request.ImageTag, ct);

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
            return await handler.ExecuteAsync(new ExecuteImagePushRequest(topologyId, body.ImageTag), ct);
        })
        .WithName("ExecuteImagePush")
        .WithTags("Images");
    }
}
