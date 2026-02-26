using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record GetServiceKeySchemaRequest;

public sealed record GetServiceKeySchemaResponse(List<CredentialField> Fields);

public sealed class GetServiceKeySchemaHandler
    : IRequestHandler<GetServiceKeySchemaRequest, Result<GetServiceKeySchemaResponse>>
{
    public Task<Result<GetServiceKeySchemaResponse>> Handle(GetServiceKeySchemaRequest request, CancellationToken ct)
    {
        return Task.FromResult<Result<GetServiceKeySchemaResponse>>(
            new GetServiceKeySchemaResponse(ServiceKeySchema.GetSchema()));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/service-keys/schema", async (
            GetServiceKeySchemaHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetServiceKeySchemaRequest(), ct);
        })
        .WithName("GetServiceKeySchema")
        .WithTags("Deploy");
    }
}
