using System.Text.RegularExpressions;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Validation;

public sealed partial class TopologyValidator(ProviderRegistry registry) : ITopologyValidator
{
    private static readonly Regex DomainRegex = DomainPattern();
    private static readonly Regex DangerousCharsRegex = DangerousCharsPattern();
    private static readonly HashSet<string> ValidBackupFrequencies =
        new(StringComparer.OrdinalIgnoreCase) { "hourly", "daily", "weekly" };

    public List<string> Validate(Topology topology) =>
        ValidateFull(topology).Errors.Select(e => e.Message).ToList();

    public TopologyValidationResult ValidateFull(Topology topology)
    {
        var items = new List<TopologyValidationError>();

        RunStructuralChecks(topology, items);
        RunDeployErrorChecks(topology, items);
        RunWarningChecks(topology, items);

        return new TopologyValidationResult(items);
    }

    // ─── Tier 0: Existing structural checks ─────────────────────────

    private static void RunStructuralChecks(Topology topology, List<TopologyValidationError> items)
    {
        if (string.IsNullOrWhiteSpace(topology.Name))
            items.Add(new(ValidationSeverity.Error, "Topology name is required.", Field: "name"));

        if (topology.Containers.Count == 0)
            items.Add(new(ValidationSeverity.Error, "Topology must have at least one container."));

        var allPorts = new HashSet<Guid>();
        var allNodeIds = new HashSet<Guid>();
        ValidateContainersRecursive(topology.Containers, items, allPorts, allNodeIds);

        foreach (var wire in topology.Wires)
        {
            if (!allNodeIds.Contains(wire.FromNodeId))
                items.Add(new(ValidationSeverity.Error,
                    $"Wire {wire.Id} references non-existent source node {wire.FromNodeId}."));
            if (!allNodeIds.Contains(wire.ToNodeId))
                items.Add(new(ValidationSeverity.Error,
                    $"Wire {wire.Id} references non-existent target node {wire.ToNodeId}."));
            if (!allPorts.Contains(wire.FromPortId))
                items.Add(new(ValidationSeverity.Error,
                    $"Wire {wire.Id} references non-existent source port {wire.FromPortId}."));
            if (!allPorts.Contains(wire.ToPortId))
                items.Add(new(ValidationSeverity.Error,
                    $"Wire {wire.Id} references non-existent target port {wire.ToPortId}."));
            if (wire.FromNodeId == wire.ToNodeId)
                items.Add(new(ValidationSeverity.Error,
                    $"Wire {wire.Id} cannot connect a node to itself."));
        }

        var wireKeys = new HashSet<string>();
        foreach (var wire in topology.Wires)
        {
            var key = $"{wire.FromPortId}-{wire.ToPortId}";
            var reverseKey = $"{wire.ToPortId}-{wire.FromPortId}";
            if (!wireKeys.Add(key) || wireKeys.Contains(reverseKey))
                items.Add(new(ValidationSeverity.Error,
                    $"Duplicate wire connection between ports {wire.FromPortId} and {wire.ToPortId}."));
        }
    }

    private static void ValidateContainersRecursive(
        List<Container> containers, List<TopologyValidationError> items,
        HashSet<Guid> allPorts, HashSet<Guid> allNodeIds)
    {
        foreach (var container in containers)
        {
            allNodeIds.Add(container.Id);
            foreach (var port in container.Ports)
                allPorts.Add(port.Id);

            foreach (var image in container.Images)
            {
                allNodeIds.Add(image.Id);
                foreach (var port in image.Ports)
                    allPorts.Add(port.Id);

                if (image.Config.TryGetValue("replicas", out var replicas) && !string.IsNullOrEmpty(replicas))
                {
                    if (!IsValidReplicaValue(replicas))
                        items.Add(new(ValidationSeverity.Error,
                            $"Image '{image.Name}' has invalid replicas value '{replicas}'. Must be a positive integer, a range (e.g. 1-3), or a $VARIABLE reference.",
                            NodeId: image.Id.ToString(), Field: "replicas"));
                }
            }

            if (string.IsNullOrWhiteSpace(container.Name))
                items.Add(new(ValidationSeverity.Error,
                    $"Container {container.Id} must have a name.",
                    NodeId: container.Id.ToString(), Field: "name"));

            if (container.Width <= 0 || container.Height <= 0)
                items.Add(new(ValidationSeverity.Error,
                    $"Container '{container.Name}' must have positive dimensions.",
                    NodeId: container.Id.ToString()));

            ValidateContainersRecursive(container.Children, items, allPorts, allNodeIds);
        }
    }

