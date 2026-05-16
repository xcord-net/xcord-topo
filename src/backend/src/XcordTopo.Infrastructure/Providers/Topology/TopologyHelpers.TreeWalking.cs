using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
    // --- Tree-walking ---

    public static List<HostEntry> CollectHosts(List<Container> containers)
    {
        var result = new List<HostEntry>();
        var seen = new HashSet<Container>(ReferenceEqualityComparer.Instance);
        CollectHostsRecursive(containers, result, seen);
        return result;
    }

    private static void CollectHostsRecursive(
        List<Container> containers, List<HostEntry> result, HashSet<Container> seen)
    {
        foreach (var container in containers)
        {
            if (container.Kind is ContainerKind.Host or ContainerKind.DataPool && seen.Add(container))
                result.Add(new HostEntry(container));
            CollectHostsRecursive(container.Children, result, seen);
        }
    }

    public static List<ComputePoolEntry> CollectComputePools(
        List<Container> containers, Topology topology, List<PoolSelection>? selections = null)
    {
        var result = new List<ComputePoolEntry>();
        var seen = new HashSet<Container>(ReferenceEqualityComparer.Instance);
        var tierProfiles = topology.TierProfiles.Count > 0
            ? topology.TierProfiles
            : ImageOperationalMetadata.DefaultTierProfiles;

        CollectComputePoolsRecursive(containers, topology, selections, tierProfiles, result, seen);
        return result;
    }

    private static void CollectComputePoolsRecursive(
        List<Container> containers, Topology topology, List<PoolSelection>? selections,
        List<TierProfile> tierProfiles, List<ComputePoolEntry> result, HashSet<Container> seen)
    {
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.ComputePool && seen.Add(container))
            {
                // One entry per tier profile - each tier gets its own host group
                foreach (var tier in tierProfiles)
                {
                    var selection = selections?.FirstOrDefault(s =>
                        string.Equals(s.PoolName, container.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(s.TierProfileId, tier.Id, StringComparison.OrdinalIgnoreCase));
                    var targetTenants = selection?.TargetTenants ?? 0;
                    var selectedPlanId = selection?.PlanId;

                    result.Add(new ComputePoolEntry(container, tier, targetTenants, selectedPlanId));
                }
            }
            CollectComputePoolsRecursive(container.Children, topology, selections, tierProfiles, result, seen);
        }
    }

    public static List<Image> CollectImages(Container container)
    {
        var images = new List<Image>(container.Images);
        foreach (var child in container.Children)
        {
            images.AddRange(CollectImages(child));
        }
        return images;
    }

    /// <summary>
    /// Collect images from a container tree, skipping subtrees that have their own
    /// infrastructure instances (ComputePool, DataPool, Host). Those images are
    /// provisioned on their own instances, not co-located on this container's host.
    /// </summary>
    public static List<Image> CollectImagesExcludingPools(Container container)
    {
        var images = new List<Image>(container.Images);
        foreach (var child in container.Children)
        {
            if (child.Kind is ContainerKind.ComputePool or ContainerKind.Host or ContainerKind.DataPool) continue;
            images.AddRange(CollectImagesExcludingPools(child));
        }
        return images;
    }

    public static List<Container> CollectCaddyContainers(Container container)
    {
        var caddies = new List<Container>();
        foreach (var child in container.Children)
        {
            if (child.Kind == ContainerKind.Caddy)
                caddies.Add(child);
            else
                caddies.AddRange(CollectCaddyContainers(child));
        }
        return caddies;
    }

    /// <summary>
    /// Collect standalone Caddy containers from a flat list of already-partitioned containers.
    /// Used in multi-provider GenerateHclForContainers where PartitionContainers has already flattened.
    /// Caddies inside a Host are NOT standalone - they're provisioned as part of the Host.
    /// </summary>
    public static List<Container> CollectStandaloneCaddies(List<Container> containers)
    {
        return containers.Where(c => c.Kind == ContainerKind.Caddy).ToList();
    }

    /// <summary>
    /// Recursively collect Caddy containers that are not inside a Host.
    /// Used in single-provider GenerateHcl where containers are still in tree form.
    /// </summary>
    public static List<Container> CollectStandaloneCaddiesRecursive(List<Container> containers)
    {
        var result = new List<Container>();
        CollectCaddiesWalk(containers, false, result);
        return result;
    }

    private static void CollectCaddiesWalk(List<Container> containers, bool insideHost, List<Container> result)
    {
        foreach (var c in containers)
        {
            if (c.Kind == ContainerKind.Caddy && !insideHost)
                result.Add(c);
            CollectCaddiesWalk(c.Children, insideHost || c.Kind is ContainerKind.Host or ContainerKind.DataPool, result);
        }
    }

    /// <summary>
    /// Collect all DNS containers from the topology.
    /// </summary>
    public static List<Container> CollectDnsContainers(List<Container> containers)
    {
        var result = new List<Container>();
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.Dns)
                result.Add(container);
            result.AddRange(CollectDnsContainers(container.Children));
        }
        return result;
    }

    /// <summary>
    /// Collects all Registry images from the topology tree.
    /// </summary>
    public static List<Image> CollectRegistryImages(List<Container> containers)
    {
        var result = new List<Image>();
        foreach (var container in containers)
        {
            result.AddRange(container.Images.Where(i => i.Kind == ImageKind.Registry));
            result.AddRange(CollectRegistryImages(container.Children));
        }
        return result;
    }

    /// <summary>
    /// Resolves the effective registry URL for the topology.
    /// If a Registry image exists, derives the domain from its name + topology domain
    /// (same subdomain pattern as Caddy routing). Falls back to topology-level Registry.
    /// </summary>
    public static string ResolveRegistry(Topology topology)
    {
        var registryImages = CollectRegistryImages(topology.Containers);
        var registry = registryImages.FirstOrDefault();
        if (registry != null)
        {
            var topoDomain = ResolveDomain(topology);
            var subdomain = SanitizeName(registry.Name);
            return $"{subdomain}.{topoDomain}";
        }
        return topology.Registry;
    }

    /// <summary>
    /// Find all containers that should get DNS records for a DNS container.
    /// Uses explicit wires first; if none exist, collects infrastructure containers
    /// from the DNS container's subtree (Host, Caddy, ComputePool).
    /// </summary>
    public static List<Container> CollectContainersWiredToDns(Container dnsContainer, WireResolver resolver)
    {
        var wired = new List<Container>();

        // Check for explicit wires to the DNS "records" port
        var recordsPort = dnsContainer.Ports.FirstOrDefault(p => p.Name == "records");
        if (recordsPort != null)
        {
            var incoming = resolver.ResolveIncoming(dnsContainer.Id, "records");
            foreach (var (node, _) in incoming)
            {
                if (node is Container c)
                    wired.Add(c);
            }
        }

        // If no explicit wires, auto-discover from DNS container's children.
        // Nesting inside a DNS container implies DNS record generation.
        if (wired.Count == 0)
            CollectInfraContainers(dnsContainer.Children, wired);

        return wired;
    }

    private static void CollectInfraContainers(List<Container> containers, List<Container> result)
    {
        foreach (var c in containers)
        {
            if (c.Kind is ContainerKind.Host or ContainerKind.Caddy)
                result.Add(c);
            // Recurse into children - but NOT into ComputePools, whose internal Caddies
            // are Swarm services (not standalone instances) and don't need DNS records
            if (c.Kind != ContainerKind.ComputePool)
                CollectInfraContainers(c.Children, result);
        }
    }

}
