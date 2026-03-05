using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Shared topology tree-walking helpers used by all cloud providers.
/// Extracted from LinodeProvider to break cross-provider coupling.
/// </summary>
public static class TopologyHelpers
{
    // --- Records ---

    public record HostEntry(Container Host);
    public record ComputePoolEntry(Container Pool, TierProfile TierProfile, int TargetTenants, string? SelectedPlanId = null);
    public record PoolSelection(string PoolName, string PlanId, int TargetTenants);
    public record SecretEntry(string ResourceName, string Description);

    // --- Tree-walking ---

    public static List<HostEntry> CollectHosts(List<Container> containers)
    {
        var result = new List<HostEntry>();
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.Host)
            {
                result.Add(new HostEntry(container));
                result.AddRange(CollectHosts(container.Children));
            }
            // ComputePool and Dns containers are handled separately
        }
        return result;
    }

    public static List<ComputePoolEntry> CollectComputePools(
        List<Container> containers, Topology topology, List<PoolSelection>? selections = null)
    {
        var result = new List<ComputePoolEntry>();
        var tierProfiles = topology.TierProfiles.Count > 0
            ? topology.TierProfiles
            : ImageOperationalMetadata.DefaultTierProfiles;

        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.ComputePool)
            {
                var tierProfileId = container.Config.GetValueOrDefault("tierProfile", "free");
                var tierProfile = tierProfiles.FirstOrDefault(t => t.Id == tierProfileId)
                    ?? tierProfiles.First();

                // Use deploy-time pool selection if available, otherwise default to 0
                var selection = selections?.FirstOrDefault(s =>
                    string.Equals(s.PoolName, container.Name, StringComparison.OrdinalIgnoreCase));
                var targetTenants = selection?.TargetTenants ?? 0;
                var selectedPlanId = selection?.PlanId;

                result.Add(new ComputePoolEntry(container, tierProfile, targetTenants, selectedPlanId));
            }
        }
        return result;
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
    /// Collect all DNS containers from the topology.
    /// </summary>
    public static List<Container> CollectDnsContainers(List<Container> containers)
    {
        var result = new List<Container>();
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.Dns)
                result.Add(container);
        }
        return result;
    }

    /// <summary>
    /// Find all Host containers wired to a DNS container's "records" port.
    /// </summary>
    public static List<HostEntry> CollectHostsWiredToDns(Container dnsContainer, WireResolver resolver, List<HostEntry> allHosts)
    {
        var wiredHosts = new List<HostEntry>();
        var recordsPort = dnsContainer.Ports.FirstOrDefault(p => p.Name == "records");
        if (recordsPort == null) return wiredHosts;

        var incoming = resolver.ResolveIncoming(dnsContainer.Id, "records");
        foreach (var (node, _) in incoming)
        {
            if (node is Container c && c.Kind == ContainerKind.Host)
            {
                var entry = allHosts.FirstOrDefault(h => h.Host.Id == c.Id);
                if (entry != null) wiredHosts.Add(entry);
            }
        }
        return wiredHosts;
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

    public static bool IsReplicatedHost(HostEntry entry)
    {
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

    public static int CalculateHostRam(Container host)
    {
        var totalRam = 0;
        var images = CollectImages(host);
        foreach (var image in images)
        {
            if (ImageOperationalMetadata.Images.TryGetValue(image.Kind, out var meta))
                totalRam += meta.MinRamMb;
            else
                totalRam += 256;
        }
        var caddies = CollectCaddyContainers(host);
        if (caddies.Count > 0)
            totalRam += ImageOperationalMetadata.Caddy.MinRamMb;

        return totalRam;
    }

    // --- Secret helpers ---

    public static List<SecretEntry> CollectSecrets(HostEntry entry, WireResolver resolver)
    {
        var secrets = new List<SecretEntry>();
        var hostName = SanitizeName(entry.Host.Name);
        var images = CollectImages(entry.Host);

        foreach (var image in images)
        {
            var imgName = SanitizeName(image.Name);
            switch (image.Kind)
            {
                case ImageKind.PostgreSQL:
                    secrets.Add(new($"{hostName}_{imgName}_password", $"PostgreSQL password for {image.Name}"));
                    break;
                case ImageKind.Redis:
                    secrets.Add(new($"{hostName}_{imgName}_password", $"Redis password for {image.Name}"));
                    break;
                case ImageKind.MinIO:
                    secrets.Add(new($"{hostName}_{imgName}_access_key", $"MinIO access key for {image.Name}"));
                    secrets.Add(new($"{hostName}_{imgName}_secret_key", $"MinIO secret key for {image.Name}"));
                    break;
                case ImageKind.LiveKit:
                    secrets.Add(new($"{hostName}_{imgName}_api_key", $"LiveKit API key for {image.Name}"));
                    secrets.Add(new($"{hostName}_{imgName}_api_secret", $"LiveKit API secret for {image.Name}"));
                    break;
            }
        }
        return secrets;
    }

    /// <summary>
    /// Derive DB name from the consumer wired to this PG image.
    /// HubServer -> xcord_hub, FederationServer -> xcord, otherwise -> app
    /// </summary>
    public static string DeriveDbName(Image pgImage, WireResolver resolver)
    {
        var incoming = resolver.ResolveIncoming(pgImage.Id, "postgres");
        foreach (var (node, _) in incoming)
        {
            if (node is Image consumerImage)
            {
                return consumerImage.Kind switch
                {
                    ImageKind.HubServer => "xcord_hub",
                    ImageKind.FederationServer => "xcord",
                    _ => "app"
                };
            }
        }
        return "app";
    }

    // --- Environment variable building ---

    public static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver, Topology? topology = null)
    {
        var envVars = new List<(string, string)>();
        var hostName = SanitizeName(entry.Host.Name);

        switch (image.Kind)
        {
            case ImageKind.PostgreSQL:
            {
                var secretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_password.result}}";
                var dbName = DeriveDbName(image, resolver);
                envVars.Add(("POSTGRES_PASSWORD", secretRef));
                envVars.Add(("POSTGRES_DB", dbName));
                envVars.Add(("POSTGRES_USER", "postgres"));
                break;
            }
            case ImageKind.Redis:
                break;
            case ImageKind.MinIO:
            {
                var accessKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_access_key.result}}";
                var secretKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_secret_key.result}}";
                envVars.Add(("MINIO_ROOT_USER", accessKeyRef));
                envVars.Add(("MINIO_ROOT_PASSWORD", secretKeyRef));
                break;
            }
            case ImageKind.HubServer:
            {
                var pgTarget = resolver.ResolveWiredImage(image.Id, "pg");
                if (pgTarget != null)
                {
                    var pgContainer = SanitizeName(pgTarget.Name);
                    var pgHost = resolver.FindHostFor(pgTarget.Id);
                    var pgHostName = pgHost != null ? SanitizeName(pgHost.Name) : hostName;
                    var dbName = DeriveDbName(pgTarget, resolver);
                    var pgSecretRef = $"${{random_password.{pgHostName}_{pgContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__DefaultConnection",
                        $"Host={pgContainer};Port=5432;Database={dbName};Username=postgres;Password={pgSecretRef}"));
                }

                var redisTarget = resolver.ResolveWiredImage(image.Id, "redis");
                if (redisTarget != null)
                {
                    var redisContainer = SanitizeName(redisTarget.Name);
                    var redisHost = resolver.FindHostFor(redisTarget.Id);
                    var redisHostName = redisHost != null ? SanitizeName(redisHost.Name) : hostName;
                    var redisSecretRef = $"${{random_password.{redisHostName}_{redisContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__Redis",
                        $"{redisContainer}:6379,password={redisSecretRef}"));
                }

                // Service keys — hub gets Stripe + SMTP
                if (topology != null)
                {
                    AddServiceKeyEnvVar(envVars, topology, "stripe_publishable_key", "Stripe__PublishableKey");
                    AddServiceKeyEnvVar(envVars, topology, "stripe_secret_key", "Stripe__SecretKey");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_host", "Email__SmtpHost");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_port", "Email__SmtpPort");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_username", "Email__SmtpUsername");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_password", "Email__SmtpPassword");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_from_address", "Email__FromAddress");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_from_name", "Email__FromName");
                }
                break;
            }
            case ImageKind.FederationServer:
            {
                var pgTarget = resolver.ResolveWiredImage(image.Id, "pg");
                if (pgTarget != null)
                {
                    var pgContainer = SanitizeName(pgTarget.Name);
                    var pgHost = resolver.FindHostFor(pgTarget.Id);
                    var pgHostName = pgHost != null ? SanitizeName(pgHost.Name) : hostName;
                    var dbName = DeriveDbName(pgTarget, resolver);
                    var pgSecretRef = $"${{random_password.{pgHostName}_{pgContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__DefaultConnection",
                        $"Host={pgContainer};Port=5432;Database={dbName};Username=postgres;Password={pgSecretRef}"));
                }

                var redisTarget = resolver.ResolveWiredImage(image.Id, "redis");
                if (redisTarget != null)
                {
                    var redisContainer = SanitizeName(redisTarget.Name);
                    var redisHost = resolver.FindHostFor(redisTarget.Id);
                    var redisHostName = redisHost != null ? SanitizeName(redisHost.Name) : hostName;
                    var redisSecretRef = $"${{random_password.{redisHostName}_{redisContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__Redis",
                        $"{redisContainer}:6379,password={redisSecretRef}"));
                }

                var minioTarget = resolver.ResolveWiredImage(image.Id, "minio");
                if (minioTarget != null)
                {
                    var minioContainer = SanitizeName(minioTarget.Name);
                    var minioHost = resolver.FindHostFor(minioTarget.Id);
                    var minioHostName = minioHost != null ? SanitizeName(minioHost.Name) : hostName;
                    var accessRef = $"${{random_password.{minioHostName}_{minioContainer}_access_key.result}}";
                    var secretRef = $"${{random_password.{minioHostName}_{minioContainer}_secret_key.result}}";
                    envVars.Add(("MinIO__Endpoint", $"{minioContainer}:9000"));
                    envVars.Add(("MinIO__AccessKey", accessRef));
                    envVars.Add(("MinIO__SecretKey", secretRef));
                }

                // Service keys — instances get SMTP + Tenor
                if (topology != null)
                {
                    AddServiceKeyEnvVar(envVars, topology, "smtp_host", "Email__SmtpHost");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_port", "Email__SmtpPort");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_username", "Email__SmtpUsername");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_password", "Email__SmtpPassword");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_from_address", "Email__FromAddress");
                    AddServiceKeyEnvVar(envVars, topology, "smtp_from_name", "Email__FromName");
                    AddServiceKeyEnvVar(envVars, topology, "tenor_api_key", "Gif__ApiKey");
                }
                break;
            }
            case ImageKind.LiveKit:
            {
                var apiKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_api_key.result}}";
                var apiSecretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_api_secret.result}}";
                envVars.Add(("LIVEKIT_KEYS", $"{apiKeyRef}: {apiSecretRef}"));
                break;
            }
        }

        return envVars;
    }

    private static void AddServiceKeyEnvVar(
        List<(string Key, string Value)> envVars,
        Topology topology,
        string serviceKey,
        string envVarName)
    {
        if (topology.ServiceKeys.TryGetValue(serviceKey, out var value) && !string.IsNullOrEmpty(value))
            envVars.Add((envVarName, $"${{var.{serviceKey}}}"));
    }

    public static string? ResolveCommandOverride(Image image, HostEntry entry, WireResolver resolver)
    {
        if (image.Kind == ImageKind.Redis)
        {
            var hostName = SanitizeName(entry.Host.Name);
            var secretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_password.result}}";
            return $"redis-server --requirepass {secretRef}";
        }

        if (image.Kind == ImageKind.MinIO)
            return "server /data --console-address :9001";

        return null;
    }

    // --- Backup ---

    public static List<string> GenerateBackupCommands(List<Image> images, Container host)
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

            var retentionStr = image.Config.GetValueOrDefault("backupRetention", "");
            if (string.IsNullOrEmpty(retentionStr))
                retentionStr = host.Config.GetValueOrDefault("backupRetention", "");
            var retention = int.TryParse(retentionStr, out var r) ? r : 7;

            var containerName = SanitizeName(image.Name);
            var backupDir = $"/opt/backups/{containerName}";

            var backupCmd = image.Kind switch
            {
                ImageKind.PostgreSQL =>
                    $"docker exec {containerName} pg_dumpall -U postgres | gzip > {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).sql.gz",
                ImageKind.Redis =>
                    $"docker exec {containerName} redis-cli BGSAVE && sleep 2 && docker cp {containerName}:/data/dump.rdb {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).rdb",
                ImageKind.MinIO =>
                    $"docker run --rm --network xcord-bridge -v {backupDir}:/backup minio/mc mirror http://{containerName}:9000 /backup/{containerName}_$(date +%Y%m%d_%H%M%S)/",
                _ => null
            };

            if (backupCmd == null) continue;

            var scriptContent = $"#!/bin/bash\\n{backupCmd}\\nfind {backupDir} -type f -mtime +{retention} -delete\\nfind {backupDir} -type d -empty -delete";

            commands.Add($"mkdir -p {backupDir}");
            commands.Add($"printf '{scriptContent}\\n' > {backupDir}/backup.sh");
            commands.Add($"chmod +x {backupDir}/backup.sh");
            commands.Add($"(crontab -l 2>/dev/null; echo \\\"{schedule} {backupDir}/backup.sh\\\") | crontab -");
        }

        return commands;
    }

    // --- Caddyfile ---

    public static string GenerateCaddyfile(Container caddy, WireResolver resolver)
    {
        var upstreams = resolver.ResolveCaddyUpstreams(caddy);
        var domain = caddy.Config.GetValueOrDefault("domain", "{$DOMAIN}");

        var securityHeaders = new[]
        {
            "  header {",
            "    Strict-Transport-Security \"max-age=31536000; includeSubDomains; preload\"",
            "    X-Content-Type-Options \"nosniff\"",
            "    X-Frame-Options \"DENY\"",
            "    Referrer-Policy \"strict-origin-when-cross-origin\"",
            "    Permissions-Policy \"camera=(), microphone=(self), geolocation=(), payment=()\"",
            "  }"
        };

        // Each routable image gets its own subdomain block
        var blocks = new List<string>();
        foreach (var (image, subdomain) in upstreams)
        {
            var containerName = SanitizeName(image.Name);
            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
            var port = meta?.Ports.FirstOrDefault() ?? 80;
            var host = $"{subdomain}.{domain}";

            var block = new List<string> { $"{host} {{" };
            block.AddRange(securityHeaders);
            block.Add($"  reverse_proxy {containerName}:{port}");
            block.Add("}");
            blocks.Add(string.Join("\n", block));
        }

        return string.Join("\n\n", blocks);
    }

    // --- Utilities ---

    public static string SanitizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_')
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Aggregate("", (current, c) => current + c);

    public static string GetDefaultDockerImage(ImageKind kind) => kind switch
    {
        ImageKind.HubServer => "ghcr.io/xcord/hub:latest",
        ImageKind.FederationServer => "ghcr.io/xcord/fed:latest",
        ImageKind.Redis => "redis:7-alpine",
        ImageKind.PostgreSQL => "postgres:17-alpine",
        ImageKind.MinIO => "minio/minio:latest",
        ImageKind.LiveKit => "livekit/livekit-server:latest",
        ImageKind.Custom => "alpine:latest",
        _ => "alpine:latest"
    };

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
