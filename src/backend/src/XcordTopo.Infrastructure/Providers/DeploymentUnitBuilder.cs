using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Walks a topology tree and produces a flat list of DeploymentUnits
/// that cloud providers can translate into infrastructure resources.
/// </summary>
public static class DeploymentUnitBuilder
{
    public static List<DeploymentUnit> Build(Topology topology, List<TopologyHelpers.PoolSelection>? selections = null)
    {
        var units = new List<DeploymentUnit>();
        Walk(topology.Containers, topology, selections, currentUnit: null, units);
        return units;
    }

    private static void Walk(
        List<Container> containers,
        Topology topology,
        List<TopologyHelpers.PoolSelection>? selections,
        DeploymentUnit? currentUnit,
        List<DeploymentUnit> units)
    {
        foreach (var container in containers)
        {
            var providerKey = TopologyHelpers.ResolveProviderKey(container, topology);

            switch (container.Kind)
            {
                case ContainerKind.Host:
                {
                    var (min, max) = TopologyHelpers.ParseReplicaRange(container.Config);
                    var instanceUnit = new InstanceUnit(
                        Container: container,
                        ProviderKey: providerKey,
                        Services: new List<ServiceEntry>(),
                        MinReplicas: min,
                        MaxReplicas: max);

                    Descend(container, topology, selections, instanceUnit, units);
                    units.Add(instanceUnit);
                    break;
                }

                case ContainerKind.Caddy:
                {
                    if (currentUnit is InstanceUnit instance)
                    {
                        instance.Services.Add(ServiceEntry.FromCaddy(container));
                        Descend(container, topology, selections, currentUnit, units);
                    }
                    else if (currentUnit is PoolUnit pool)
                    {
                        pool.Services.Add(ServiceEntry.FromCaddy(container));
                        Descend(container, topology, selections, currentUnit, units);
                    }
                    else
                    {
                        // Standalone Caddy — wrap in its own InstanceUnit
                        var (min, max) = TopologyHelpers.ParseReplicaRange(container.Config);
                        var caddyUnit = new InstanceUnit(
                            Container: container,
                            ProviderKey: providerKey,
                            Services: new List<ServiceEntry> { ServiceEntry.FromCaddy(container) },
                            MinReplicas: min,
                            MaxReplicas: max);

                        Descend(container, topology, selections, caddyUnit, units);
                        units.Add(caddyUnit);
                    }
                    break;
                }

                case ContainerKind.ComputePool:
                {
                    var tierProfiles = topology.TierProfiles.Count > 0
                        ? topology.TierProfiles
                        : ImageOperationalMetadata.DefaultTierProfiles;

                    var tierProfileId = container.Config.GetValueOrDefault("tierProfile", "free");
                    var tierProfile = tierProfiles.FirstOrDefault(t => t.Id == tierProfileId)
                        ?? tierProfiles.First();

                    var selection = selections?.FirstOrDefault(s =>
                        s.PoolName.Equals(container.Name, StringComparison.OrdinalIgnoreCase));
                    var targetTenants = selection?.TargetTenants ?? 0;
                    var selectedPlanId = selection?.PlanId;

                    var poolUnit = new PoolUnit(
                        Container: container,
                        ProviderKey: providerKey,
                        Services: new List<ServiceEntry>(),
                        TierProfile: tierProfile,
                        TargetTenants: targetTenants,
                        SelectedPlanId: selectedPlanId);

                    Descend(container, topology, selections, poolUnit, units);
                    units.Add(poolUnit);
                    break;
                }

                case ContainerKind.Dns:
                {
                    var domain = container.Config.GetValueOrDefault("domain", container.Name);
                    var dnsUnit = new DnsUnit(
                        Container: container,
                        ProviderKey: providerKey,
                        Domain: domain);

                    units.Add(dnsUnit);
                    // DNS is a passthrough — descend with the current unit, not the DNS unit
                    Descend(container, topology, selections, currentUnit, units);
                    break;
                }

                default:
                {
                    // Grouping node — descend with current unit
                    Descend(container, topology, selections, currentUnit, units);
                    break;
                }
            }
        }
    }

    private static void Descend(
        Container container,
        Topology topology,
        List<TopologyHelpers.PoolSelection>? selections,
        DeploymentUnit? currentUnit,
        List<DeploymentUnit> units)
    {
        // Add images first
        if (currentUnit is InstanceUnit instanceUnit)
        {
            foreach (var image in container.Images)
                instanceUnit.Services.Add(ServiceEntry.FromImage(image));
        }
        else if (currentUnit is PoolUnit poolUnit)
        {
            foreach (var image in container.Images)
                poolUnit.Services.Add(ServiceEntry.FromImage(image));
        }

        // Then walk child containers
        Walk(container.Children, topology, selections, currentUnit, units);
    }
}
