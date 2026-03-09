using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record CostEstimateRequest(
    Guid TopologyId, List<TopologyHelpers.PoolSelection>? PoolSelections = null);

public sealed record CostEstimateBody(List<TopologyHelpers.PoolSelection>? PoolSelections);

public sealed record ServiceBreakdownItem(string Name, string Kind, int RamMb);

public sealed record HostCostEntry(
    string HostName, string PlanId, string PlanLabel, int RamMb, int Count, decimal PricePerMonth,
    string? TierProfileId = null, int? TenantsPerHost = null, int? TargetTenants = null,
    List<ServiceBreakdownItem>? Services = null);

public sealed record CostEstimateResponse(List<HostCostEntry> Hosts, decimal TotalMonthly);

public sealed class EstimateCostHandler(
    ITopologyStore topologyStore,
    ProviderRegistry registry)
    : IRequestHandler<CostEstimateRequest, Result<CostEstimateResponse>>
{
    public async Task<Result<CostEstimateResponse>> Handle(CostEstimateRequest request, CancellationToken ct)
    {
        var topology = await topologyStore.GetAsync(request.TopologyId, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", "Topology not found");

        var units = DeploymentUnitBuilder.Build(topology, request.PoolSelections);
        var entries = new List<HostCostEntry>();
        var total = 0m;

        foreach (var unit in units)
        {
            switch (unit)
            {
                case InstanceUnit inst:
                {
                    var provider = registry.Get(inst.ProviderKey);
                    if (provider is null) continue;
                    var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
                    var plan = plans.FirstOrDefault(p => p.MemoryMb >= inst.TotalRamMb) ?? plans.Last();
                    var lineTotal = plan.PriceMonthly * inst.MinReplicas;
                    entries.Add(new HostCostEntry(
                        inst.Container?.Name ?? "instance",
                        plan.Id, plan.Label,
                        inst.TotalRamMb, inst.MinReplicas, lineTotal,
                        Services: inst.Services.Select(s => new ServiceBreakdownItem(s.Name, s.Kind, s.RamMb)).ToList()));
                    total += lineTotal;
                    break;
                }
                case PoolUnit pool:
                {
                    var provider = registry.Get(pool.ProviderKey);
                    if (provider is null) continue;
                    var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();

                    var sharedOverhead = pool.Services.Where(s => s.Scaling == ImageScaling.Shared).Sum(s => s.RamMb);

                    var perTenantMb = 0;
                    foreach (var svc in pool.Services.Where(s => s.Scaling == ImageScaling.PerTenant))
                    {
                        var spec = pool.TierProfile.ImageSpecs.GetValueOrDefault(svc.Kind);
                        perTenantMb += spec?.MemoryMb ?? 256;
                    }

                    ComputePlan selectedPlan;
                    if (pool.SelectedPlanId is not null)
                        selectedPlan = plans.FirstOrDefault(p => p.Id == pool.SelectedPlanId)
                            ?? plans.FirstOrDefault(p => p.MemoryMb >= sharedOverhead + perTenantMb) ?? plans.Last();
                    else
                        selectedPlan = plans.FirstOrDefault(p => p.MemoryMb >= sharedOverhead + perTenantMb) ?? plans.Last();

                    var poolImages = TopologyHelpers.CollectImages(pool.Container!);
                    var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(
                        selectedPlan.MemoryMb, pool.TierProfile, poolImages);
                    var hostsRequired = ImageOperationalMetadata.CalculateHostsRequired(pool.TargetTenants, tenantsPerHost);
                    var ramPerHost = sharedOverhead + (tenantsPerHost * perTenantMb);
                    var lineTotal = selectedPlan.PriceMonthly * hostsRequired;

                    entries.Add(new HostCostEntry(
                        pool.Container?.Name ?? "pool",
                        selectedPlan.Id, selectedPlan.Label,
                        ramPerHost, hostsRequired, lineTotal,
                        pool.TierProfile.Id, tenantsPerHost, pool.TargetTenants,
                        Services: pool.Services.Select(s => new ServiceBreakdownItem(s.Name, s.Kind, s.RamMb)).ToList()));
                    total += lineTotal;
                    break;
                }
                // DnsUnit — no compute cost, skip
            }
        }

        return new CostEstimateResponse(entries, total);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/estimate", async (
            Guid topologyId,
            HttpRequest httpRequest,
            EstimateCostHandler handler,
            CancellationToken ct) =>
        {
            List<TopologyHelpers.PoolSelection>? selections = null;
            if (httpRequest.ContentLength is > 0)
            {
                var body = await httpRequest.ReadFromJsonAsync<CostEstimateBody>(ct);
                selections = body?.PoolSelections;
            }
            return await handler.ExecuteAsync(
                new CostEstimateRequest(topologyId, selections), ct);
        })
        .WithName("EstimateCost")
        .WithTags("Terraform");
    }
}
