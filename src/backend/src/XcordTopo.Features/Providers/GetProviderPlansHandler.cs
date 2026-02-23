using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Providers;

public sealed record GetProviderPlansRequest(string Key);

public sealed record GetProviderPlansResponse(List<ComputePlan> Plans);

public sealed class GetProviderPlansHandler(ProviderRegistry registry)
    : IRequestHandler<GetProviderPlansRequest, Result<GetProviderPlansResponse>>
{
    public Task<Result<GetProviderPlansResponse>> Handle(GetProviderPlansRequest request, CancellationToken ct)
    {
        var provider = registry.Get(request.Key);
        if (provider is null)
            return Task.FromResult<Result<GetProviderPlansResponse>>(
                Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{request.Key}' not found"));

        return Task.FromResult<Result<GetProviderPlansResponse>>(
            new GetProviderPlansResponse(provider.GetPlans()));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/providers/{key}/plans", async (
            string key, GetProviderPlansHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetProviderPlansRequest(key), ct);
        })
        .WithName("GetProviderPlans")
        .WithTags("Providers");
    }
}
