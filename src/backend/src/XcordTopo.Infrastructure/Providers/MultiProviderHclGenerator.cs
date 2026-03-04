using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Coordinates HCL generation across multiple cloud providers.
/// Single-provider topologies delegate to the existing GenerateHcl path.
/// Multi-provider topologies call each provider's GenerateHclForContainers and merge results.
/// </summary>
public sealed class MultiProviderHclGenerator(ProviderRegistry registry)
{
    public Dictionary<string, string> Generate(Topology topology)
    {
        var activeKeys = TopologyHelpers.CollectActiveProviderKeys(topology);

        // Single provider — delegate to existing path for zero-risk backward compat
        if (activeKeys.Count <= 1)
        {
            var provider = registry.Get(topology.Provider);
            if (provider == null)
                throw new InvalidOperationException($"Provider '{topology.Provider}' is not registered");
            return provider.GenerateHcl(topology);
        }

        // Multi-provider — partition containers by provider, call each
        var containersByProvider = PartitionContainers(topology);
        var mergedFiles = new Dictionary<string, string>();

        foreach (var (providerKey, containers) in containersByProvider)
        {
            var provider = registry.Get(providerKey);
            if (provider == null) continue;

            var files = provider.GenerateHclForContainers(topology, containers);
            foreach (var (fileName, content) in files)
            {
                if (mergedFiles.TryGetValue(fileName, out var existing))
                    mergedFiles[fileName] = existing + "\n" + content;
                else
                    mergedFiles[fileName] = content;
            }
        }

        return mergedFiles;
    }

    private static Dictionary<string, List<Container>> PartitionContainers(Topology topology)
    {
        var result = new Dictionary<string, List<Container>>(StringComparer.OrdinalIgnoreCase);

        void Walk(List<Container> containers)
        {
            foreach (var container in containers)
            {
                // Only top-level provisionable containers get partitioned
                if (container.Kind is ContainerKind.Host
                    or ContainerKind.ComputePool or ContainerKind.Dns)
                {
                    var key = TopologyHelpers.ResolveProviderKey(container, topology);
                    if (!result.TryGetValue(key, out var list))
                    {
                        list = [];
                        result[key] = list;
                    }
                    list.Add(container);
                }
            }
        }

        Walk(topology.Containers);
        return result;
    }
}
