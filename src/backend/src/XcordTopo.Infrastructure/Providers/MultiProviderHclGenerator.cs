using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Coordinates HCL generation across multiple cloud providers.
/// Single-provider topologies delegate to the existing GenerateHcl path.
/// Multi-provider topologies call each provider's GenerateHclForContainers and merge results.
/// Generates unified main.tf, variables.tf, and secrets.tf to prevent duplicates.
/// </summary>
public sealed class MultiProviderHclGenerator(ProviderRegistry registry)
{
    private static readonly HashSet<string> UnifiedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "main.tf", "variables.tf", "secrets.tf"
    };

    private static readonly string[] UnifiedFilePrefixes = ["main_", "variables_", "secrets_"];

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

        // Generate unified files that must not be duplicated across providers
        mergedFiles["main.tf"] = GenerateUnifiedMain(activeKeys);
        mergedFiles["variables.tf"] = GenerateUnifiedVariables(activeKeys, topology, poolSelections);
        mergedFiles["secrets.tf"] = GenerateUnifiedSecrets(topology, poolSelections);

        foreach (var group in unitsByProvider)
        {
            var provider = registry.Get(group.Key);
            if (provider == null) continue;

            // Deduplicate containers — multiple units can reference the same container
            var containers = group
                .Select(u => u.Container)
                .Where(c => c != null)
                .Distinct()
                .Cast<Container>()
                .ToList();

            var files = provider.GenerateHclForContainers(topology, containers, poolSelections);
            foreach (var (fileName, content) in files)
            {
                // Skip files we generate as unified versions
                if (IsUnifiedFile(fileName))
                    continue;

                if (mergedFiles.TryGetValue(fileName, out var existing))
                    mergedFiles[fileName] = existing + "\n" + content;
                else
                    mergedFiles[fileName] = content;
            }
        }

        // Strip empty files (e.g. instances_linode.tf when Linode only has DNS)
        var emptyKeys = mergedFiles
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in emptyKeys)
            mergedFiles.Remove(key);

        return mergedFiles;
    }

    private static bool IsUnifiedFile(string fileName)
    {
        if (UnifiedFileNames.Contains(fileName))
            return true;
        foreach (var prefix in UnifiedFilePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string GenerateUnifiedMain(List<string> activeKeys)
    {
        var main = new HclBuilder();

        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                if (activeKeys.Contains("linode"))
                {
                    p.Block("linode", lp =>
                    {
                        lp.Attribute("source", "linode/linode");
                        lp.Attribute("version", "~> 2.0");
                    });
                }

                if (activeKeys.Contains("aws"))
                {
                    p.Block("aws", ap =>
                    {
                        ap.Attribute("source", "hashicorp/aws");
                        ap.Attribute("version", "~> 5.0");
                    });
                }

                p.Block("random", rp =>
                {
                    rp.Attribute("source", "hashicorp/random");
                    rp.Attribute("version", "~> 3.0");
                });
            });
        });
        main.Line();

        if (activeKeys.Contains("linode"))
        {
            main.Block("provider \"linode\"", b =>
            {
                b.RawAttribute("token", "var.linode_token");
            });
            main.Line();
        }

        if (activeKeys.Contains("aws"))
        {
            main.Block("provider \"aws\"", b =>
            {
                b.RawAttribute("access_key", "var.aws_access_key_id");
                b.RawAttribute("secret_key", "var.aws_secret_access_key");
                b.RawAttribute("region", "var.region");
            });
            main.Line();
        }

        return main.ToString();
    }

    private string GenerateUnifiedVariables(
        List<string> activeKeys,
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections)
    {
        var vars = new HclBuilder();

        // Provider-specific credential variables
        if (activeKeys.Contains("linode"))
        {
            vars.Block("variable \"linode_token\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("description", "Linode API token");
                b.RawAttribute("sensitive", "true");
            });
            vars.Line();
        }

        if (activeKeys.Contains("aws"))
        {
            vars.Block("variable \"aws_access_key_id\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("description", "AWS access key ID");
                b.RawAttribute("sensitive", "true");
            });
            vars.Line();
            vars.Block("variable \"aws_secret_access_key\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("description", "AWS secret access key");
                b.RawAttribute("sensitive", "true");
            });
            vars.Line();
        }

        // Shared variables (region, domain, ssh_public_key)
        var defaultRegion = activeKeys.Contains("aws")
            ? topology.ProviderConfig.GetValueOrDefault("region", "us-east-1")
            : topology.ProviderConfig.GetValueOrDefault("region", "us-east");

        vars.Block("variable \"region\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", defaultRegion);
            b.Attribute("description", "Deployment region");
        });
        vars.Line();
        vars.Block("variable \"domain\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Primary domain name");
        });
        vars.Line();
        vars.Block("variable \"ssh_public_key\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "SSH public key for instance access");
        });
        vars.Line();
        vars.Block("variable \"ssh_private_key\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "SSH private key for provisioner authentication");
            b.RawAttribute("sensitive", "true");
        });

        // Registry variables (hardcoded here like per-provider paths, with resolved default)
        vars.Line();
        vars.Block("variable \"registry_url\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", TopologyHelpers.ResolveRegistry(topology));
            b.Attribute("description", "Docker registry URL for pulling xcord images");
        });
        vars.Line();
        vars.Block("variable \"app_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "latest");
            b.Attribute("description", "Version tag for xcord application images (hub, fed)");
        });
        vars.Line();
        vars.Block("variable \"registry_username\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "Docker registry username (leave empty for no auth)");
        });
        vars.Line();
        vars.Block("variable \"registry_password\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "Docker registry password or token");
            b.RawAttribute("sensitive", "true");
        });

        // Host replica variables
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        ProviderHclBase.CollectHostReplicaVariables(hosts, vars);

        // Elastic image replica variables
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        ProviderHclBase.CollectElasticImageVariables(hosts, standaloneCaddies, vars);

        // Pool variables — group by provider for correct plan resolution
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var poolsByProvider = pools
            .GroupBy(p => TopologyHelpers.ResolveProviderKey(p.Pool, topology), StringComparer.OrdinalIgnoreCase);

        foreach (var group in poolsByProvider)
        {
            var provider = registry.Get(group.Key);
            if (provider == null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            ProviderHclBase.GeneratePoolVariables(vars, group.ToList(), plans);
        }

        // Data pool variables (deferred — count defaults to 0)
        ProviderHclBase.GenerateDataPoolVariables(vars, topology);

        // Service key variables (emitted once, not per-provider)
        ProviderHclBase.GenerateServiceKeyVariables(vars, topology);

        return vars.ToString();
    }

    private static string GenerateUnifiedSecrets(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections)
    {
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology);
        return ProviderHclBase.GenerateSecrets(hosts, resolver, pools, standaloneCaddies);
    }
}
