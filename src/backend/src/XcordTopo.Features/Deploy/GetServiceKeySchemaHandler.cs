using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Deploy;

public sealed record GetServiceKeySchemaRequest(Guid? TopologyId = null);

public sealed record GetServiceKeySchemaResponse(List<CredentialField> Fields);

public sealed class GetServiceKeySchemaHandler(ITopologyStore topologyStore)
    : IRequestHandler<GetServiceKeySchemaRequest, Result<GetServiceKeySchemaResponse>>
{
    public async Task<Result<GetServiceKeySchemaResponse>> Handle(GetServiceKeySchemaRequest request, CancellationToken ct)
    {
        Topology? topology = null;
        if (request.TopologyId.HasValue)
            topology = await topologyStore.GetAsync(request.TopologyId.Value, ct);

        return new GetServiceKeySchemaResponse(ServiceKeySchema.GetSchema(topology));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/service-keys/schema", async (
            Guid? topologyId,
            GetServiceKeySchemaHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetServiceKeySchemaRequest(topologyId), ct);
        })
        .WithName("GetServiceKeySchema")
        .WithTags("Deploy");
    }
}
