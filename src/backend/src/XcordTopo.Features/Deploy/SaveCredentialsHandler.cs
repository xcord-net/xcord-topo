using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record SaveCredentialsRequest(string ProviderKey, Dictionary<string, string> Variables);

public sealed record SaveCredentialsResponse(string Status);

public sealed class SaveCredentialsHandler(
    ICredentialStore credentialStore,
    LinodeProvider provider)
    : IRequestHandler<SaveCredentialsRequest, Result<SaveCredentialsResponse>>, IValidatable<SaveCredentialsRequest>
{
    public Error? Validate(SaveCredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderKey))
            return Error.Validation("INVALID_PROVIDER", "Provider key is required");
        if (request.Variables is null || request.Variables.Count == 0)
            return Error.Validation("EMPTY_VARIABLES", "At least one variable is required");
        return null;
    }

    public async Task<Result<SaveCredentialsResponse>> Handle(SaveCredentialsRequest request, CancellationToken ct)
    {
        if (!string.Equals(provider.Key, request.ProviderKey, StringComparison.OrdinalIgnoreCase))
            return Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{request.ProviderKey}' not found");

        await credentialStore.SaveAsync(request.ProviderKey, request.Variables, ct);
        return new SaveCredentialsResponse("saved");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/providers/{providerKey}/credentials", async (
            string providerKey,
            SaveCredentialsBody body,
            SaveCredentialsHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(
                new SaveCredentialsRequest(providerKey, body.Variables), ct);
        })
        .WithName("SaveCredentials")
        .WithTags("Deploy");
    }
}

public sealed record SaveCredentialsBody(Dictionary<string, string> Variables);
