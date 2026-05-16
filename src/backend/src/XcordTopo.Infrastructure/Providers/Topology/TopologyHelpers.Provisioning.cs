using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
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
            // On a Caddy host, IsPublicEndpoint images are reverse-proxied via the Docker
            // bridge network - they must NOT bind host ports (Caddy owns 80/443).
            // Only assign host ports for cross-host consumers (e.g. database access).
            var needsPorts = HasCrossHostConsumers(image, caddy, resolver);
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

}
