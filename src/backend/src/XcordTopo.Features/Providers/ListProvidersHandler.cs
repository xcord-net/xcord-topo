using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Providers;

public sealed record ListProvidersRequest;

public sealed record ListProvidersResponse(List<ProviderInfo> Providers);

public sealed class ListProvidersHandler(LinodeProvider provider)
    : IRequestHandler<ListProvidersRequest, Result<ListProvidersResponse>>
{
    public Task<Result<ListProvidersResponse>> Handle(ListProvidersRequest request, CancellationToken ct)
    {
        var providers = new List<ProviderInfo> { provider.GetInfo() };
        return Task.FromResult<Result<ListProvidersResponse>>(new ListProvidersResponse(providers));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/providers", async (
            ListProvidersHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ListProvidersRequest(), ct);
        })
        .WithName("ListProviders")
        .WithTags("Providers");
    }
}
