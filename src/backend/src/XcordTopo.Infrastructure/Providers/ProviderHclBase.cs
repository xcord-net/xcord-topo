using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Abstract base class that extracts shared HCL generation logic from cloud providers.
/// Identical methods (SelectPlan, ResolvePoolPlan, CollectHostReplicaVariables, GenerateSecrets,
/// GenerateOutputs, GetIpReference) live here. Provider-specific generation methods are abstract.
/// </summary>
public abstract class ProviderHclBase : ICloudProvider
{
    // --- Provider-specific properties for parameterized shared methods ---

    protected abstract string InstanceResourceType { get; }
    protected abstract string PublicIpField { get; }
    protected abstract string PrivateIpField { get; }

    // --- ICloudProvider interface ---

    public abstract string Key { get; }
    public abstract ProviderInfo GetInfo();
    public abstract List<Region> GetRegions();
    public abstract List<ComputePlan> GetPlans();
    public abstract List<CredentialField> GetCredentialSchema();
    public abstract Dictionary<string, string> GenerateHcl(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null);
    public abstract Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null);

    // --- Shared methods (identical across providers) ---

    internal string SelectPlan(int requiredRamMb)
    {
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        return (plans.FirstOrDefault(p => p.MemoryMb >= requiredRamMb) ?? plans.Last()).Id;
    }

    internal string SelectPlan(string name, int requiredRamMb, List<TopologyHelpers.InfraSelection>? infraSelections)
    {
        var selection = infraSelections?.FirstOrDefault(
            s => string.Equals(s.ImageName, name, StringComparison.OrdinalIgnoreCase));
        if (selection is not null)
        {
            var plans = GetPlans();
            if (plans.Any(p => p.Id == selection.PlanId))
                return selection.PlanId;
        }
        return SelectPlan(requiredRamMb);
    }

    internal static ComputePlan ResolvePoolPlan(TopologyHelpers.ComputePoolEntry pool, List<ComputePlan> plans)
    {
        if (pool.SelectedPlanId is not null)
        {
            var selected = plans.FirstOrDefault(p => p.Id == pool.SelectedPlanId);
            if (selected is not null) return selected;
        }

        var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
        var poolImages = TopologyHelpers.CollectImages(pool.Pool);
        var sharedOverhead = ImageOperationalMetadata.CalculateSharedOverheadMb(poolImages);
        var minHostRam = sharedOverhead + fedMemory;
        return plans.FirstOrDefault(p => p.MemoryMb >= minHostRam) ?? plans.Last();
    }

    internal static void CollectHostReplicaVariables(List<TopologyHelpers.HostEntry> hosts, HclBuilder vars)
    {
        foreach (var entry in hosts)
        {
            var (literal, varRef) = TopologyHelpers.ParseHostReplicas(entry.Host);
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");

            var needsVariable = varRef != null || (literal.HasValue && literal.Value > 1 && hasMinMax);
            if (!needsVariable) continue;

            var varName = varRef != null ? TopologyHelpers.SanitizeName(varRef) : $"{TopologyHelpers.SanitizeName(entry.Host.Name)}_replicas";
            var defaultValue = literal ?? 1;

            var minStr = entry.Host.Config.GetValueOrDefault("minReplicas", "");
            var maxStr = entry.Host.Config.GetValueOrDefault("maxReplicas", "");
            var hasMin = int.TryParse(minStr, out var minVal);
            var hasMax = int.TryParse(maxStr, out var maxVal);

            vars.Line();
            vars.Block($"variable \"{varName}\"", b =>
            {
                b.RawAttribute("type", "number");
                b.Attribute("default", defaultValue);
                b.Attribute("description", $"Number of replicas for host '{entry.Host.Name}'");

                if (hasMin || hasMax)
                {
                    b.Block("validation", vb =>
                    {
                        if (hasMin && hasMax)
                        {
                            vb.RawAttribute("condition", $"var.{varName} >= {minVal} && var.{varName} <= {maxVal}");
                            vb.Attribute("error_message", $"Replicas must be between {minVal} and {maxVal}.");
                        }
                        else if (hasMin)
                        {
                            vb.RawAttribute("condition", $"var.{varName} >= {minVal}");
                            vb.Attribute("error_message", $"Replicas must be at least {minVal}.");
                        }
                        else
                        {
                            vb.RawAttribute("condition", $"var.{varName} <= {maxVal}");
                            vb.Attribute("error_message", $"Replicas must be at most {maxVal}.");
                        }
                    });
                }
            });
        }
    }

    internal static string GenerateSecrets(
        List<TopologyHelpers.HostEntry> hosts,
        WireResolver resolver,
        List<TopologyHelpers.ComputePoolEntry>? pools = null,
        List<Container>? standaloneCaddies = null)
    {
        var secrets = new HclBuilder();
        foreach (var secret in hosts.SelectMany(entry => TopologyHelpers.CollectSecrets(entry, resolver)))
        {
            secrets.Block($"resource \"random_password\" \"{secret.ResourceName}\"", b =>
            {
                b.Attribute("length", 32);
                b.RawAttribute("special", "false");
            });
            secrets.Line();
        }

        // Standalone Caddy images (pg_hub, redis_hub, etc.) need secrets too
        if (standaloneCaddies != null)
        {
            foreach (var secret in standaloneCaddies.SelectMany(caddy =>
                TopologyHelpers.CollectSecrets(new TopologyHelpers.HostEntry(caddy), resolver, excludePools: true)))
            {
                secrets.Block($"resource \"random_password\" \"{secret.ResourceName}\"", b =>
                {
                    b.Attribute("length", 32);
                    b.RawAttribute("special", "false");
                });
                secrets.Line();
            }
        }

        // Pool shared service secrets — data-driven from actual pool images
        // Deduplicate by container since multiple tier entries share the same infra
        if (pools != null)
        {
            foreach (var secret in pools.DistinctBy(p => p.Pool.Id)
                .SelectMany(pool => TopologyHelpers.CollectPoolSecrets(pool)))
            {
                var length = secret.ResourceName.Contains("access_key") ? 20
                    : secret.ResourceName.Contains("secret_key") || secret.ResourceName.Contains("api_secret") ? 40
                    : 32;
                secrets.Block($"resource \"random_password\" \"{secret.ResourceName}\"", b =>
                {
                    b.Attribute("length", length);
                    b.RawAttribute("special", "false");
                });
                secrets.Line();
            }
        }

        return secrets.ToString();
    }

    protected string GenerateOutputs(
        List<TopologyHelpers.HostEntry> hosts,
        List<TopologyHelpers.ComputePoolEntry> pools,
        List<Container> standaloneCaddies)
    {
        var outputs = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            if (TopologyHelpers.IsReplicatedHost(entry))
            {
                outputs.Block($"output \"{resourceName}_ips\"", b =>
                {
                    b.RawAttribute("value", $"{InstanceResourceType}.{resourceName}[*].{PublicIpField}");
                    b.Attribute("description", $"Public IPs of {entry.Host.Name} instances");
                });
                outputs.Line();
                outputs.Block($"output \"{resourceName}_private_ips\"", b =>
                {
                    b.RawAttribute("value", $"{InstanceResourceType}.{resourceName}[*].{PrivateIpField}");
                    b.Attribute("description", $"Private IPs of {entry.Host.Name} instances");
                });
            }
            else
            {
                outputs.Block($"output \"{resourceName}_ip\"", b =>
                {
                    b.RawAttribute("value", $"{InstanceResourceType}.{resourceName}.{PublicIpField}");
                    b.Attribute("description", $"Public IP of {entry.Host.Name}");
                });
            }
            outputs.Line();
        }

        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            outputs.Block($"output \"{poolName}_ips\"", b =>
            {
                b.RawAttribute("value", $"{InstanceResourceType}.{poolName}[*].{PublicIpField}");
                b.Attribute("description", $"Public IPs of {pool.TierProfile.Name}");
            });
            outputs.Line();
        }

        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            outputs.Block($"output \"{resourceName}_ip\"", b =>
            {
                b.RawAttribute("value", $"{InstanceResourceType}.{resourceName}.{PublicIpField}");
                b.Attribute("description", $"Public IP of {caddy.Name}");
            });
            outputs.Line();
        }

        // Elastic image outputs (replicas > 1, broken out into own instances)
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            outputs.Block($"output \"{resourceName}_ips\"", b =>
            {
                b.RawAttribute("value", $"{InstanceResourceType}.{resourceName}[*].{PublicIpField}");
                b.Attribute("description", $"Public IPs of {image.Name} instances");
            });
            outputs.Line();
        }

        return outputs.ToString();
    }

    /// <summary>
    /// Get the Terraform public IP reference for a host based on which provider owns it.
    /// </summary>
    internal static string GetIpReference(string hostName, string providerKey, bool isReplicated)
    {
        if (string.Equals(providerKey, "aws", StringComparison.OrdinalIgnoreCase))
            return isReplicated ? $"aws_instance.{hostName}[0].public_ip" : $"aws_instance.{hostName}.public_ip";

        return isReplicated ? $"linode_instance.{hostName}[0].ip_address" : $"linode_instance.{hostName}.ip_address";
    }

    /// <summary>
    /// Get the Terraform private IP reference for a host based on which provider owns it.
    /// Used for cross-host internal communication (e.g., Hub → PG on a different host).
    /// </summary>
    internal static string GetPrivateIpReference(string hostName, string providerKey, bool isReplicated)
    {
        if (string.Equals(providerKey, "aws", StringComparison.OrdinalIgnoreCase))
            return isReplicated ? $"aws_instance.{hostName}[0].private_ip" : $"aws_instance.{hostName}.private_ip";

        return isReplicated
            ? $"linode_instance.{hostName}[0].private_ip_address"
            : $"linode_instance.{hostName}.private_ip_address";
    }

    /// <summary>
    /// Generates shared pool and service key variable blocks, appended after provider-specific variables.
    /// </summary>
    protected void GeneratePoolAndServiceKeyVariables(
        HclBuilder vars,
        Topology topology,
        List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        GeneratePoolVariables(vars, pools, plans);
        GenerateDataPoolVariables(vars, topology);
        GenerateServiceKeyVariables(vars, topology);
    }

    /// <summary>
    /// Generates Terraform variable blocks for elastic image replica counts.
    /// Called for hosts and standalone caddies that have images with replicas > 1.
    /// </summary>
    internal static void CollectElasticImageVariables(
        List<TopologyHelpers.HostEntry> hosts,
        List<Container> standaloneCaddies,
        HclBuilder vars)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ProcessContainer(Container container)
        {
            var images = TopologyHelpers.CollectImagesExcludingPools(container);
            foreach (var image in images)
            {
                var (min, max) = TopologyHelpers.ParseReplicaRange(image.Config);
                if (min <= 1 && max <= 1) continue;

                var varName = $"{TopologyHelpers.SanitizeName(image.Name)}_replicas";
                if (!seen.Add(varName)) continue;

                vars.Line();
                vars.Block($"variable \"{varName}\"", b =>
                {
                    b.RawAttribute("type", "number");
                    b.Attribute("default", min);
                    b.Attribute("description", $"Number of replicas for {image.Name}");

                    if (min != max)
                    {
                        b.Block("validation", vb =>
                        {
                            vb.RawAttribute("condition", $"var.{varName} >= {min} && var.{varName} <= {max}");
                            vb.Attribute("error_message", $"Replicas must be between {min} and {max}.");
                        });
                    }
                });
            }
        }

        foreach (var entry in hosts)
            ProcessContainer(entry.Host);
        foreach (var caddy in standaloneCaddies)
            ProcessContainer(caddy);
    }

    internal static void GeneratePoolVariables(
        HclBuilder vars,
        List<TopologyHelpers.ComputePoolEntry> pools,
        List<ComputePlan> plans)
    {
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            var selectedPlan = ResolvePoolPlan(pool, plans);
            var poolImages = TopologyHelpers.CollectImages(pool.Pool);
            var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(selectedPlan.MemoryMb, pool.TierProfile, poolImages);

            vars.Line();
            vars.Block($"variable \"{poolName}_host_count\"", b =>
            {
                b.RawAttribute("type", "number");
                b.Attribute("default", 0);
                b.Attribute("description", $"Number of compute hosts for {pool.TierProfile.Name}");
            });
            vars.Line();
            vars.Block($"variable \"{poolName}_tenants_per_host\"", b =>
            {
                b.RawAttribute("type", "number");
                b.Attribute("default", tenantsPerHost > 0 ? tenantsPerHost : 1);
                b.Attribute("description", $"Number of tenants per host for {pool.TierProfile.Name}");
            });
        }
    }

    internal static void GenerateDataPoolVariables(
        HclBuilder vars,
        Topology topology)
    {
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        foreach (var entry in hosts)
        {
            if (entry.Host.Kind != ContainerKind.DataPool) continue;
            var poolName = TopologyHelpers.SanitizeName(entry.Host.Name);

            vars.Line();
            vars.Block($"variable \"{poolName}_count\"", b =>
            {
                b.RawAttribute("type", "number");
                b.Attribute("default", 0);
                b.Attribute("description", $"Number of data pool instances for '{entry.Host.Name}' (0 = deferred)");
            });
        }
    }

    internal static void GenerateServiceKeyVariables(
        HclBuilder vars,
        Topology topology)
    {
        // Group service keys by prefix (e.g., "registry_", "smtp_", "stripe_", "tenor_").
        // If ANY key in a group is present in topology.ServiceKeys, emit ALL keys in that group.
        // This ensures sensitive keys stored in the credential store (not in ServiceKeys)
        // still get Terraform variables emitted.
        var schema = ServiceKeySchema.GetSchema();
        var groups = schema.GroupBy(f => f.Key.Split('_')[0]).ToList();

        foreach (var group in groups)
        {
            // Registry variables are hardcoded per-provider with ResolveRegistry() defaults — skip here
            if (string.Equals(group.Key, "registry", StringComparison.OrdinalIgnoreCase)) continue;

            var anyPresent = group.Any(f => topology.ServiceKeys.ContainsKey(f.Key));
            if (!anyPresent) continue;

            foreach (var field in group)
            {
                vars.Line();
                vars.Block($"variable \"{field.Key}\"", b =>
                {
                    b.RawAttribute("type", "string");
                    if (field.Sensitive)
                        b.RawAttribute("sensitive", "true");
                    b.Attribute("description", field.Label);
                    b.Attribute("default", "");
                });
            }
        }
    }
}
