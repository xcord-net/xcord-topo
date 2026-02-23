using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Providers;

public sealed record GetCredentialSchemaRequest(string Key);

public sealed record GetCredentialSchemaResponse(List<CredentialField> Fields);

public sealed class GetCredentialSchemaHandler(ProviderRegistry registry)
    : IRequestHandler<GetCredentialSchemaRequest, Result<GetCredentialSchemaResponse>>
{
    public Task<Result<GetCredentialSchemaResponse>> Handle(GetCredentialSchemaRequest request, CancellationToken ct)
    {
        var provider = registry.Get(request.Key);
        if (provider is null)
            return Task.FromResult<Result<GetCredentialSchemaResponse>>(
                Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{request.Key}' not found"));

        return Task.FromResult<Result<GetCredentialSchemaResponse>>(
            new GetCredentialSchemaResponse(provider.GetCredentialSchema()));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/providers/{key}/credential-schema", async (
            string key, GetCredentialSchemaHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetCredentialSchemaRequest(key), ct);
        })
        .WithName("GetCredentialSchema")
        .WithTags("Providers");
    }
}
