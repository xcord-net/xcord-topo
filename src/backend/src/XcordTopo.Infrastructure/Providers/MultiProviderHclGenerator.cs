using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Coordinates HCL generation across multiple cloud providers.
/// Single-provider topologies delegate to the existing GenerateHcl path.
/// Multi-provider topologies call each provider's GenerateHclForContainers and merge results.
/// Generates unified main.tf, variables.tf, and secrets.tf to prevent duplicates.
/// </summary>
public sealed class MultiProviderHclGenerator(ProviderRegistry registry, ImagePluginRegistry imagePluginRegistry)
{
    private static readonly HashSet<string> UnifiedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "main.tf", "variables.tf", "secrets.tf"
    };

    private static readonly string[] UnifiedFilePrefixes = ["main_", "variables_", "secrets_"];

    public Dictionary<string, string> Generate(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var activeKeys = TopologyHelpers.CollectActiveProviderKeys(topology);

        // Single provider - delegate to existing path for zero-risk backward compat
        if (activeKeys.Count <= 1)
        {
            var provider = registry.Get(topology.Provider);
            if (provider == null)
                throw new InvalidOperationException($"Provider '{topology.Provider}' is not registered");
            return provider.GenerateHcl(topology, poolSelections, infraSelections);
        }

        // Multi-provider - build deployment units and group by provider key
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

            // Deduplicate containers - multiple units can reference the same container
            var containers = group
                .Select(u => u.Container)
                .Where(c => c != null)
                .Distinct()
                .Cast<Container>()
                .ToList();

            var files = provider.GenerateHclForContainers(topology, containers, poolSelections, infraSelections);
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
        foreach (var key in mergedFiles.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList())
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
                    p.MapBlock("linode", lp =>
                    {
                        lp.Attribute("source", "linode/linode");
                        lp.Attribute("version", "~> 2.0");
                    });
                }

                if (activeKeys.Contains("aws"))
                {
                    p.MapBlock("aws", ap =>
                    {
                        ap.Attribute("source", "hashicorp/aws");
                        ap.Attribute("version", "~> 5.0");
                    });
                }

                p.MapBlock("random", rp =>
                {
                    rp.Attribute("source", "hashicorp/random");
                    rp.Attribute("version", "~> 3.0");
                });
                p.MapBlock("tls", tp =>
                {
                    tp.Attribute("source", "hashicorp/tls");
                    tp.Attribute("version", "~> 4.0");
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
                b.RawAttribute("region", "var.aws_region");
            });
            main.Line();
        }

        main.Block("resource \"tls_private_key\" \"deploy\"", b =>
        {
            b.Attribute("algorithm", "ED25519");
        });
        main.Line();

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
            vars.Block("variable \"ssh_cidr_blocks\"", b =>
            {
                b.RawAttribute("type", "list(string)");
                b.RawAttribute("default", "[]");
                b.Attribute("description", "CIDR blocks allowed for SSH access (empty = allow all, required for provisioners)");
            });
            vars.Line();
        }

        // Per-provider region variables (namespaced to avoid tfvars collision)
        if (activeKeys.Contains("aws"))
        {
            vars.Block("variable \"aws_region\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("aws_region", "us-east-1"));
                b.Attribute("description", "AWS region");
            });
            vars.Line();
        }

        if (activeKeys.Contains("linode"))
        {
            vars.Block("variable \"linode_region\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("linode_region", "us-east"));
                b.Attribute("description", "Linode region");
            });
            vars.Line();
        }

        vars.Block("variable \"domain\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Primary domain name");
        });
        vars.Line();

        // Registry variables (hardcoded here like per-provider paths, with resolved default)
        vars.Line();
        vars.Block("variable \"registry_url\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", TopologyHelpers.ResolveRegistry(topology));
            b.Attribute("description", "Docker registry URL for pulling xcord images");
        });
        vars.Line();
        vars.Block("variable \"hub_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Version tag for hub server image (e.g. v0.1.5)");
        });
        vars.Line();
        vars.Block("variable \"fed_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Version tag for federation server image (e.g. v0.1.5)");
        });
        vars.Line();
        vars.Block("variable \"deploy_apps\"", b =>
        {
            b.RawAttribute("type", "bool");
            b.RawAttribute("default", "false");
            b.Attribute("description", "Set to true after images are pushed to deploy application containers");
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

        // Pool variables - group by provider for correct plan resolution
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

        // Data pool variables (deferred - count defaults to 0)
        ProviderHclBase.GenerateDataPoolVariables(vars, topology);

        // Service key variables (emitted once, not per-provider)
        ProviderHclBase.GenerateServiceKeyVariables(vars, topology);

        return vars.ToString();
    }

    /// <summary>
    /// Builds a structured resource summary from the same topology data used for HCL generation.
    /// This is the source of truth for the review tab - computed during generation, not parsed from HCL.
    /// </summary>
    public ResourceSummary BuildResourceSummary(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var resources = new List<ResourceEntry>();
        var total = 0m;

        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology, imagePluginRegistry);

        // Infrastructure hosts (excludes DataPool - those are pools, not infra)
        foreach (var entry in hosts)
        {
            if (entry.Host.Kind == ContainerKind.DataPool) continue;

            var providerKey = TopologyHelpers.ResolveProviderKey(entry.Host, topology);
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host, imagePluginRegistry);
            var plan = ResolveInfraPlan(entry.Host.Name, plans, ramRequired, infraSelections);
            var (min, _) = TopologyHelpers.ParseHostReplicas(entry.Host);
            var count = min ?? 1;
            var lineTotal = plan.PriceMonthly * count;
            var services = BuildServiceDetails(entry.Host, resolver);

            resources.Add(new ResourceEntry(
                entry.Host.Name, providerKey, plan.Id, plan.Label, plan.MemoryMb,
                count, lineTotal, IsPool: false, Services: services));
            total += lineTotal;
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var providerKey = TopologyHelpers.ResolveProviderKey(caddy, topology);
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var ramRequired = TopologyHelpers.CalculateStandaloneCaddyRam(caddy, imagePluginRegistry);
            var plan = ResolveInfraPlan(caddy.Name, plans, ramRequired, infraSelections);
            var lineTotal = plan.PriceMonthly;

            var services = BuildServiceDetails(caddy, resolver);

            resources.Add(new ResourceEntry(
                caddy.Name, providerKey, plan.Id, plan.Label, plan.MemoryMb,
                1, lineTotal, IsPool: false, Services: services));
            total += lineTotal;
        }

        // Elastic images (replicas > 1)
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            // Find the provider for this elastic image by looking at which host/caddy owns it
            var providerKey = topology.Provider;
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var desc = imagePluginRegistry.GetDescriptor(image);
            var ramRequired = desc?.MinRamMb ?? 256;
            var plan = ResolveInfraPlan(image.Name, plans, ramRequired, infraSelections);
            var (min, _) = TopologyHelpers.ParseReplicaRange(image.Config);
            var lineTotal = plan.PriceMonthly * min;

            resources.Add(new ResourceEntry(
                image.Name, providerKey, plan.Id, plan.Label, plan.MemoryMb,
                min, lineTotal, IsPool: false,
                Services: [new ServiceDetail(image.Name, image.ResolveTypeId(), desc?.MinRamMb ?? 256)]));
            total += lineTotal;
        }

        // Compute pools - one entry per tier
        foreach (var pool in pools)
        {
            var providerKey = TopologyHelpers.ResolveProviderKey(pool.Pool, topology);
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var selectedPlan = ProviderHclBase.ResolvePoolPlan(pool, plans);
            var poolImages = TopologyHelpers.CollectImages(pool.Pool);
            var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(
                selectedPlan.MemoryMb, pool.TierProfile, poolImages, imagePluginRegistry);

            resources.Add(new ResourceEntry(
                pool.ResourceName, providerKey, selectedPlan.Id, selectedPlan.Label,
                selectedPlan.MemoryMb, 0, selectedPlan.PriceMonthly,
                IsPool: true, TierProfileName: pool.TierProfile.Name,
                TenantsPerHost: tenantsPerHost > 0 ? tenantsPerHost : 1));
            // Pool cost is 0 initially (count=0), not added to total
        }

        // Data pools - deferred pool instances for shared data services (PG, Redis, MinIO, etc.)
        foreach (var entry in hosts)
        {
            if (entry.Host.Kind != ContainerKind.DataPool) continue;

            var providerKey = TopologyHelpers.ResolveProviderKey(entry.Host, topology);
            var provider = registry.Get(providerKey);
            if (provider is null) continue;

            var plans = provider.GetPlans().OrderBy(p => p.PriceMonthly).ToList();
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host, imagePluginRegistry);
            var plan = ResolveInfraPlan(entry.Host.Name, plans, ramRequired, infraSelections);
            var services = BuildServiceDetails(entry.Host, resolver);

            resources.Add(new ResourceEntry(
                entry.Host.Name, providerKey, plan.Id, plan.Label, plan.MemoryMb,
                0, plan.PriceMonthly, IsPool: true, Services: services));
        }

        // Public endpoints
        var rawEndpoints = TopologyHelpers.CollectPublicEndpoints(topology, imagePluginRegistry);
        var endpoints = rawEndpoints
            .Select(e => new PublicEndpoint(e.Url, e.Kind, e.Backend))
            .ToList();

        return new ResourceSummary(resources, endpoints, total);
    }

    /// <summary>
    /// Resolves the plan for an infrastructure resource, using the user's selection if available,
    /// otherwise auto-selecting the cheapest plan that meets the RAM requirement.
    /// </summary>
    private static ComputePlan ResolveInfraPlan(
        string name, List<ComputePlan> plans, int ramRequired,
        List<TopologyHelpers.InfraSelection>? infraSelections)
    {
        var selection = infraSelections?.FirstOrDefault(
            s => string.Equals(s.ImageName, name, StringComparison.OrdinalIgnoreCase));
        if (selection is not null)
        {
            var selected = plans.FirstOrDefault(p => p.Id == selection.PlanId);
            if (selected is not null) return selected;
        }
        return plans.FirstOrDefault(p => p.MemoryMb >= ramRequired) ?? plans.Last();
    }

    private List<ServiceDetail> BuildServiceDetails(Container container, WireResolver resolver)
    {
        var services = new List<ServiceDetail>();
        var images = TopologyHelpers.CollectImagesExcludingPools(container);
        foreach (var image in images)
        {
            var desc = imagePluginRegistry.GetDescriptor(image);
            services.Add(new ServiceDetail(image.Name, image.ResolveTypeId(), desc?.MinRamMb ?? 256));
        }
        // Include Caddy itself if container is a Caddy or contains one
        if (container.Kind == ContainerKind.Caddy)
            services.Add(new ServiceDetail("caddy", "Caddy", ImageOperationalMetadata.Caddy.MinRamMb));
        return services;
    }

    private string GenerateUnifiedSecrets(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections)
    {
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology, imagePluginRegistry);
        return ProviderHclBase.GenerateSecrets(hosts, resolver, imagePluginRegistry, pools, standaloneCaddies);
    }
}
