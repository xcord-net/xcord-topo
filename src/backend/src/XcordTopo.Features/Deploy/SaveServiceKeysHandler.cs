using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record SaveServiceKeysRequest(Dictionary<string, string> Variables);

public sealed record SaveServiceKeysResponse(string Status);

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
        var status = await credentialStore.GetStatusAsync("service-keys", ct);
        var alreadySaved = status.SetVariables.ToHashSet();
        var errors = CredentialValidator.Validate(schema, request.Variables, alreadySaved);
        if (errors.Count > 0)
        {
            var detail = string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}"));
            return Error.Validation("CREDENTIAL_VALIDATION_FAILED", detail);
        }

        await credentialStore.SaveAsync("service-keys", request.Variables, ct);
        return new SaveServiceKeysResponse("saved");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/service-keys", async (
            SaveCredentialsBody body,
            SaveServiceKeysHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(
                new SaveServiceKeysRequest(body.Variables), ct);
        })
        .WithName("SaveServiceKeys")
        .WithTags("Deploy");
    }
}
