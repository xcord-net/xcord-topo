namespace XcordTopo.Infrastructure.Manifest;

using System.Text.Json;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

public sealed class ManifestGenerator
{
    public (PublicManifest Public, GatewayTopologySection Gateway) Generate(
        Topology topology, string? terraformStateJson)
    {
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outputs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (terraformStateJson != null)
        {
            ParseTerraformState(terraformStateJson, secrets, outputs);
        }

        var wireResolver = new WireResolver(topology);
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology);
        var dnsContainers = TopologyHelpers.CollectDnsContainers(topology.Containers);

        var publicManifest = new PublicManifest();
        var gateway = new GatewayTopologySection();

        foreach (var poolEntry in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(poolEntry.Pool.Name);
            var tier = poolEntry.Pool.Config.GetValueOrDefault("tierProfile", "free");

            // Find the host that contains this compute pool
            var parentHost = FindParentHost(poolEntry.Pool, hosts);
            var hostName = parentHost != null ? TopologyHelpers.SanitizeName(parentHost.Host.Name) : poolName;
            var images = parentHost != null ? TopologyHelpers.CollectImages(parentHost.Host) : [];

            var publicIps = ResolvePublicIps(hostName, outputs);
            var privateIp = ResolveOutput(hostName + "_private_ip", outputs) ?? publicIps.FirstOrDefault()
                ?? throw new InvalidOperationException($"No IP resolved for pool host '{hostName}'. Terraform state may be missing or incomplete.");

            // Find service images
            var pgImage = images.FirstOrDefault(i => i.Kind == ImageKind.PostgreSQL);
            var redisImage = images.FirstOrDefault(i => i.Kind == ImageKind.Redis);
            var minioImage = images.FirstOrDefault(i => i.Kind == ImageKind.MinIO);
            var livekitImage = images.FirstOrDefault(i => i.Kind == ImageKind.LiveKit);

            // Get capacity from tier profile
            var fedSpec = poolEntry.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer");
            var memoryPerTenant = fedSpec?.MemoryMb ?? 256;
            var cpuPerTenant = fedSpec?.CpuMillicores ?? 250;
            var tenantSlots = int.TryParse(poolEntry.Pool.Config.GetValueOrDefault("tenantSlots", "0"), out var ts) ? ts : 0;

            // Public manifest entry
            publicManifest.ComputePools.Add(new PublicPoolEntry
            {
                Name = poolName,
                Tier = tier,
                Region = poolEntry.Pool.Config.GetValueOrDefault("region", ""),
                PublicIps = publicIps,
                TenantSlots = tenantSlots,
                MemoryMbPerTenant = memoryPerTenant,
                CpuMillicoresPerTenant = cpuPerTenant
            });

            // Gateway entry with secrets
            gateway.ComputePools.Add(new GatewayPoolEntry
            {
                Name = poolName,
                Tier = tier,
                Database = BuildDatabaseEntry(pgImage, hostName, privateIp, secrets),
                Redis = BuildRedisEntry(redisImage, hostName, privateIp, secrets),
                Storage = BuildStorageEntry(minioImage, hostName, privateIp, secrets),
                Docker = new GatewayDockerEntry
                {
                    SocketProxyUrl = $"https://{privateIp}:2376",
                    InstanceImage = TopologyHelpers.GetDefaultDockerImage(ImageKind.FederationServer, TopologyHelpers.ResolveRegistry(topology))
                },
                Caddy = new GatewayCaddyEntry { AdminUrl = $"https://{privateIp}:2019" },
                LiveKit = BuildLiveKitEntry(livekitImage, hostName, privateIp, secrets),
                Capacity = new GatewayCapacityEntry
                {
                    TenantSlots = tenantSlots,
                    MemoryMbPerTenant = memoryPerTenant,
                    CpuMillicoresPerTenant = cpuPerTenant
                }
            });

            gateway.PublicIpsByPool[poolName] = publicIps;
        }

        // DNS
        var dnsContainer = dnsContainers.FirstOrDefault();
        if (dnsContainer != null)
        {
            var provider = dnsContainer.Config.GetValueOrDefault("provider", "");
            var zoneId = dnsContainer.Config.GetValueOrDefault("zoneId", "");
            var domain = dnsContainer.Config.GetValueOrDefault("domain", "");
            var apiToken = dnsContainer.Config.GetValueOrDefault("apiToken", "");

            publicManifest.Dns = new ManifestDns
            {
                Provider = provider,
                ZoneId = zoneId,
                DomainName = domain
            };
            publicManifest.Domain = new ManifestDomain { BaseDomain = domain };

            gateway.Dns = new GatewayDnsEntry
            {
                Provider = provider,
                ZoneId = zoneId,
                ApiToken = apiToken,
                BaseDomain = domain
            };
        }

