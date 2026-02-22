using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Terraform;

public sealed record CancelTerraformRequest(Guid TopologyId);

public sealed record CancelTerraformResponse(bool Cancelled);

public sealed class CancelTerraformHandler(ITerraformExecutor executor)
    : IRequestHandler<CancelTerraformRequest, Result<CancelTerraformResponse>>
{
    public Task<Result<CancelTerraformResponse>> Handle(CancelTerraformRequest request, CancellationToken ct)
    {
        executor.Cancel(request.TopologyId);
        return Task.FromResult<Result<CancelTerraformResponse>>(new CancelTerraformResponse(true));
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/cancel", async (
            Guid topologyId, CancelTerraformHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CancelTerraformRequest(topologyId), ct);
        })
        .WithName("CancelTerraform")
        .WithTags("Terraform");
    }
}
