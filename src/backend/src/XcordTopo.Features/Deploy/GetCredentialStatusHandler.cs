using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record GetCredentialStatusRequest(string ProviderKey);

public sealed class GetCredentialStatusHandler(
    ICredentialStore credentialStore,
    ProviderRegistry registry)
    : IRequestHandler<GetCredentialStatusRequest, Result<CredentialStatus>>
{
    public async Task<Result<CredentialStatus>> Handle(GetCredentialStatusRequest request, CancellationToken ct)
    {
        if (registry.Get(request.ProviderKey) is null)
            return Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{request.ProviderKey}' not found");

        return await credentialStore.GetStatusAsync(request.ProviderKey, ct);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/providers/{providerKey}/credentials", async (
            string providerKey,
            GetCredentialStatusHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(
                new GetCredentialStatusRequest(providerKey), ct);
        })
        .WithName("GetCredentialStatus")
        .WithTags("Deploy");
    }
}
