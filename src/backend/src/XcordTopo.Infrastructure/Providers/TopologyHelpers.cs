using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Shared topology tree-walking helpers used by all cloud providers.
/// Extracted from LinodeProvider to break cross-provider coupling.
/// </summary>
public static class TopologyHelpers
{
    // --- Records ---

    public record HostEntry(Container Host);
    public record ComputePoolEntry(Container Pool, TierProfile TierProfile, int TargetTenants, string? SelectedPlanId = null)
    {
        /// <summary>
        /// Tier-qualified resource name: "compute_pool_free", "compute_pool_pro", etc.
        /// </summary>
        public string ResourceName => SanitizeName(Pool.Name) + "_" + SanitizeName(TierProfile.Id);
    }
    public record PoolSelection(string PoolName, string PlanId, int TargetTenants, string? TierProfileId = null);
    public record InfraSelection(string ImageName, string PlanId);
    public record SecretEntry(string ResourceName, string Description, int Length = 32);

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

    // --- Replica helpers ---

    public static bool IsVariableRef(string value) =>
        value.StartsWith('$') && value.Length > 1 && !value.Contains(' ');

    public static (int? Literal, string? VarRef) ParseHostReplicas(Container host)
    {
        var replicas = host.Config.GetValueOrDefault("replicas", "1");
        if (IsVariableRef(replicas))
            return (null, replicas[1..]);
        return (int.TryParse(replicas, out var n) ? n : 1, null);
    }

    public static (int Min, int Max) ParseReplicaRange(Dictionary<string, string> config)
    {
        var replicas = config.GetValueOrDefault("replicas", "1");
        if (replicas.Contains('-'))
        {
            var parts = replicas.Split('-', 2);
            var min = int.TryParse(parts[0], out var lo) ? lo : 1;
            var max = int.TryParse(parts[1], out var hi) ? hi : min;
            return (min, max);
        }
        var n = int.TryParse(replicas, out var v) ? v : 1;
        var minR = config.TryGetValue("minReplicas", out var minStr) && int.TryParse(minStr, out var minVal) ? minVal : n;
        var maxR = config.TryGetValue("maxReplicas", out var maxStr) && int.TryParse(maxStr, out var maxVal) ? maxVal : n;
        return (Math.Min(minR, maxR), Math.Max(minR, maxR));
    }

    public static bool IsReplicatedHost(HostEntry entry)
    {
        // DataPool hosts use a count variable (deferred deployment), so they're always "replicated" for indexing
        if (entry.Host.Kind == ContainerKind.DataPool)
            return true;
        var (literal, varRef) = ParseHostReplicas(entry.Host);
        return varRef != null || (literal.HasValue && literal.Value > 1);
    }

    /// <summary>
    /// Get the Terraform count expression for a replicated host.
    /// Host-replicated hosts use a literal or variable reference.
    /// Returns null for non-replicated hosts.
    /// </summary>
    public static string? GetHostCountExpression(HostEntry entry)
    {
        // DataPool instances are deferred - controlled by a count variable defaulting to 0
        if (entry.Host.Kind == ContainerKind.DataPool)
            return $"var.{SanitizeName(entry.Host.Name)}_count";

        var (literal, varRef) = ParseHostReplicas(entry.Host);
        if (varRef != null)
            return $"var.{SanitizeName(varRef)}";
        if (literal.HasValue && literal.Value > 1)
        {
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");
            if (hasMinMax)
                return $"var.{SanitizeName(entry.Host.Name)}_replicas";
            return literal.Value.ToString();
        }
        return null;
    }

    // --- Compute plan auto-selection ---

    public static int CalculateHostRam(Container host) =>
        CalculateHostRam(host, DefaultPlugins.CreateRegistry());

    public static int CalculateHostRam(Container host, ImagePluginRegistry registry)
    {
        var totalRam = 0;
        var images = CollectImages(host);
        foreach (var image in images)
        {
            var desc = registry.GetDescriptor(image);
            totalRam += desc?.MinRamMb ?? 256;
        }
        var caddies = CollectCaddyContainers(host);
        if (caddies.Count > 0)
            totalRam += ImageOperationalMetadata.Caddy.MinRamMb;

        return totalRam;
    }

    /// <summary>
    /// Calculate RAM for a standalone Caddy container, excluding elastic images that break
    /// out into their own instances. Includes Caddy overhead + non-elastic co-located images.
    /// </summary>
    public static int CalculateStandaloneCaddyRam(Container caddy) =>
        CalculateStandaloneCaddyRam(caddy, DefaultPlugins.CreateRegistry());

    public static int CalculateStandaloneCaddyRam(Container caddy, ImagePluginRegistry registry)
    {
        var totalRam = ImageOperationalMetadata.Caddy.MinRamMb;
        var images = CollectImagesExcludingPools(caddy);
        foreach (var image in images)
        {
            var (min, max) = ParseReplicaRange(image.Config);
            if (min > 1 || max > 1) continue; // Elastic - gets its own instance

            var desc = registry.GetDescriptor(image);
            totalRam += desc?.MinRamMb ?? 256;
        }
        return totalRam;
    }

    /// <summary>
    /// Collect elastic images (replicas > 1) from hosts and standalone Caddies,
    /// excluding images inside ComputePool subtrees.
    /// </summary>
    public static List<Image> CollectElasticImages(
        List<HostEntry> hosts,
        List<Container> standaloneCaddies)
    {
        var result = new List<Image>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Process(Container container)
        {
            var images = CollectImagesExcludingPools(container);
            foreach (var image in images)
            {
                var (min, max) = ParseReplicaRange(image.Config);
                if (min <= 1 && max <= 1) continue;

                var name = SanitizeName(image.Name);
                if (seen.Add(name))
                    result.Add(image);
            }
        }

        foreach (var entry in hosts) Process(entry.Host);
        foreach (var caddy in standaloneCaddies) Process(caddy);
        return result;
    }

