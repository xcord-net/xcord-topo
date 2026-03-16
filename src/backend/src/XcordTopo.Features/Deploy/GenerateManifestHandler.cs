using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Manifest;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Features.Deploy;

public sealed record GenerateManifestRequest(Guid TopologyId);

public sealed record GenerateManifestResponse(PublicManifest PublicManifest, GatewayTopologySection GatewayTopologySection);

public sealed class GenerateManifestHandler(
    ITopologyStore topologyStore,
    IHclFileManager hclFileManager,
    ImagePluginRegistry imagePluginRegistry)
    : IRequestHandler<GenerateManifestRequest, Result<GenerateManifestResponse>>
{
    public async Task<Result<GenerateManifestResponse>> Handle(GenerateManifestRequest request, CancellationToken ct)
    {
        var topology = await topologyStore.GetAsync(request.TopologyId, ct);
        if (topology == null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", $"Topology {request.TopologyId} not found");

        var stateJson = await hclFileManager.ReadStateAsync(request.TopologyId, ct);
        if (stateJson is null)
            return Error.Validation("NO_TERRAFORM_STATE",
                "Cannot generate manifest: Terraform has not been applied for this topology. Run terraform init/plan/apply first.");

        var generator = new ManifestGenerator(imagePluginRegistry);
        var (publicManifest, gatewaySection) = generator.Generate(topology, stateJson);

        return new GenerateManifestResponse(publicManifest, gatewaySection);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/generate-manifest", async (
            Guid topologyId, GenerateManifestHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GenerateManifestRequest(topologyId), ct);
        })
        .WithName("GenerateManifest")
        .WithTags("Deploy");
    }
}