        return (publicManifest, gateway);
    }

    private static void ParseTerraformState(
        string stateJson,
        Dictionary<string, string> secrets,
        Dictionary<string, JsonElement> outputs)
    {
        using var doc = JsonDocument.Parse(stateJson);
        var root = doc.RootElement;

        // Parse random_password resources
        if (root.TryGetProperty("resources", out var resources) &&
            resources.ValueKind == JsonValueKind.Array)
        {
            foreach (var resource in resources.EnumerateArray())
            {
                if (!resource.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "random_password")
                    continue;

                if (!resource.TryGetProperty("name", out var nameProp))
                    continue;

                var resourceName = nameProp.GetString();
                if (string.IsNullOrEmpty(resourceName))
                    continue;

                if (!resource.TryGetProperty("instances", out var instances) ||
                    instances.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var instance in instances.EnumerateArray())
                {
                    if (!instance.TryGetProperty("attributes", out var attrs))
                        continue;

                    if (!attrs.TryGetProperty("result", out var resultProp))
                        continue;

                    var value = resultProp.GetString();
                    if (value != null)
                        secrets[resourceName] = value;

                    break; // Only first instance
                }
            }
        }

        // Parse outputs
        if (root.TryGetProperty("outputs", out var outputsElement) &&
            outputsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var output in outputsElement.EnumerateObject())
            {
                if (output.Value.TryGetProperty("value", out var value))
                    outputs[output.Name] = value.Clone();
            }
        }
    }

    private static TopologyHelpers.HostEntry? FindParentHost(
        Container pool, List<TopologyHelpers.HostEntry> hosts)
    {
        foreach (var hostEntry in hosts)
        {
            if (ContainsContainer(hostEntry.Host, pool.Id))
                return hostEntry;
        }
        return null;
    }

    private static bool ContainsContainer(Container parent, Guid targetId)
    {
        foreach (var child in parent.Children)
        {
            if (child.Id == targetId)
                return true;
            if (ContainsContainer(child, targetId))
                return true;
        }
        return false;
    }

    private static List<string> ResolvePublicIps(string hostName, Dictionary<string, JsonElement> outputs)
    {
        // Try array form first
        var arrayKey = hostName + "_public_ips";
        if (outputs.TryGetValue(arrayKey, out var arrayVal) &&
            arrayVal.ValueKind == JsonValueKind.Array)
        {
            var ips = new List<string>();
            foreach (var item in arrayVal.EnumerateArray())
            {
                var ip = item.GetString();
                if (!string.IsNullOrEmpty(ip))
                    ips.Add(ip);
            }
            if (ips.Count > 0) return ips;
        }

        // Fall back to single IP
        var singleKey = hostName + "_public_ip";
        if (outputs.TryGetValue(singleKey, out var singleVal))
        {
            var ip = singleVal.ValueKind == JsonValueKind.String
                ? singleVal.GetString()
                : null;
            if (!string.IsNullOrEmpty(ip))
                return [ip];
        }

        return [];
    }

    private static string? ResolveOutput(string key, Dictionary<string, JsonElement> outputs)
    {
        if (!outputs.TryGetValue(key, out var val))
            return null;

        return val.ValueKind == JsonValueKind.String ? val.GetString() : null;
    }

    private static GatewayDatabaseEntry BuildDatabaseEntry(
        Image? pgImage, string hostName, string privateIp, Dictionary<string, string> secrets)
    {
        if (pgImage == null)
            return new GatewayDatabaseEntry();

        var imgName = TopologyHelpers.SanitizeName(pgImage.Name);
        var secretKey = $"{hostName}_{imgName}_password";
        if (!secrets.TryGetValue(secretKey, out var password))
            throw new InvalidOperationException($"Missing secret '{secretKey}' for PostgreSQL. Terraform state may be incomplete.");

        return new GatewayDatabaseEntry
        {
            ConnectionString = $"Host={privateIp};Port=5432;Database=postgres;Username=postgres;Password={password}"
        };
    }

    private static GatewayRedisEntry BuildRedisEntry(
        Image? redisImage, string hostName, string privateIp, Dictionary<string, string> secrets)
    {
        if (redisImage == null)
            return new GatewayRedisEntry();

        var imgName = TopologyHelpers.SanitizeName(redisImage.Name);
        var secretKey = $"{hostName}_{imgName}_password";
        if (!secrets.TryGetValue(secretKey, out var password))
            throw new InvalidOperationException($"Missing secret '{secretKey}' for Redis. Terraform state may be incomplete.");

        return new GatewayRedisEntry
        {
            ConnectionString = $"{privateIp}:6379,password={password}"
        };
    }

    private static GatewayStorageEntry BuildStorageEntry(
        Image? minioImage, string hostName, string privateIp, Dictionary<string, string> secrets)
    {
        if (minioImage == null)
            return new GatewayStorageEntry();

        var imgName = TopologyHelpers.SanitizeName(minioImage.Name);
        var accessKeyName = $"{hostName}_{imgName}_access_key";
        var secretKeyName = $"{hostName}_{imgName}_secret_key";
        if (!secrets.TryGetValue(accessKeyName, out var accessKey))
            throw new InvalidOperationException($"Missing secret '{accessKeyName}' for MinIO. Terraform state may be incomplete.");
        if (!secrets.TryGetValue(secretKeyName, out var secretKey))
            throw new InvalidOperationException($"Missing secret '{secretKeyName}' for MinIO. Terraform state may be incomplete.");

        return new GatewayStorageEntry
        {
            Endpoint = $"https://{privateIp}:9000",
            AccessKey = accessKey,
            SecretKey = secretKey,
            UseSsl = true
        };
    }

    private static GatewayLiveKitEntry BuildLiveKitEntry(
        Image? livekitImage, string hostName, string privateIp, Dictionary<string, string> secrets)
    {
        if (livekitImage == null)
            return new GatewayLiveKitEntry();

        var imgName = TopologyHelpers.SanitizeName(livekitImage.Name);
        var apiKeyName = $"{hostName}_{imgName}_api_key";
        var apiSecretName = $"{hostName}_{imgName}_api_secret";
        if (!secrets.TryGetValue(apiKeyName, out var apiKey))
            throw new InvalidOperationException($"Missing secret '{apiKeyName}' for LiveKit. Terraform state may be incomplete.");
        if (!secrets.TryGetValue(apiSecretName, out var apiSecret))
            throw new InvalidOperationException($"Missing secret '{apiSecretName}' for LiveKit. Terraform state may be incomplete.");

        return new GatewayLiveKitEntry
        {
            Host = $"wss://{privateIp}:7880",
            ApiKey = apiKey,
            ApiSecret = apiSecret
        };
    }
}