    // --- Secret helpers ---

    public static List<SecretEntry> CollectSecrets(
        HostEntry entry, WireResolver resolver, bool excludePools = false) =>
        CollectSecrets(entry, resolver, DefaultPlugins.CreateRegistry(), excludePools);

    public static List<SecretEntry> CollectSecrets(
        HostEntry entry, WireResolver resolver, ImagePluginRegistry registry, bool excludePools = false)
    {
        var secrets = new List<SecretEntry>();
        var hostName = SanitizeName(entry.Host.Name);
        var images = excludePools
            ? CollectImagesExcludingPools(entry.Host)
            : CollectImages(entry.Host);

        foreach (var image in images)
        {
            var plugin = registry.GetForImage(image);
            if (plugin == null) continue;
            var imgName = SanitizeName(image.Name);
            foreach (var secret in plugin.GetSecrets())
            {
                secrets.Add(new($"{hostName}_{imgName}_{secret.Name}", $"{secret.Description} for {image.Name}", secret.Length));
            }
        }
        return secrets;
    }

    /// <summary>
    /// Collect secrets needed for a compute pool's images (data-driven, not hardcoded).
    /// </summary>
    public static List<SecretEntry> CollectPoolSecrets(ComputePoolEntry pool) =>
        CollectPoolSecrets(pool, DefaultPlugins.CreateRegistry());

    public static List<SecretEntry> CollectPoolSecrets(ComputePoolEntry pool, ImagePluginRegistry registry)
    {
        var secrets = new List<SecretEntry>();
        var poolName = SanitizeName(pool.Pool.Name);
        foreach (var image in pool.Pool.Images)
        {
            var plugin = registry.GetForImage(image);
            if (plugin == null) continue;
            var imgName = SanitizeName(image.Name);
            foreach (var secret in plugin.GetSecrets())
            {
                secrets.Add(new($"{poolName}_{imgName}_{secret.Name}", $"{secret.Description} for {image.Name}", secret.Length));
            }
        }
        return secrets;
    }

    /// <summary>
    /// Generate a docker service create command for a pool image.
    /// Returns null if the image kind has no metadata or docker image.
    /// </summary>
    public static string? GenerateSwarmServiceCommand(
        Image image, string poolName, WireResolver resolver, bool useSudo,
        IReadOnlyList<Image>? poolImages = null) =>
        GenerateSwarmServiceCommand(image, poolName, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine(), useSudo, poolImages);

