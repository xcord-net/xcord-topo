using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record GetServiceKeyStatusRequest;

public sealed class GetServiceKeyStatusHandler(ICredentialStore credentialStore)
    : IRequestHandler<GetServiceKeyStatusRequest, Result<CredentialStatus>>
{
    public async Task<Result<CredentialStatus>> Handle(GetServiceKeyStatusRequest request, CancellationToken ct)
    {
        return await credentialStore.GetStatusAsync("service-keys", ct);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/service-keys", async (
            GetServiceKeyStatusHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetServiceKeyStatusRequest(), ct);
        })
        .WithName("GetServiceKeyStatus")
        .WithTags("Deploy");
    }
}
