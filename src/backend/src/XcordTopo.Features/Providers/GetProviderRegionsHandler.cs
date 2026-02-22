using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Providers;

public sealed record GetProviderRegionsRequest(string Key);

public sealed record GetProviderRegionsResponse(List<Region> Regions);

public sealed class GetProviderRegionsHandler(ProviderRegistry registry)
    : IRequestHandler<GetProviderRegionsRequest, Result<GetProviderRegionsResponse>>
{
    public Task<Result<GetProviderRegionsResponse>> Handle(GetProviderRegionsRequest request, CancellationToken ct)
    {
        var provider = registry.Get(request.Key);
        if (provider is null)
            return Task.FromResult<Result<GetProviderRegionsResponse>>(
                Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{request.Key}' not found"));

        return Task.FromResult<Result<GetProviderRegionsResponse>>(
            new GetProviderRegionsResponse(provider.GetRegions()));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/providers/{key}/regions", async (
            string key, GetProviderRegionsHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetProviderRegionsRequest(key), ct);
        })
        .WithName("GetProviderRegions")
        .WithTags("Providers");
    }
}
