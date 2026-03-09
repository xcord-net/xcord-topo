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
        Topology topology, List<TopologyHelpers.PoolSelection>? poolSelections = null);
    public abstract Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null);

    // --- Shared methods (identical across providers) ---

    internal string SelectPlan(int requiredRamMb)
    {
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var plan in plans)
        {
            if (plan.MemoryMb >= requiredRamMb)
                return plan.Id;
        }
        return plans.Last().Id;
    }

    protected static ComputePlan ResolvePoolPlan(TopologyHelpers.ComputePoolEntry pool, List<ComputePlan> plans)
    {
        if (pool.SelectedPlanId is not null)
        {
            var selected = plans.FirstOrDefault(p => p.Id == pool.SelectedPlanId);
            if (selected is not null) return selected;
        }

        var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
        var sharedOverhead = ImageOperationalMetadata.CalculateSharedOverheadMb();
        var minHostRam = sharedOverhead + fedMemory;
        return plans.FirstOrDefault(p => p.MemoryMb >= minHostRam) ?? plans.Last();
    }

    protected static void CollectHostReplicaVariables(List<TopologyHelpers.HostEntry> hosts, HclBuilder vars)
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
                b.Attribute("type", "number");
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

    protected static string GenerateSecrets(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver)
    {
        var secrets = new HclBuilder();
        foreach (var entry in hosts)
        {
            var allSecrets = TopologyHelpers.CollectSecrets(entry, resolver);
            foreach (var secret in allSecrets)
            {
                secrets.Block($"resource \"random_password\" \"{secret.ResourceName}\"", b =>
                {
                    b.Attribute("length", 32);
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
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            outputs.Block($"output \"{poolName}_ips\"", b =>
            {
                b.RawAttribute("value", $"{InstanceResourceType}.{poolName}[*].{PublicIpField}");
                b.Attribute("description", $"Public IPs of compute pool '{pool.Pool.Name}'");
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

        return outputs.ToString();
    }

    /// <summary>
    /// Get the Terraform IP reference for a host based on which provider owns it.
    /// </summary>
    internal static string GetIpReference(string hostName, string providerKey, bool isReplicated)
    {
        if (string.Equals(providerKey, "aws", StringComparison.OrdinalIgnoreCase))
            return isReplicated ? $"aws_instance.{hostName}[0].public_ip" : $"aws_instance.{hostName}.public_ip";

        return isReplicated ? $"linode_instance.{hostName}[0].ip_address" : $"linode_instance.{hostName}.ip_address";
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
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var selectedPlan = ResolvePoolPlan(pool, plans);
            var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(selectedPlan.MemoryMb, pool.TierProfile);
            var hostsRequired = ImageOperationalMetadata.CalculateHostsRequired(pool.TargetTenants, tenantsPerHost);

            vars.Line();
            vars.Block($"variable \"{poolName}_host_count\"", b =>
            {
                b.Attribute("type", "number");
                b.Attribute("default", hostsRequired);
                b.Attribute("description", $"Number of compute hosts for pool '{pool.Pool.Name}' ({pool.TierProfile.Name}, {pool.TargetTenants} tenants)");
            });
            vars.Line();
            vars.Block($"variable \"{poolName}_tenants_per_host\"", b =>
            {
                b.Attribute("type", "number");
                b.Attribute("default", tenantsPerHost > 0 ? tenantsPerHost : 1);
                b.Attribute("description", $"Number of tenants per host in pool '{pool.Pool.Name}'");
            });
        }

        foreach (var field in ServiceKeySchema.GetSchema())
        {
            if (!topology.ServiceKeys.ContainsKey(field.Key)) continue;

            vars.Line();
            vars.Block($"variable \"{field.Key}\"", b =>
            {
                b.Attribute("type", "string");
                if (field.Sensitive)
                    b.RawAttribute("sensitive", "true");
                b.Attribute("description", field.Label);
                b.Attribute("default", "");
            });
        }
    }
}
