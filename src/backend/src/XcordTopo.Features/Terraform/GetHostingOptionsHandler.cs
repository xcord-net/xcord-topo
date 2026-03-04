using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record GetHostingOptionsRequest(Guid TopologyId);

public sealed record PoolHostingOption(
    string PlanId, string PlanLabel, int MemoryMb, int VCpus,
    decimal PriceMonthly, int TenantsPerHost, decimal CostPerTenant);

public sealed record PoolHostingEntry(
    string PoolName, string TierProfileId, string TierProfileName,
    List<PoolHostingOption> Options);

public sealed record GetHostingOptionsResponse(List<PoolHostingEntry> Pools);

public sealed class GetHostingOptionsHandler(
    ITopologyStore topologyStore,
    ProviderRegistry registry)
    : IRequestHandler<GetHostingOptionsRequest, Result<GetHostingOptionsResponse>>
{
    public async Task<Result<GetHostingOptionsResponse>> Handle(
        GetHostingOptionsRequest request, CancellationToken ct)
    {
        var topology = await topologyStore.GetAsync(request.TopologyId, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", "Topology not found");

        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology);
        var result = new List<PoolHostingEntry>();

        foreach (var pool in pools)
        {
            var providerKey = TopologyHelpers.ResolveProviderKey(pool.Pool, topology);
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var poolImages = TopologyHelpers.CollectImages(pool.Pool);
            var options = new List<PoolHostingOption>();

            foreach (var plan in plans)
            {
                var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(
                    plan.MemoryMb, pool.TierProfile, poolImages);
                if (tenantsPerHost < 1) continue;

                var costPerTenant = plan.PriceMonthly / tenantsPerHost;
                options.Add(new PoolHostingOption(
                    plan.Id, plan.Label, plan.MemoryMb, plan.VCpus,
                    plan.PriceMonthly, tenantsPerHost,
                    Math.Round(costPerTenant, 2)));
            }

            result.Add(new PoolHostingEntry(
                pool.Pool.Name, pool.TierProfile.Id, pool.TierProfile.Name, options));
        }

        return new GetHostingOptionsResponse(result);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/topologies/{topologyId:guid}/hosting-options", async (
            Guid topologyId, GetHostingOptionsHandler handler, CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetHostingOptionsRequest(topologyId), ct);
        })
        .WithName("GetHostingOptions")
        .WithTags("Terraform");
    }
}
