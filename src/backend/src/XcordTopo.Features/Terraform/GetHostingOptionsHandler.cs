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

public sealed record InfraImageCost(
    string ImageName, string ImageKind, string ContainerName,
    int RamMb, string PlanId, string PlanLabel,
    decimal PriceMonthly, int MinReplicas, int MaxReplicas,
    decimal MinCostMonthly, decimal MaxCostMonthly);

public sealed record GetHostingOptionsResponse(
    List<PoolHostingEntry> Pools,
    List<InfraImageCost> InfraImages);

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

        // Collect infrastructure image costs (images NOT inside ComputePools)
        var infraImages = new List<InfraImageCost>();
        CollectInfraImages(topology.Containers, topology, registry, infraImages);

        return new GetHostingOptionsResponse(result, infraImages);
    }

    private static void CollectInfraImages(
        List<Container> containers, Topology topology,
        ProviderRegistry registry, List<InfraImageCost> results)
    {
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.ComputePool)
                continue; // Pool images are handled by the pool section

            // Group all images on this container into one host entry
            if (container.Images.Count > 0 || container.Kind == ContainerKind.Caddy)
            {
                var totalRam = 0;

                // Caddy containers include their own overhead
                if (container.Kind == ContainerKind.Caddy)
                    totalRam += ImageOperationalMetadata.Caddy.MinRamMb;

                foreach (var image in container.Images)
                {
                    var imageRam = ImageOperationalMetadata.Images.TryGetValue(image.Kind, out var meta)
                        ? meta.MinRamMb : 256;
                    totalRam += imageRam;
                }

                if (totalRam > 0)
                {
                    var providerKey = TopologyHelpers.ResolveProviderKey(container, topology);
                    var provider = registry.Get(providerKey);
                    if (provider is not null)
                    {
                        var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
                        var selectedPlan = plans.FirstOrDefault(p => p.MemoryMb >= totalRam) ?? plans.Last();

                        var imageNames = container.Images.Select(i => i.Name).ToList();
                        if (container.Kind == ContainerKind.Caddy)
                            imageNames.Insert(0, "Caddy");
                        var label = string.Join(", ", imageNames);

                        var (minReplicas, maxReplicas) = TopologyHelpers.ParseReplicaRange(container.Config);

                        results.Add(new InfraImageCost(
                            container.Name, label, container.Name,
                            totalRam, selectedPlan.Id, selectedPlan.Label,
                            selectedPlan.PriceMonthly, minReplicas, maxReplicas,
                            selectedPlan.PriceMonthly * minReplicas,
                            selectedPlan.PriceMonthly * maxReplicas));
                    }
                }
            }

            CollectInfraImages(container.Children, topology, registry, results);
        }
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
