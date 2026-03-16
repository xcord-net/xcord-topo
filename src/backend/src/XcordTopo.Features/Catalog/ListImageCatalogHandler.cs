using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.PluginSdk;

namespace XcordTopo.Features.Catalog;

public sealed record ListImageCatalogRequest;

public sealed record ListImageCatalogResponse(List<CatalogEntry> Images);

public sealed class ListImageCatalogHandler(ImagePluginRegistry registry)
    : IRequestHandler<ListImageCatalogRequest, Result<ListImageCatalogResponse>>
{
    public Task<Result<ListImageCatalogResponse>> Handle(ListImageCatalogRequest request, CancellationToken ct)
    {
        var catalog = registry.GetCatalog();
        return Task.FromResult<Result<ListImageCatalogResponse>>(new ListImageCatalogResponse(catalog.ToList()));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/catalog/images", async (
            ListImageCatalogHandler handler, CancellationToken ct) =>
        {
            var request = new ListImageCatalogRequest();
            return await handler.ExecuteAsync(request, ct);
        })
        .WithName("ListImageCatalog")
        .WithTags("Catalog");
    }
}
