using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Coordinates HCL generation across multiple cloud providers.
/// Single-provider topologies delegate to the existing GenerateHcl path.
/// Multi-provider topologies call each provider's GenerateHclForContainers and merge results.
/// </summary>
public sealed class MultiProviderHclGenerator(ProviderRegistry registry)
{
    public Dictionary<string, string> Generate(
        Topology topology, List<TopologyHelpers.PoolSelection>? poolSelections = null)
    {
        var activeKeys = TopologyHelpers.CollectActiveProviderKeys(topology);

        // Single provider — delegate to existing path for zero-risk backward compat
        if (activeKeys.Count <= 1)
        {
            var provider = registry.Get(topology.Provider);
            if (provider == null)
                throw new InvalidOperationException($"Provider '{topology.Provider}' is not registered");
            return provider.GenerateHcl(topology, poolSelections);
        }

        // Multi-provider — build deployment units and group by provider key
        var units = DeploymentUnitBuilder.Build(topology, poolSelections);
        var unitsByProvider = units
            .GroupBy(u => u.ProviderKey, StringComparer.OrdinalIgnoreCase);

        var mergedFiles = new Dictionary<string, string>();

        foreach (var group in unitsByProvider)
        {
            var provider = registry.Get(group.Key);
            if (provider == null) continue;

            // Transitional: extract containers from units until providers accept units directly
            var containers = group
                .Select(u => u.Container)
                .Where(c => c != null)
                .Cast<Container>()
                .ToList();

            var files = provider.GenerateHclForContainers(topology, containers, poolSelections);
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
}
