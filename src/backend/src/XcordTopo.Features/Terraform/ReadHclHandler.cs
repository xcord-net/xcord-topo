using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Terraform;

public sealed record ReadHclRequest(Guid TopologyId);

public sealed record ReadHclResponse(Dictionary<string, string> Files);

public sealed class ReadHclHandler(IHclFileManager hclFileManager)
    : IRequestHandler<ReadHclRequest, Result<ReadHclResponse>>
{
    public async Task<Result<ReadHclResponse>> Handle(ReadHclRequest request, CancellationToken ct)
    {
        var files = await hclFileManager.ReadFilesAsync(request.TopologyId, ct);
        return new ReadHclResponse(files);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies/{topologyId:guid}/terraform/hcl", async (
            Guid topologyId, ReadHclHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ReadHclRequest(topologyId), ct);
        })
        .WithName("ReadHcl")
        .WithTags("Terraform");
    }
}