    public static string? GenerateSwarmServiceCommand(
        Image image, string poolName, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine, bool useSudo,
        IReadOnlyList<Image>? poolImages = null)
    {
        if (image.Scaling == ImageScaling.PerTenant)
            return null;

        var plugin = registry.GetForImage(image);
        if (plugin == null) return null;
        var desc = plugin.GetDescriptor();

        var dockerImage = GetDockerImageForHcl(image, "", registry);
        if (string.IsNullOrEmpty(dockerImage))
            return null;

        var imgName = SanitizeName(image.Name);
        var sudo = useSudo ? "sudo " : "";
        var parts = new List<string>
        {
            $"{sudo}docker service create",
            $"--name shared-{imgName}",
            "--replicas 1",
            "--network xcord-pool"
        };

        if (desc.MountPath != null)
        {
            var volumeName = $"{imgName}data";
            parts.Add($"--mount type=volume,source={volumeName},target={desc.MountPath}");
        }

        // Use template engine for env vars
        var templates = plugin.GetEnvVarTemplates();
        var templateContext = new TemplateContext
        {
            HostName = poolName,
            ImageName = imgName,
            ResolveWire = portName =>
            {
                var target = resolver.ResolveWiredImage(image.Id, portName);
                if (target == null) return null;
                var targetName = SanitizeName(target.Name);
                var targetDesc = registry.GetDescriptor(target);
                var port = targetDesc?.Ports.FirstOrDefault()?.Port ?? 80;
                var secretRef = $"${{nonsensitive(random_password.{poolName}_{targetName}_password.result)}}";
                return ($"shared-{targetName}", port, secretRef);
            },
            ImageConfig = image.Config,
            DerivedDbName = DeriveDbName(image, resolver, registry)
        };

        if (plugin.HasCustomEnvVarBuilder)
        {
            var context = new EnvVarContext(
                HostName: poolName,
                ImageName: imgName,
                SecretRef: secretName =>
                    $"${{nonsensitive(random_password.{poolName}_{imgName}_{secretName}.result)}}",
                ResolveWire: portName =>
                {
                    var target = resolver.ResolveWiredImage(image.Id, portName);
                    if (target == null) return null;
                    var targetName = SanitizeName(target.Name);
                    var targetDesc = registry.GetDescriptor(target);
                    var port = targetDesc?.Ports.FirstOrDefault()?.Port ?? 80;
                    var secretRef = $"${{nonsensitive(random_password.{poolName}_{targetName}_password.result)}}";
                    return new WireResolution($"shared-{targetName}", port, secretRef);
                },
                ServiceKeys: new Dictionary<string, string>(),
                ServiceKeyRef: _ => null,
                BaseDomain: null);

            foreach (var e in plugin.BuildEnvVars(context))
                parts.Add($"-e {e.Key}={e.Value}");
        }
        else
        {
            foreach (var template in templates)
            {
                var value = templateEngine.Resolve(template.ValueTemplate, templateContext);
                parts.Add($"-e {template.Key}={value}");
            }
        }

        parts.Add(dockerImage);

        // Command override
        var cmdOverride = plugin.GetCommandOverride();
        if (cmdOverride != null)
        {
            var resolvedCmd = templateEngine.Resolve(cmdOverride, templateContext);
            parts.Add(resolvedCmd);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Derive DB name from the consumer wired to this PG image.
    /// HubServer -> xcord_hub, FederationServer -> xcord, otherwise -> app
    /// </summary>
    public static string DeriveDbName(Image pgImage, WireResolver resolver) =>
        DeriveDbName(pgImage, resolver, DefaultPlugins.CreateRegistry());

    public static string DeriveDbName(Image pgImage, WireResolver resolver, ImagePluginRegistry registry)
    {
        var incoming = resolver.ResolveIncoming(pgImage.Id, "postgres");
        foreach (var (node, _) in incoming)
        {
            if (node is Image consumerImage)
            {
                var consumerPlugin = registry.GetForImage(consumerImage);
                var dbName = consumerPlugin?.GetDockerBehavior().DbNameWhenConsuming;
                if (!string.IsNullOrEmpty(dbName))
                    return dbName;
            }
        }
        return "app";
    }

    // --- Environment variable building ---

    public static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver, Topology? topology = null,
        Container? resolveFrom = null,
        Dictionary<Guid, Dictionary<int, int>>? portAssignments = null) =>
        BuildEnvVars(image, entry, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine(), topology, resolveFrom, portAssignments);

    public static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine,
        Topology? topology = null,
        Container? resolveFrom = null,
        Dictionary<Guid, Dictionary<int, int>>? portAssignments = null)
    {
        var plugin = registry.GetForImage(image);
        if (plugin == null) return [];

        var hostName = SanitizeName(entry.Host.Name);
        var imgName = SanitizeName(image.Name);
        var sourceHost = resolveFrom ?? entry.Host;

        if (plugin.HasCustomEnvVarBuilder)
        {
            var context = new EnvVarContext(
                HostName: hostName,
                ImageName: imgName,
                SecretRef: secretName =>
                    $"${{nonsensitive(random_password.{hostName}_{imgName}_{secretName}.result)}}",
                ResolveWire: portName =>
                {
                    var target = resolver.ResolveWiredImage(image.Id, portName);
                    if (target == null) return null;
                    var targetName = SanitizeName(target.Name);
                    var targetHost = resolver.FindHostFor(target.Id);
                    var targetHostName = targetHost != null ? SanitizeName(targetHost.Name) : hostName;
                    var host = topology != null
                        ? ResolveServiceHost(target, sourceHost, resolver, topology)
                        : targetName;
                    var desc = registry.GetDescriptor(target);
                    var port = desc?.Ports.FirstOrDefault()?.Port ?? 80;
                    var resolvedPort = ResolveServicePort(target, sourceHost, port, resolver, portAssignments);
                    var secretRef = $"${{nonsensitive(random_password.{targetHostName}_{targetName}_password.result)}}";
                    return new WireResolution(host, resolvedPort, secretRef);
                },
                ServiceKeys: topology?.ServiceKeys ?? new Dictionary<string, string>(),
                ServiceKeyRef: serviceKey =>
                {
                    if (topology == null) return null;
                    var prefix = serviceKey.Split('_')[0];
                    var schema = ServiceKeySchema.GetSchema(topology);
                    var field = schema.FirstOrDefault(f => f.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
                    var groupHasAnyKey = schema
                        .Where(f => f.Key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) || f.Key == prefix)
                        .Any(f => topology.ServiceKeys.ContainsKey(f.Key));
                    if (!groupHasAnyKey) return null;
                    return field?.Sensitive == true
                        ? $"${{nonsensitive(var.{serviceKey})}}"
                        : $"${{var.{serviceKey}}}";
                },
                BaseDomain: topology?.ServiceKeys.ContainsKey("hub_base_domain") == true
                    ? "${var.hub_base_domain}" : null);

            return plugin.BuildEnvVars(context)
                .Select(e => (e.Key, e.Value))
                .ToList();
        }

        // Declarative plugins: resolve templates
        var templates = plugin.GetEnvVarTemplates();
        if (templates.Count == 0) return [];

        var dbName = DeriveDbName(image, resolver, registry);
        var templateContext = new TemplateContext
        {
            HostName = hostName,
            ImageName = imgName,
            ResolveWire = portName =>
            {
                var target = resolver.ResolveWiredImage(image.Id, portName);
                if (target == null) return null;
                var targetName = SanitizeName(target.Name);
                var targetHost = resolver.FindHostFor(target.Id);
                var targetHostName = targetHost != null ? SanitizeName(targetHost.Name) : hostName;
                var host = topology != null
                    ? ResolveServiceHost(target, sourceHost, resolver, topology)
                    : targetName;
                var desc = registry.GetDescriptor(target);
                var port = desc?.Ports.FirstOrDefault()?.Port ?? 80;
                var resolvedPort = ResolveServicePort(target, sourceHost, port, resolver, portAssignments);
                var secretRef = $"${{nonsensitive(random_password.{targetHostName}_{targetName}_password.result)}}";
                return (host, resolvedPort, secretRef);
            },
            Registry = topology != null ? ResolveRegistry(topology) : null,
            ImageConfig = image.Config,
            DerivedDbName = dbName
        };

        return templates
            .Select(t => (t.Key, templateEngine.Resolve(t.ValueTemplate, templateContext)))
            .ToList();
    }


