using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Walks a topology tree and produces a flat list of DeploymentUnits
/// that cloud providers can translate into infrastructure resources.
/// Tracks two pieces of state through the recursion:
///   currentUnit  - the deployment unit being built (InstanceUnit or PoolUnit)
///   currentPool  - the nearest enclosing pool (propagates through entire subtree)
/// </summary>
public static class DeploymentUnitBuilder
{
    public static List<DeploymentUnit> Build(Topology topology, List<TopologyHelpers.PoolSelection>? selections = null)
    {
        var units = new List<DeploymentUnit>();
        Walk(topology.Containers, topology, selections, currentUnit: null, currentPool: null, units);
        return units;
    }

    private static void Walk(
        List<Container> containers,
        Topology topology,
        List<TopologyHelpers.PoolSelection>? selections,
        DeploymentUnit? currentUnit,
        PoolUnit? currentPool,
        List<DeploymentUnit> units)
    {
        foreach (var container in containers)
        {
            var providerKey = TopologyHelpers.ResolveProviderKey(container, topology);

            switch (container.Kind)
            {
                case ContainerKind.Host:
                case ContainerKind.DataPool:
                {
                    var (min, max) = TopologyHelpers.ParseReplicaRange(container.Config);
                    var instanceUnit = new InstanceUnit(
                        Container: container,
                        ProviderKey: providerKey,
                        Services: new List<ServiceEntry>(),
                        MinReplicas: min,
                        MaxReplicas: max);

                    Descend(container, topology, selections, instanceUnit, currentPool, units);
                    units.Add(instanceUnit);
                    break;
                }

                case ContainerKind.Caddy:
                {
                    if (currentUnit is InstanceUnit instance)
                    {
                        instance.Services.Add(ServiceEntry.FromCaddy(container));
                        Descend(container, topology, selections, currentUnit, currentPool, units);
                    }
                    else if (currentUnit is PoolUnit pool)
                    {
                        pool.Services.Add(ServiceEntry.FromCaddy(container));
                        Descend(container, topology, selections, currentUnit, currentPool, units);
                    }
                    else
                    {
                            // Standalone Caddy - wrap in its own InstanceUnit
                            var (min, max) = TopologyHelpers.ParseReplicaRange(container.Config);
                        var caddyUnit = new InstanceUnit(
                            Container: container,
                            ProviderKey: providerKey,
                            Services: new List<ServiceEntry> { ServiceEntry.FromCaddy(container) },
                            MinReplicas: min,
                            MaxReplicas: max);

                        Descend(container, topology, selections, caddyUnit, currentPool, units);
                        units.Add(caddyUnit);
                    }
                    break;
                }

                case ContainerKind.ComputePool:
                {
                    var tierProfiles = topology.TierProfiles.Count > 0
                        ? topology.TierProfiles
                        : ImageOperationalMetadata.DefaultTierProfiles;

                    var tierProfileId = container.Config.GetValueOrDefault("tierProfile", "");
                    TierProfile? tierProfile = string.IsNullOrEmpty(tierProfileId)
                        ? tierProfiles.OrderByDescending(t =>
                            t.ImageSpecs.Values.Sum(s => s.MemoryMb)).First()
                        : tierProfiles.FirstOrDefault(t => t.Id == tierProfileId)
                            ?? tierProfiles.OrderByDescending(t =>
                                t.ImageSpecs.Values.Sum(s => s.MemoryMb)).First();

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

                        // Pool becomes both currentUnit AND currentPool - pool context propagates
                        Descend(container, topology, selections, poolUnit, poolUnit, units);
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
                        // DNS is a passthrough - descend with the current unit, not the DNS unit
                        Descend(container, topology, selections, currentUnit, currentPool, units);
                    break;
                }

                default:
                {
                        // Grouping node - descend with current unit
                        Descend(container, topology, selections, currentUnit, currentPool, units);
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
        PoolUnit? currentPool,
        List<DeploymentUnit> units)
    {
        var providerKey = TopologyHelpers.ResolveProviderKey(container, topology);

        foreach (var image in container.Images)
        {
            // Inside a pool subtree - all images are Swarm-managed, never break out
            if (currentPool is not null)
            {
                currentPool.Services.Add(ServiceEntry.FromImage(image, TopologyHelpers.ResolveRegistry(topology)));
                continue;
            }

            // Elastic images (replicas > 1) break out into own InstanceUnit (autoscaling group)
            var (min, max) = TopologyHelpers.ParseReplicaRange(image.Config);
            if (min > 1 || max > 1)
            {
                var elasticUnit = new InstanceUnit(
                    Container: null,
                    ProviderKey: providerKey,
                    Services: new List<ServiceEntry> { ServiceEntry.FromImage(image, TopologyHelpers.ResolveRegistry(topology)) },
                    MinReplicas: min,
                    MaxReplicas: max);
                units.Add(elasticUnit);
            }
            else if (currentUnit is InstanceUnit instanceUnit)
            {
                instanceUnit.Services.Add(ServiceEntry.FromImage(image, TopologyHelpers.ResolveRegistry(topology)));
            }
            else if (currentUnit is PoolUnit poolUnit)
            {
                poolUnit.Services.Add(ServiceEntry.FromImage(image, TopologyHelpers.ResolveRegistry(topology)));
            }
        }

        // Then walk child containers
        Walk(container.Children, topology, selections, currentUnit, currentPool, units);
    }
}
