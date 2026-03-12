using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record SaveServiceKeysRequest(Guid TopologyId, Dictionary<string, string> Variables);

public sealed record SaveServiceKeysResponse(string Status, CredentialStatus UpdatedStatus);

public sealed class SaveServiceKeysHandler(ICredentialStore credentialStore)
    : IRequestHandler<SaveServiceKeysRequest, Result<SaveServiceKeysResponse>>, IValidatable<SaveServiceKeysRequest>
{
    public Error? Validate(SaveServiceKeysRequest request)
    {
        if (request.Variables is null || request.Variables.Count == 0)
            return Error.Validation("EMPTY_VARIABLES", "At least one variable is required");
        return null;
    }

    public async Task<Result<SaveServiceKeysResponse>> Handle(SaveServiceKeysRequest request, CancellationToken ct)
    {
        var schema = ServiceKeySchema.GetSchema();
        var errors = CredentialValidator.Validate(schema, request.Variables);
        if (errors.Count > 0)
        {
            var detail = string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}"));
            return Error.Validation("SERVICE_KEY_VALIDATION_FAILED", detail);
        }

        await credentialStore.SaveAsync(request.TopologyId, "service-keys", request.Variables, ct);
        var updatedStatus = await credentialStore.GetStatusAsync(request.TopologyId, "service-keys", ct);
        return new SaveServiceKeysResponse("saved", updatedStatus);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/service-keys", async (
            Guid topologyId,
            SaveCredentialsBody body,
            SaveServiceKeysHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(
                new SaveServiceKeysRequest(topologyId, body.Variables), ct);
        })
        .WithName("SaveServiceKeys")
        .WithTags("Deploy");
    }
}
