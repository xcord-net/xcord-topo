using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
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
    /// True if any image co-located on the host declares a MountPath (data volume).
    /// Data-bearing hosts (PG, Redis, MinIO, Registry) get terraform lifecycle protection
    /// (prevent_destroy + delete_on_termination=false on root_block_device) so that
    /// re-running a topology apply cannot accidentally wipe stateful infrastructure.
    /// Caddy data is excluded -- TLS certs can be re-issued, so caddy hosts remain
    /// freely destroyable.
    /// </summary>
    public static bool HasPersistentImage(Container host, ImagePluginRegistry registry)
    {
        var images = CollectImages(host);
        foreach (var image in images)
        {
            var desc = registry.GetDescriptor(image);
            if (desc?.MountPath != null) return true;
        }
        return false;
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

}