    private static void AddServiceKeyEnvVar(
        List<(string Key, string Value)> envVars,
        Topology topology,
        string serviceKey,
        string envVarName)
    {
        // Always reference the Terraform variable - even keys not in topology.ServiceKeys
        // (e.g., smtp_password stored in credential store) get declared as variables
        // via group-based emission in GenerateServiceKeyVariables.
        // If no keys from the group are defined at all, skip entirely.
        var prefix = serviceKey.Split('_')[0];
        var schema = ServiceKeySchema.GetSchema(topology);
        var field = schema.FirstOrDefault(f => f.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
        var groupHasAnyKey = schema
            .Where(f => f.Key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) || f.Key == prefix)
            .Any(f => topology.ServiceKeys.ContainsKey(f.Key));

        if (groupHasAnyKey)
        {
            // Wrap sensitive vars with nonsensitive() to prevent Terraform from
            // suppressing all provisioner output when these appear in inline commands.
            var varRef = field?.Sensitive == true
                ? $"${{nonsensitive(var.{serviceKey})}}"
                : $"${{var.{serviceKey}}}";
            envVars.Add((envVarName, varRef));
        }
    }

    public static string? ResolveCommandOverride(Image image, HostEntry entry, WireResolver resolver) =>
        ResolveCommandOverride(image, entry, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine());

    public static string? ResolveCommandOverride(
        Image image, HostEntry entry, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine)
    {
        var plugin = registry.GetForImage(image);
        var cmdTemplate = plugin?.GetCommandOverride();
        if (cmdTemplate == null) return null;

        var hostName = SanitizeName(entry.Host.Name);
        var imgName = SanitizeName(image.Name);
        var templateContext = new TemplateContext
        {
            HostName = hostName,
            ImageName = imgName,
            ImageConfig = image.Config
        };
        return templateEngine.Resolve(cmdTemplate, templateContext);
    }

    // --- Backup ---

    public static List<string> GenerateBackupCommands(List<Image> images, Container host, BackupTarget? backupTarget = null) =>
        GenerateBackupCommands(images, host, DefaultPlugins.CreateRegistry(), new TemplateEngine(), backupTarget);

    public static List<string> GenerateBackupCommands(
        List<Image> images, Container host,
        ImagePluginRegistry registry, TemplateEngine templateEngine,
        BackupTarget? backupTarget = null)
    {
        var commands = new List<string>();
        var scheduleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hourly"] = "0 * * * *",
            ["daily"] = "0 2 * * *",
            ["weekly"] = "0 2 * * 0"
        };

        foreach (var image in images)
        {
            var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
            if (string.IsNullOrEmpty(volumeSize)) continue;

            var frequency = image.Config.GetValueOrDefault("backupFrequency", "");
            if (string.IsNullOrEmpty(frequency))
                frequency = host.Config.GetValueOrDefault("backupFrequency", "");
            if (string.IsNullOrEmpty(frequency)) continue;

            if (!scheduleMap.TryGetValue(frequency, out var schedule)) continue;

            var plugin = registry.GetForImage(image);
            var backupDef = plugin?.GetBackupDefinition();
            if (backupDef == null) continue;

            var retentionStr = image.Config.GetValueOrDefault("backupRetention", "");
            if (string.IsNullOrEmpty(retentionStr))
                retentionStr = host.Config.GetValueOrDefault("backupRetention", "");
            var retention = int.TryParse(retentionStr, out var r) ? r : 7;

            var containerName = SanitizeName(image.Name);
            var backupDir = $"/opt/backups/{containerName}";
            var hostName = SanitizeName(host.Name);

            var templateContext = new TemplateContext
            {
                HostName = hostName,
                ImageName = containerName,
                BackupDir = backupDir,
                ImageConfig = image.Config
            };
            var backupCmd = templateEngine.Resolve(backupDef.CommandTemplate, templateContext);

            var scriptLines = new List<string> { "#!/bin/bash", backupCmd };

            if (backupTarget != null)
            {
                scriptLines.Add("source /opt/backups/.coldstore.env");
                scriptLines.Add($"BACKUP_FILE=$(ls -t {backupDir}/{containerName}_* 2>/dev/null | head -1)");
                scriptLines.Add("if [ -n \"$BACKUP_FILE\" ]; then");
                scriptLines.Add($"  aws s3 cp \"$BACKUP_FILE\" \"s3://${{COLDSTORE_BUCKET}}/host-backups/{hostName}/{containerName}/\" --endpoint-url \"https://${{COLDSTORE_ENDPOINT}}\"");
                scriptLines.Add("fi");
            }

            scriptLines.Add($"find {backupDir} -type f -mtime +{retention} -delete");
            scriptLines.Add($"find {backupDir} -type d -empty -delete");

            var scriptContent = string.Join("\\n", scriptLines);

            commands.Add($"mkdir -p {backupDir}");
            commands.Add($"printf '{scriptContent}\\n' > {backupDir}/backup.sh");
            commands.Add($"chmod +x {backupDir}/backup.sh");
            commands.Add($"(crontab -l 2>/dev/null; echo \\\"{schedule} {backupDir}/backup.sh\\\") | crontab -");
        }

