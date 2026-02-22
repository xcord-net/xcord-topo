using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Features.Terraform;

public sealed record CostEstimateRequest(Guid TopologyId);

public sealed record HostCostEntry(string HostName, string PlanId, string PlanLabel, int RamMb, int Count, decimal PricePerMonth);

public sealed record CostEstimateResponse(List<HostCostEntry> Hosts, decimal TotalMonthly);

public sealed class EstimateCostHandler(
    ITopologyStore topologyStore,
    ProviderRegistry providerRegistry)
    : IRequestHandler<CostEstimateRequest, Result<CostEstimateResponse>>
{
    public async Task<Result<CostEstimateResponse>> Handle(CostEstimateRequest request, CancellationToken ct)
    {
        var topology = await topologyStore.GetAsync(request.TopologyId, ct);
        if (topology is null)
            return Error.NotFound("TOPOLOGY_NOT_FOUND", "Topology not found");

        var provider = providerRegistry.Get(topology.Provider);
        if (provider is null)
            return Error.NotFound("PROVIDER_NOT_FOUND", $"Provider '{topology.Provider}' not found");

        var hosts = LinodeProvider.CollectHosts(topology.Containers, null);
        var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        var entries = new List<HostCostEntry>();
        var total = 0m;

        foreach (var entry in hosts)
        {
            var requiredRam = LinodeProvider.CalculateHostRam(entry.Host);
            var selectedPlan = plans.FirstOrDefault(p => p.MemoryMb >= requiredRam) ?? plans.Last();

            // Determine count
            var count = 1;
            if (entry.FedGroup != null)
            {
                var instanceCount = entry.FedGroup.Config.GetValueOrDefault("instanceCount", "1");
                if (int.TryParse(instanceCount, out var n)) count = n;
            }
            else
            {
                var (literal, _) = LinodeProvider.ParseHostReplicas(entry.Host);
                if (literal.HasValue) count = literal.Value;
            }

            var lineTotal = selectedPlan.PriceMonthly * count;
            entries.Add(new HostCostEntry(
                entry.Host.Name,
                selectedPlan.Id,
                selectedPlan.Label,
                requiredRam,
                count,
                lineTotal));
            total += lineTotal;
        }

        return new CostEstimateResponse(entries, total);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/topologies/{topologyId:guid}/terraform/estimate", async (
            Guid topologyId,
            EstimateCostHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CostEstimateRequest(topologyId), ct);
        })
        .WithName("EstimateCost")
        .WithTags("Terraform");
    }
}