    // ─── Tier 1: Deploy-blocking checks ─────────────────────────────

    private void RunDeployErrorChecks(Topology topology, List<TopologyValidationError> items)
    {
        CheckNameUniqueness(topology, items);
        CheckProviderValidity(topology, items);
        CheckRegionValidity(topology, items);
        CheckTierProfileReferences(topology, items);
        CheckDomainPresence(topology, items);
        CheckVolumeSizes(topology, items);
        CheckComputePoolRequiredImages(topology, items);
        CheckWireCompleteness(topology, items);
        CheckCaddyfileSafety(topology, items);
        CheckHostRamFeasibility(topology, items);
    }

    private static void CheckNameUniqueness(Topology topology, List<TopologyValidationError> items)
    {
        // Container names must be globally unique (they become Terraform resource names)
        var seenContainers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                var sanitized = TopologyHelpers.SanitizeName(c.Name);
                if (!string.IsNullOrEmpty(sanitized))
                {
                    if (seenContainers.TryGetValue(sanitized, out var existing))
                        items.Add(new(ValidationSeverity.Error,
                            $"Name collision: '{c.Name}' and '{existing}' both sanitize to '{sanitized}', causing Terraform resource conflicts.",
                            NodeId: c.Id.ToString(), Field: "name"));
                    else
                        seenContainers[sanitized] = c.Name;
                }

                // Image names only need to be unique within their parent container
                // (they become Docker container names scoped to the host)
                var seenImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var img in c.Images)
                {
                    var imgSanitized = TopologyHelpers.SanitizeName(img.Name);
                    if (!string.IsNullOrEmpty(imgSanitized))
                    {
                        if (seenImages.TryGetValue(imgSanitized, out var existing))
                            items.Add(new(ValidationSeverity.Error,
                                $"Name collision: '{img.Name}' and '{existing}' both sanitize to '{imgSanitized}' within '{c.Name}'.",
                                NodeId: img.Id.ToString(), Field: "name"));
                        else
                            seenImages[imgSanitized] = img.Name;
                    }
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);
    }

    private void CheckProviderValidity(Topology topology, List<TopologyValidationError> items)
    {
        var activeKeys = TopologyHelpers.CollectActiveProviderKeys(topology);
        foreach (var key in activeKeys)
        {
            if (registry.Get(key) is null)
                items.Add(new(ValidationSeverity.Error,
                    $"Provider '{key}' is not registered. Valid providers: {string.Join(", ", registry.GetAll().Select(p => p.Key))}"));
        }
    }

    private void CheckRegionValidity(Topology topology, List<TopologyValidationError> items)
    {
        // Check provider-namespaced region keys (e.g., aws_region, linode_region)
        foreach (var provider in registry.GetAll())
        {
            var regionKey = $"{provider.Key}_region";
            if (topology.ProviderConfig.TryGetValue(regionKey, out var region) && !string.IsNullOrEmpty(region))
            {
                var validRegions = provider.GetRegions().Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!validRegions.Contains(region))
                    items.Add(new(ValidationSeverity.Error,
                        $"Region '{region}' is not valid for provider '{provider.Key}'. Valid regions: {string.Join(", ", validRegions)}",
                        Field: regionKey));
            }
        }
    }

    private static void CheckTierProfileReferences(Topology topology, List<TopologyValidationError> items)
    {
        var profiles = topology.TierProfiles.Count > 0
            ? topology.TierProfiles
            : ImageOperationalMetadata.DefaultTierProfiles;
        var validIds = profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        void Walk(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind == ContainerKind.ComputePool)
                {
                    var tierRef = container.Config.GetValueOrDefault("tierProfile", "");
                    if (!string.IsNullOrEmpty(tierRef) && !validIds.Contains(tierRef))
                        items.Add(new(ValidationSeverity.Error,
                            $"ComputePool '{container.Name}' references unknown tier profile '{tierRef}'. Valid profiles: {string.Join(", ", validIds)}",
                            NodeId: container.Id.ToString(), Field: "tierProfile"));
                }

                Walk(container.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckDomainPresence(Topology topology, List<TopologyValidationError> items)
    {
        void Walk(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind is ContainerKind.Caddy or ContainerKind.Dns)
                {
                    var domain = container.Config.GetValueOrDefault("domain", "");
                    if (string.IsNullOrWhiteSpace(domain))
                        items.Add(new(ValidationSeverity.Error,
                            $"{container.Kind} container '{container.Name}' must have a non-empty domain.",
                            NodeId: container.Id.ToString(), Field: "domain"));
                    else if (!DomainRegex.IsMatch(domain))
                        items.Add(new(ValidationSeverity.Error,
                            $"{container.Kind} container '{container.Name}': '{domain}' is not a valid domain name.",
                            NodeId: container.Id.ToString(), Field: "domain"));
                }

                Walk(container.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckVolumeSizes(Topology topology, List<TopologyValidationError> items)
    {
        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                foreach (var img in c.Images)
                {
                    if (img.Config.TryGetValue("volumeSize", out var sizeStr) && !string.IsNullOrEmpty(sizeStr))
                    {
                        if (!int.TryParse(sizeStr, out var size) || size <= 0)
                            items.Add(new(ValidationSeverity.Error,
                                $"Image '{img.Name}' in '{c.Name}': volumeSize must be a positive integer, got '{sizeStr}'.",
                                NodeId: img.Id.ToString(), Field: "volumeSize"));
                    }
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckComputePoolRequiredImages(Topology topology, List<TopologyValidationError> items)
    {
        // ComputePool no longer requires FederationServer images — they are hub-provisioned at runtime.
        // DataPool should contain at least one data service image.
        void Walk(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind == ContainerKind.DataPool)
                {
                    var hasDataImage = container.Images.Any(i =>
                        i.Kind is ImageKind.PostgreSQL or ImageKind.Redis or ImageKind.MinIO);
                    if (!hasDataImage)
                        items.Add(new(ValidationSeverity.Error,
                            $"DataPool '{container.Name}' must contain at least one data service image (PostgreSQL, Redis, or MinIO).",
                            NodeId: container.Id.ToString()));
                }

                Walk(container.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckWireCompleteness(Topology topology, List<TopologyValidationError> items)
    {
        var resolver = new WireResolver(topology);

        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                foreach (var img in c.Images)
                {
                    switch (img.Kind)
                    {
                        case ImageKind.FederationServer:
                            if (resolver.ResolveWiredImage(img.Id, "pg") is null)
                                items.Add(new(ValidationSeverity.Error,
                                    $"FederationServer '{img.Name}' in '{c.Name}' is not connected to a PostgreSQL image.",
                                    NodeId: img.Id.ToString()));
                            if (resolver.ResolveWiredImage(img.Id, "redis") is null)
                                items.Add(new(ValidationSeverity.Error,
                                    $"FederationServer '{img.Name}' in '{c.Name}' is not connected to a Redis image.",
                                    NodeId: img.Id.ToString()));
                            if (resolver.ResolveWiredImage(img.Id, "minio") is null)
                                items.Add(new(ValidationSeverity.Error,
                                    $"FederationServer '{img.Name}' in '{c.Name}' is not connected to a MinIO image.",
                                    NodeId: img.Id.ToString()));
                            break;
                        case ImageKind.HubServer:
                            if (resolver.ResolveWiredImage(img.Id, "pg") is null)
                                items.Add(new(ValidationSeverity.Error,
                                    $"HubServer '{img.Name}' in '{c.Name}' is not connected to a PostgreSQL image.",
                                    NodeId: img.Id.ToString()));
                            if (resolver.ResolveWiredImage(img.Id, "redis") is null)
                                items.Add(new(ValidationSeverity.Error,
                                    $"HubServer '{img.Name}' in '{c.Name}' is not connected to a Redis image.",
                                    NodeId: img.Id.ToString()));
                            break;
                    }
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckCaddyfileSafety(Topology topology, List<TopologyValidationError> items)
    {
        void Walk(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind == ContainerKind.Caddy)
                {
                    var domain = container.Config.GetValueOrDefault("domain", "");
                    if (!string.IsNullOrEmpty(domain) && DangerousCharsRegex.IsMatch(domain))
                        items.Add(new(ValidationSeverity.Error,
                            $"Caddy container '{container.Name}': domain value contains unsafe characters that could cause shell injection.",
                            NodeId: container.Id.ToString(), Field: "domain"));
                }

                Walk(container.Children);
            }
        }

        Walk(topology.Containers);
    }

    private void CheckHostRamFeasibility(Topology topology, List<TopologyValidationError> items)
    {
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var provider = registry.Get(topology.Provider);
        if (provider is null) return;

        var plans = provider.GetPlans();
        if (plans.Count == 0) return;
        var maxPlanRam = plans.Max(p => p.MemoryMb);

        foreach (var entry in hosts)
        {
            var required = TopologyHelpers.CalculateHostRam(entry.Host);
            if (required > maxPlanRam)
                items.Add(new(ValidationSeverity.Error,
                    $"Host '{entry.Host.Name}' requires {required} MB RAM but the largest available plan for '{topology.Provider}' is {maxPlanRam} MB.",
                    NodeId: entry.Host.Id.ToString()));
        }
    }

    // ─── Tier 2: Warning checks ─────────────────────────────────────

    private static void RunWarningChecks(Topology topology, List<TopologyValidationError> items)
    {
        CheckOrphanedImages(topology, items);
        CheckUnusedTierProfiles(topology, items);
        CheckBackupFrequency(topology, items);
    }

    private static void CheckOrphanedImages(Topology topology, List<TopologyValidationError> items)
    {
        var connectedNodes = new HashSet<Guid>();
        foreach (var wire in topology.Wires)
        {
            connectedNodes.Add(wire.FromNodeId);
            connectedNodes.Add(wire.ToNodeId);
        }

        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                foreach (var img in c.Images)
                {
                    if (img.Ports.Count > 0 && !connectedNodes.Contains(img.Id))
                        items.Add(new(ValidationSeverity.Warning,
                            $"Image '{img.Name}' in '{c.Name}' has ports but no wires connecting it.",
                            NodeId: img.Id.ToString()));
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);
    }

    private static void CheckUnusedTierProfiles(Topology topology, List<TopologyValidationError> items)
    {
        if (topology.TierProfiles.Count == 0) return;

        var referencedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasAgnosticPool = false;

        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                if (c.Kind == ContainerKind.ComputePool)
                {
                    var tierRef = c.Config.GetValueOrDefault("tierProfile", "");
                    if (!string.IsNullOrEmpty(tierRef))
                        referencedIds.Add(tierRef);
                    else
                        hasAgnosticPool = true; // tier-agnostic pool serves all tiers
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);

        // A tier-agnostic pool (no tierProfile) implicitly references all profiles
        if (hasAgnosticPool) return;

        foreach (var profile in topology.TierProfiles)
        {
            if (!referencedIds.Contains(profile.Id))
                items.Add(new(ValidationSeverity.Warning,
                    $"Tier profile '{profile.Name}' (id: {profile.Id}) is defined but not referenced by any ComputePool."));
        }
    }

    private static void CheckBackupFrequency(Topology topology, List<TopologyValidationError> items)
    {
        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                var hostFreq = c.Config.GetValueOrDefault("backupFrequency", "");
                if (!string.IsNullOrEmpty(hostFreq) && !ValidBackupFrequencies.Contains(hostFreq))
                    items.Add(new(ValidationSeverity.Warning,
                        $"Container '{c.Name}': backupFrequency '{hostFreq}' is not recognized (valid: hourly, daily, weekly) and will be silently ignored.",
                        NodeId: c.Id.ToString(), Field: "backupFrequency"));

                foreach (var img in c.Images)
                {
                    var imgFreq = img.Config.GetValueOrDefault("backupFrequency", "");
                    if (!string.IsNullOrEmpty(imgFreq) && !ValidBackupFrequencies.Contains(imgFreq))
                        items.Add(new(ValidationSeverity.Warning,
                            $"Image '{img.Name}' in '{c.Name}': backupFrequency '{imgFreq}' is not recognized and will be silently ignored.",
                            NodeId: img.Id.ToString(), Field: "backupFrequency"));
                }

                Walk(c.Children);
            }
        }

        Walk(topology.Containers);
    }

    // ─── Utility ────────────────────────────────────────────────────

    private static bool IsValidReplicaValue(string value)
    {
        if (value.StartsWith('$') && value.Length > 1 && !value.Contains(' '))
            return true;
        if (int.TryParse(value, out var n) && n > 0)
            return true;
        // Accept range format "min-max" where both are positive integers and min <= max
        var parts = value.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var min) && min > 0
            && int.TryParse(parts[1], out var max) && max > 0
            && min <= max)
            return true;
        return false;
    }

    [GeneratedRegex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$")]
    private static partial Regex DomainPattern();

    [GeneratedRegex(@"[;|&$`""\\<>(){}\[\]*?!#]")]
    private static partial Regex DangerousCharsPattern();
}
