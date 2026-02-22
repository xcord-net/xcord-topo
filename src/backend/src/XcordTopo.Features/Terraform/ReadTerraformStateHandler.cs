using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Terraform;

public sealed record ReadTerraformStateRequest(Guid TopologyId);

public sealed record ReadTerraformStateResponse(string? State);

public sealed class ReadTerraformStateHandler(IHclFileManager hclFileManager)
    : IRequestHandler<ReadTerraformStateRequest, Result<ReadTerraformStateResponse>>
{
    public async Task<Result<ReadTerraformStateResponse>> Handle(ReadTerraformStateRequest request, CancellationToken ct)
    {
        var state = await hclFileManager.ReadStateAsync(request.TopologyId, ct);
        return new ReadTerraformStateResponse(state);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies/{topologyId:guid}/terraform/state", async (
            Guid topologyId, ReadTerraformStateHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ReadTerraformStateRequest(topologyId), ct);
        })
        .WithName("ReadTerraformState")
        .WithTags("Terraform");
    }
}
