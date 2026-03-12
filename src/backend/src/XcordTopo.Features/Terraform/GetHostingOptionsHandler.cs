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
    int DiskGb, decimal PriceMonthly, int TenantsPerHost, decimal CostPerTenant);

public sealed record PoolHostingEntry(
    string PoolName, string TierProfileId, string TierProfileName,
    List<PoolHostingOption> Options);

public sealed record InfraImagePlan(
    string PlanId, string PlanLabel, int MemoryMb, int VCpus,
    int DiskGb, decimal PriceMonthly);

public sealed record InfraImageCost(
    string ImageName, string ImageKind, string ContainerName,
    int RamMb, string PlanId, string PlanLabel,
    int DiskGb, decimal PriceMonthly, int MinReplicas, int MaxReplicas,
    decimal MinCostMonthly, decimal MaxCostMonthly,
    List<InfraImagePlan> AvailablePlans,
    List<ServiceBreakdownItem>? Services = null);

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

        var units = DeploymentUnitBuilder.Build(topology);

        // Build infra image costs from InstanceUnits (skip DataPool — provisioned on demand)
        var infraImages = new List<InfraImageCost>();
        foreach (var unit in units)
        {
            if (unit is not InstanceUnit inst) continue;
            if (inst.Container?.Kind == ContainerKind.DataPool) continue;

            var provider = registry.Get(inst.ProviderKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var viablePlans = plans.Where(p => p.MemoryMb >= inst.TotalRamMb).ToList();
            var selectedPlan = viablePlans.FirstOrDefault() ?? plans.Last();

            var availablePlans = viablePlans.Select(p => new InfraImagePlan(
                p.Id, p.Label, p.MemoryMb, p.VCpus, p.DiskGb, p.PriceMonthly)).ToList();

            var imageNames = inst.Services.Select(s => s.Name).ToList();
            var label = string.Join(", ", imageNames);
            var name = inst.Container?.Name
                ?? inst.Services.FirstOrDefault()?.Name
                ?? "instance";

            infraImages.Add(new InfraImageCost(
                name, label, name,
                inst.TotalRamMb, selectedPlan.Id, selectedPlan.Label,
                selectedPlan.DiskGb, selectedPlan.PriceMonthly,
                inst.MinReplicas, inst.MaxReplicas,
                selectedPlan.PriceMonthly * inst.MinReplicas,
                selectedPlan.PriceMonthly * inst.MaxReplicas,
                availablePlans,
                Services: inst.Services.Select(s => new ServiceBreakdownItem(s.Name, s.Kind, s.RamMb)).ToList()));
        }

        // Build pool hosting options — keep per-plan tenantsPerHost calculation
        var result = new List<PoolHostingEntry>();
        foreach (var unit in units)
        {
            if (unit is not PoolUnit pool) continue;

            var provider = registry.Get(pool.ProviderKey);
            if (provider is null) continue;

            // Tier-agnostic pools (no tierProfile) skip hosting options — hub decides placement
            if (pool.TierProfile is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var poolImages = TopologyHelpers.CollectImages(pool.Container!);
            var options = new List<PoolHostingOption>();

            foreach (var plan in plans)
            {
                var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(
                    plan.MemoryMb, pool.TierProfile, poolImages);
                if (tenantsPerHost < 1) continue;

                var costPerTenant = plan.PriceMonthly / tenantsPerHost;
                options.Add(new PoolHostingOption(
                    plan.Id, plan.Label, plan.MemoryMb, plan.VCpus,
                    plan.DiskGb, plan.PriceMonthly, tenantsPerHost,
                    Math.Round(costPerTenant, 2)));
            }

            result.Add(new PoolHostingEntry(
                pool.Container?.Name ?? "pool", pool.TierProfile.Id, pool.TierProfile.Name, options));
        }

        return new GetHostingOptionsResponse(result, infraImages);
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