        return commands;
    }

    public static List<string> GenerateColdStoreEnvSetup()
    {
        return
        [
            "apt-get install -y -qq awscli > /dev/null 2>&1",
            "mkdir -p /opt/backups",
            $"printf 'export COLDSTORE_BUCKET=%s\\nexport COLDSTORE_ENDPOINT=%s\\nexport AWS_ACCESS_KEY_ID=%s\\nexport AWS_SECRET_ACCESS_KEY=%s\\n' " +
                $"'${{var.coldstore_bucket}}' '${{var.coldstore_endpoint}}' " +
                $"'${{nonsensitive(var.coldstore_access_key)}}' '${{nonsensitive(var.coldstore_secret_key)}}' > /opt/backups/.coldstore.env",
            "chmod 600 /opt/backups/.coldstore.env"
        ];
    }

    /// <summary>
    /// Returns true if any image under this host has replicas > 1,
    /// meaning Docker Swarm mode is needed for service replication.
    /// </summary>
    public static bool HostNeedsSwarmMode(Container host)
    {
        var images = CollectImages(host);
        return images.Any(img =>
        {
            var (min, max) = ParseReplicaRange(img.Config);
            return min > 1 || max > 1;
        });
    }

    /// <summary>
    /// Get the Swarm replica expression for an image.
    /// Returns a Terraform variable reference for elastic images, "1" for non-elastic.
    /// </summary>
    public static string GetImageReplicaExpression(Image image)
    {
        var (min, max) = ParseReplicaRange(image.Config);
        if (min > 1 || max > 1)
            return $"${{var.{SanitizeName(image.Name)}_replicas}}";
        return "1";
    }

    // --- Cross-host resolution ---

    /// <summary>
    /// Resolves the hostname to use for connecting to a target image from a source host.
    /// Returns the Docker container name if both are on the same host (Docker DNS resolves it).
    /// Returns a Terraform private IP reference if on different hosts (cross-host communication).
    /// </summary>
    public static string ResolveServiceHost(
        Image targetImage, Container sourceHost, WireResolver resolver, Topology topology)
    {
        var targetHost = resolver.FindHostFor(targetImage.Id);

        if (targetHost != null)
        {
            if (targetHost.Id == sourceHost.Id)
                return SanitizeName(targetImage.Name); // Same host - Docker DNS

            var targetHostName = SanitizeName(targetHost.Name);
            var providerKey = ResolveProviderKey(targetHost, topology);
            var isReplicated = IsReplicatedHost(new HostEntry(targetHost));
            return $"${{{ProviderHclBase.GetPrivateIpReference(targetHostName, providerKey, isReplicated)}}}";
        }

        // Target has no Host ancestor - check for standalone Caddy
        var targetCaddy = resolver.FindCaddyFor(targetImage.Id);
        if (targetCaddy != null && targetCaddy.Id != sourceHost.Id)
        {
            // Target is on Caddy's instance, source is elsewhere - use Caddy's IP
            var caddyName = SanitizeName(targetCaddy.Name);
            var providerKey = ResolveProviderKey(targetCaddy, topology);
            return $"${{{ProviderHclBase.GetPrivateIpReference(caddyName, providerKey, false)}}}";
        }

        // Same container or both standalone - Docker DNS
        return SanitizeName(targetImage.Name);
    }

    /// <summary>
    /// Resolves the hostname for a Caddy upstream target image.
    /// Same as ResolveServiceHost but for Caddy containers (which may not be a Host).
    /// The caddyContainer's parent host is used as the source host.
    /// </summary>
    public static string ResolveCaddyUpstreamHost(
        Image targetImage, Container caddyContainer, WireResolver resolver, Topology topology)
    {
        // Pool images are on separate infrastructure - route to pool's public IP
        var targetPool = resolver.FindPoolFor(targetImage.Id);
        if (targetPool != null)
        {
            var poolName = SanitizeName(targetPool.Name);
            var providerKey = ResolveProviderKey(targetPool, topology);
            return $"${{{ProviderHclBase.GetPrivateIpReference(poolName, providerKey, isReplicated: true)}}}";
        }

        // Elastic images (replicas > 1) get their own instances - route to instance IP
        var (min, max) = ParseReplicaRange(targetImage.Config);
        if (min > 1 || max > 1)
        {
            var imageName = SanitizeName(targetImage.Name);
            var imageParent = resolver.FindHostFor(targetImage.Id) ?? resolver.FindCaddyFor(targetImage.Id);
            var providerKey = imageParent != null
                ? ResolveProviderKey(imageParent, topology)
                : topology.Provider;
            return $"${{{ProviderHclBase.GetPrivateIpReference(imageName, providerKey, isReplicated: true)}}}";
        }

        var caddyHost = resolver.FindHostFor(caddyContainer.Id);
        var targetHost = resolver.FindHostFor(targetImage.Id);

        // Both on the same host, or both standalone (no host ancestor) - use container name
        if (caddyHost?.Id == targetHost?.Id)
            return SanitizeName(targetImage.Name);

        // Target is on a different host (or Caddy is standalone and target has a host) - use IP reference
        if (targetHost != null)
        {
            var targetHostName = SanitizeName(targetHost.Name);
            var providerKey = ResolveProviderKey(targetHost, topology);
            var isReplicated = IsReplicatedHost(new HostEntry(targetHost));
            return $"${{{ProviderHclBase.GetPrivateIpReference(targetHostName, providerKey, isReplicated)}}}";
        }

        // Target has no host ancestor (standalone) - use container name
        return SanitizeName(targetImage.Name);
    }

    /// <summary>
    /// Checks if a target image has any consumers on a different host.
    /// Used to determine if a service's ports need to be published on the host for cross-host access.
    /// </summary>
    public static bool HasCrossHostConsumers(Image image, Container host, WireResolver resolver)
    {
        // Check all ports on this image for incoming wires from other hosts
        foreach (var port in image.Ports)
        {
            var incoming = resolver.ResolveIncoming(image.Id, port.Name);
            foreach (var (node, _) in incoming)
            {
                var consumerId = node switch
                {
                    Image img => img.Id,
                    Container c => c.Id,
                    _ => Guid.Empty
                };
                if (consumerId == Guid.Empty) continue;

                var consumerHost = resolver.FindHostFor(consumerId);
                if (consumerHost != null && consumerHost.Id != host.Id)
                    return true;

                // Elastic images (replicas > 1) run on their own instances - always cross-host
                if (consumerHost == null && node is Image consumerImage)
                {
                    var (cMin, cMax) = ParseReplicaRange(consumerImage.Config);
                    if (cMin > 1 || cMax > 1)
                        return true;
                }
            }
        }
        return false;
    }

    // --- Host port assignment ---

    /// <summary>
    /// Computes host port assignments for co-located images on a Caddy host.
    /// When multiple images share the same container port (e.g. two Redis on 6379),
    /// subsequent images get an offset host port to avoid conflicts.
    /// Returns a map of imageId → (containerPort → hostPort).
    /// </summary>
    public static Dictionary<Guid, Dictionary<int, int>> ComputeHostPortAssignments(
        Container caddy, WireResolver resolver) =>
        ComputeHostPortAssignments(caddy, resolver, DefaultPlugins.CreateRegistry());

    public static Dictionary<Guid, Dictionary<int, int>> ComputeHostPortAssignments(
        Container caddy, WireResolver resolver, ImagePluginRegistry registry)
    {
        var assignments = new Dictionary<Guid, Dictionary<int, int>>();
        var usedPorts = new HashSet<int>();
        var coLocatedImages = CollectImagesExcludingPools(caddy);

        foreach (var image in coLocatedImages)
        {
            var (imgMin, imgMax) = ParseReplicaRange(image.Config);
            if (imgMin > 1 || imgMax > 1) continue;

            var ports = registry.GetPorts(image);
            if (ports.Length == 0) continue;

            var desc = registry.GetDescriptor(image);
            var needsPorts = (desc?.IsPublicEndpoint ?? false) ||
                HasCrossHostConsumers(image, caddy, resolver);
            if (!needsPorts) continue;

            var imgPorts = new Dictionary<int, int>();
            foreach (var port in ports)
            {
                var hostPort = port;
                while (!usedPorts.Add(hostPort))
                    hostPort++;
                imgPorts[port] = hostPort;
            }
            assignments[image.Id] = imgPorts;
        }
        return assignments;
    }

    /// <summary>
    /// Resolves the correct port to use when connecting to a target image.
    /// For same-host (Docker DNS), returns the default container port.
    /// For cross-host access, returns the mapped host port from port assignments.
    /// </summary>
    public static int ResolveServicePort(
        Image targetImage, Container sourceHost, int defaultPort,
        WireResolver resolver, Dictionary<Guid, Dictionary<int, int>>? portAssignments)
    {
        if (portAssignments == null ||
            !portAssignments.TryGetValue(targetImage.Id, out var ports))
            return defaultPort;

        // Check if access is cross-host
        var targetHost = resolver.FindHostFor(targetImage.Id);
        if (targetHost != null)
            return targetHost.Id == sourceHost.Id ? defaultPort : ports.GetValueOrDefault(defaultPort, defaultPort);

        var targetCaddy = resolver.FindCaddyFor(targetImage.Id);
        if (targetCaddy != null && targetCaddy.Id != sourceHost.Id)
            return ports.GetValueOrDefault(defaultPort, defaultPort);

        return defaultPort;
    }

    // --- Rate limiting ---

    /// <summary>
    /// Generates iptables hashlimit commands for rate limiting HTTP/HTTPS traffic.
    /// Always-on for any host running Caddy, with configurable threshold via caddy.Config["rateLimit"].
    /// Default: 1000/min per source IP with 2x burst.
    /// </summary>
    public static List<string> GenerateRateLimitCommands(Container caddy)
    {
        var rateConfig = caddy.Config.GetValueOrDefault("rateLimit", "1000/min");
        var caddyName = SanitizeName(caddy.Name);

        // Parse rate (e.g., "100/min") and compute burst as 2x the rate number
        var burst = 200;
        var slashIdx = rateConfig.IndexOf('/');
        if (slashIdx > 0 && int.TryParse(rateConfig[..slashIdx], out var rateNum))
            burst = rateNum * 2;

        return
        [
            "modprobe xt_hashlimit 2>/dev/null || true",
            $"iptables -A INPUT -p tcp --dport 80 -m hashlimit --hashlimit-above {rateConfig} --hashlimit-burst {burst} --hashlimit-mode srcip --hashlimit-name {caddyName}_http -j DROP || true",
            $"iptables -A INPUT -p tcp --dport 443 -m hashlimit --hashlimit-above {rateConfig} --hashlimit-burst {burst} --hashlimit-mode srcip --hashlimit-name {caddyName}_https -j DROP || true",
            "mkdir -p /etc/iptables",
            "sh -c 'iptables-save > /etc/iptables/rules.v4'"
        ];
    }

    // --- Caddyfile ---

    public static string GenerateCaddyfile(Container caddy, WireResolver resolver, Topology? topology = null) =>
        GenerateCaddyfile(caddy, resolver, DefaultPlugins.CreateRegistry(), topology);

    public static string GenerateCaddyfile(Container caddy, WireResolver resolver, ImagePluginRegistry registry, Topology? topology = null)
    {
        var upstreams = resolver.ResolveCaddyUpstreams(caddy);
        var domain = "${var.domain}";

        var securityHeaders = new[]
        {
            "  header {",
            "    Strict-Transport-Security \"max-age=31536000; includeSubDomains; preload\"",
            "    X-Content-Type-Options \"nosniff\"",
            "    X-Frame-Options \"SAMEORIGIN\"",
            "    Referrer-Policy \"strict-origin-when-cross-origin\"",
            "    Permissions-Policy \"camera=(self), microphone=(self), geolocation=(), payment=()\"",
            "  }"
        };

        var grouped = new Dictionary<string, List<string>>();
        foreach (var (image, subdomain) in upstreams)
        {
            var ports = registry.GetPorts(image);
            var port = ports.Length > 0 ? ports[0] : 80;
            var host = $"{subdomain}.{domain}";

            var upstreamHost = topology != null
                ? ResolveCaddyUpstreamHost(image, caddy, resolver, topology)
                : SanitizeName(image.Name);

            var backend = $"{upstreamHost}:{port}";
            if (!grouped.TryGetValue(host, out var backends))
            {
                backends = [];
                grouped[host] = backends;
            }
            if (!backends.Contains(backend))
                backends.Add(backend);
        }

        // ComputePool wildcard routes (*.domain → pool) are NOT statically configured.
        // When compute_pool_host_count=0 there are no pool instances, so a static reference
        // like compute_pool[0].private_ip would be an invalid Terraform reference.
        // Hub configures wildcard tenant routing via Caddy admin API at runtime.

        var blocks = new List<string>();
        foreach (var (host, backends) in grouped)
        {
            var block = new List<string> { $"{host} {{" };
            block.AddRange(securityHeaders);
            block.Add($"  reverse_proxy {string.Join(" ", backends)}");
            block.Add("}");
            blocks.Add(string.Join("\n", block));
        }

        // Apex domain route - find fixed "www" subdomain for hub
        var hubUpstream = upstreams.FirstOrDefault(u => u.Subdomain == "www");
        if (hubUpstream != default)
        {
            var hubHost = $"{hubUpstream.Subdomain}.{domain}";
            if (grouped.TryGetValue(hubHost, out var hubBackends))
            {
                var block = new List<string> { $"{domain} {{" };
                block.AddRange(securityHeaders);
                block.Add($"  reverse_proxy {string.Join(" ", hubBackends)}");
                block.Add("}");
                blocks.Add(string.Join("\n", block));
            }
        }

        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Recursively collects all Registry images from a container and its children.
    /// Used by DNS generators to create A records for registry domains.
    /// </summary>
    public static List<Image> CollectRegistryImagesRecursive(Container container)
    {
        var result = new List<Image>();
        result.AddRange(container.Images.Where(i => i.Kind == ImageKind.Registry));
        foreach (var child in container.Children)
            result.AddRange(CollectRegistryImagesRecursive(child));
        return result;
    }

    // --- Public endpoints ---

    /// <summary>
    /// Collects public endpoints from the topology by walking all images with IsPublicEndpoint metadata.
    /// All public images derive their subdomain from their name (same pattern as Caddy routing).
    /// Uses the topology's display domain, not Terraform variable interpolation.
    /// </summary>
    public static List<(string Url, string Kind, string? Backend)> CollectPublicEndpoints(Topology topology) =>
        CollectPublicEndpoints(topology, DefaultPlugins.CreateRegistry());

    public static List<(string Url, string Kind, string? Backend)> CollectPublicEndpoints(Topology topology, ImagePluginRegistry registry)
    {
        var endpoints = new List<(string, string, string?)>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenImageIds = new HashSet<Guid>();
        var resolver = new WireResolver(topology, registry);
        var domain = ResolveDomain(topology);

        void WalkForCaddies(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind == ContainerKind.Caddy)
                {
                    var upstreams = resolver.ResolveCaddyUpstreams(container);
                    var grouped = new Dictionary<string, List<string>>();
                    foreach (var (image, subdomain) in upstreams)
                    {
                        seenImageIds.Add(image.Id);
                        var ports = registry.GetPorts(image);
                        var port = ports.Length > 0 ? ports[0] : 80;
                        var host = $"{subdomain}.{domain}";
                        var backend = $"{SanitizeName(image.Name)}:{port}";
                        if (!grouped.TryGetValue(host, out var backends))
                        {
                            backends = [];
                            grouped[host] = backends;
                        }
                        if (!backends.Contains(backend))
                            backends.Add(backend);
                    }

                    foreach (var (host, backends) in grouped)
                    {
                        var url = $"https://{host}";
                        if (seenUrls.Add(url))
                            endpoints.Add((url, "reverse_proxy", string.Join(" ", backends)));
                    }

                    var hubUpstream = upstreams.FirstOrDefault(u => u.Subdomain == "www");
                    if (hubUpstream != default)
                    {
                        var hubHost = $"{hubUpstream.Subdomain}.{domain}";
                        if (grouped.TryGetValue(hubHost, out var hubBackends))
                        {
                            var apexUrl = $"https://{domain}";
                            if (seenUrls.Add(apexUrl))
                                endpoints.Add((apexUrl, "apex", string.Join(" ", hubBackends)));
                        }
                    }
                }

                WalkForCaddies(container.Children);
            }
        }

        WalkForCaddies(topology.Containers);

        void WalkForPublicImages(List<Container> containers)
        {
            foreach (var container in containers)
            {
                foreach (var image in container.Images)
                {
                    if (seenImageIds.Contains(image.Id)) continue;
                    var desc = registry.GetDescriptor(image);
                    if (desc is not { IsPublicEndpoint: true }) continue;

                    var subdomain = image.Name.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
                    if (string.IsNullOrEmpty(subdomain)) continue;

                    var port = desc.Ports.FirstOrDefault()?.Port ?? 80;
                    var url = $"https://{subdomain}.{domain}";
                    if (seenUrls.Add(url))
                        endpoints.Add((url, image.ResolveTypeId().ToLowerInvariant(), $"{SanitizeName(image.Name)}:{port}"));
                }

                WalkForPublicImages(container.Children);
            }
        }

        WalkForPublicImages(topology.Containers);

        return endpoints;
    }

    /// <summary>
    /// Resolves the display domain from the topology's DNS container config.
    /// Falls back to "example.com" if no DNS container exists.
    /// </summary>
    public static string ResolveDomain(Topology topology)
    {
        static string? FindDomain(List<Container> containers)
        {
            foreach (var c in containers)
            {
                if (c.Kind == ContainerKind.Dns && c.Config.TryGetValue("domain", out var domain))
                    return domain;
                var child = FindDomain(c.Children);
                if (child != null) return child;
            }
            return null;
        }
        return FindDomain(topology.Containers) ?? "example.com";
    }

    // --- Utilities ---

    public static string SanitizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_')
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Aggregate("", (current, c) => current + c);

    public static string GetDefaultDockerImage(ImageKind kind, string? registry = null) => kind switch
    {
        ImageKind.HubServer => $"{registry ?? "docker.xcord.net"}/hub:latest",
        ImageKind.FederationServer => $"{registry ?? "docker.xcord.net"}/fed:latest",
        ImageKind.Redis => "redis:7-alpine",
        ImageKind.PostgreSQL => "postgres:17-alpine",
        ImageKind.MinIO => "minio/minio:RELEASE.2025-02-28T09-55-16Z",
        ImageKind.LiveKit => "livekit/livekit-server:v1.8.3",
        ImageKind.Registry => "registry:2",
        ImageKind.Custom => "alpine:3.21",
        _ => "alpine:3.21"
    };

    public static string GetDefaultDockerImage(string typeId, string? registry, ImagePluginRegistry pluginRegistry)
    {
        var plugin = pluginRegistry.Get(typeId);
        if (plugin == null) return "alpine:3.21";
        var desc = plugin.GetDescriptor();
        var behavior = plugin.GetDockerBehavior();

        if (behavior.RequiresPrivateRegistry)
        {
            var reg = registry ?? "docker.xcord.net";
            var shortName = typeId switch
            {
                "HubServer" => "hub",
                "FederationServer" => "fed",
                _ => typeId.ToLowerInvariant()
            };
            return $"{reg}/{shortName}:latest";
        }

        return desc.DefaultDockerImage ?? "alpine:3.21";
    }

    public static bool RequiresPrivateRegistry(ImageKind kind) =>
        kind is ImageKind.HubServer or ImageKind.FederationServer;

    public static bool RequiresPrivateRegistry(string typeId, ImagePluginRegistry pluginRegistry) =>
        pluginRegistry.Get(typeId)?.GetDockerBehavior().RequiresPrivateRegistry ?? false;

    public static string GetDockerImageForHcl(Image image, string resolvedRegistry) =>
        GetDockerImageForHcl(image, resolvedRegistry, DefaultPlugins.CreateRegistry());

    public static string GetDockerImageForHcl(Image image, string resolvedRegistry, ImagePluginRegistry registry)
    {
        var plugin = registry.GetForImage(image);
        var behavior = plugin?.GetDockerBehavior();

        if (behavior is not { RequiresPrivateRegistry: true })
        {
            var desc = plugin?.GetDescriptor();
            var defaultImage = desc?.DefaultDockerImage ?? "alpine:3.21";
            var dockerImage = image.DockerImage ?? defaultImage;
            if (dockerImage.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
                return defaultImage;
            return dockerImage;
        }

        var versionVar = behavior.VersionVariableName ?? throw new ArgumentException(
            $"Plugin '{image.ResolveTypeId()}' requires private registry but has no VersionVariableName");
        var shortName = image.ResolveTypeId() switch
        {
            "HubServer" => "hub",
            "FederationServer" => "fed",
            _ => image.ResolveTypeId().ToLowerInvariant()
        };
        return $"${{var.registry_url}}/{shortName}:${{var.{versionVar}}}";
    }

    public static string GetVersionVariableName(ImageKind kind) => kind switch
    {
        ImageKind.HubServer => "hub_version",
        ImageKind.FederationServer => "fed_version",
        _ => throw new ArgumentException($"No version variable for image kind: {kind}")
    };

    public static string GetVersionVariableName(string typeId, ImagePluginRegistry pluginRegistry)
    {
        var plugin = pluginRegistry.GetRequired(typeId);
        return plugin.GetDockerBehavior().VersionVariableName
            ?? throw new ArgumentException($"No version variable for image type: {typeId}");
    }

    /// <summary>
    /// Generates the docker login command for use in provisioning scripts.
    /// Returns null if no auth is configured (registry_username is empty).
    /// Uses Terraform variable interpolation so credentials stay in tfvars, not HCL.
    /// </summary>
    public static string GenerateDockerLoginCommand(bool useSudo)
    {
        var sudo = useSudo ? "sudo " : "";
        return $"{sudo}bash -c 'if [ -n \\\"${{var.registry_username}}\\\" ]; then echo \\\"${{nonsensitive(var.registry_password)}}\\\" | docker login ${{var.registry_url}} -u \\\"${{var.registry_username}}\\\" --password-stdin; fi'";
    }

    /// <summary>
    /// Resolves the effective provider key for a container:
    /// container.Config["provider"] if set, otherwise topology-level provider.
    /// </summary>
    public static string ResolveProviderKey(Container container, Topology topology) =>
        container.Config.GetValueOrDefault("provider", topology.Provider);

    /// <summary>
    /// Collect all distinct provider keys used across a topology's containers.
    /// </summary>
    public static List<string> CollectActiveProviderKeys(Topology topology)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topology.Provider };
        CollectProviderKeysRecursive(topology.Containers, topology, keys);
        return keys.ToList();
    }

    private static void CollectProviderKeysRecursive(List<Container> containers, Topology topology, HashSet<string> keys)
    {
        foreach (var container in containers)
        {
            var providerOverride = container.Config.GetValueOrDefault("provider", "");
            if (!string.IsNullOrEmpty(providerOverride))
                keys.Add(providerOverride);
            CollectProviderKeysRecursive(container.Children, topology, keys);
        }
    }
}
