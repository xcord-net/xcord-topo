namespace XcordTopo.Infrastructure.Manifest;

/// <summary>
/// Non-sensitive manifest — safe to commit to git.
/// Contains pool structure, IPs, tiers, capacity — no passwords.
/// </summary>
public sealed class PublicManifest
{
    public List<PublicPoolEntry> ComputePools { get; set; } = [];
    public List<PublicDedicatedEntry> DedicatedHosts { get; set; } = [];
    public ManifestDns? Dns { get; set; }
    public ManifestDomain? Domain { get; set; }
}

public sealed class PublicPoolEntry
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "free";
    public string Region { get; set; } = string.Empty;
    public List<string> PublicIps { get; set; } = [];
    public int TenantSlots { get; set; }
    public int MemoryMbPerTenant { get; set; }
    public int CpuMillicoresPerTenant { get; set; }
}

public sealed class PublicDedicatedEntry
{
    public string Id { get; set; } = string.Empty;
    public string Tier { get; set; } = "enterprise";
    public List<string> PublicIps { get; set; } = [];
}

public sealed class ManifestDns
{
    public string Provider { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
}

public sealed class ManifestDomain
{
    public string BaseDomain { get; set; } = string.Empty;
}

/// <summary>
/// Sensitive gateway topology section — injected into gateway.json Docker secret.
/// Contains per-pool credentials, connection strings, and endpoint URLs.
/// Mirrors the hub's TopologyOptions schema exactly.
/// </summary>
public sealed class GatewayTopologySection
{
    public List<GatewayPoolEntry> ComputePools { get; set; } = [];
    public List<GatewayDedicatedEntry> DedicatedHosts { get; set; } = [];
    public GatewayDnsEntry? Dns { get; set; }
    public Dictionary<string, List<string>> PublicIpsByPool { get; set; } = new();
}

public sealed class GatewayPoolEntry
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "free";
    public GatewayDatabaseEntry Database { get; set; } = new();
    public GatewayRedisEntry Redis { get; set; } = new();
    public GatewayStorageEntry Storage { get; set; } = new();
    public GatewayDockerEntry Docker { get; set; } = new();
    public GatewayCaddyEntry Caddy { get; set; } = new();
    public GatewayLiveKitEntry LiveKit { get; set; } = new();
    public GatewayCapacityEntry Capacity { get; set; } = new();
}

public sealed class GatewayDedicatedEntry
{
    public string Id { get; set; } = string.Empty;
    public string Tier { get; set; } = "enterprise";
    public GatewayDatabaseEntry Database { get; set; } = new();
    public GatewayRedisEntry Redis { get; set; } = new();
    public GatewayStorageEntry Storage { get; set; } = new();
    public GatewayDockerEntry Docker { get; set; } = new();
    public GatewayCaddyEntry Caddy { get; set; } = new();
    public GatewayLiveKitEntry LiveKit { get; set; } = new();
}

public sealed class GatewayDnsEntry
{
    public string Provider { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string BaseDomain { get; set; } = string.Empty;
}

public sealed class GatewayDatabaseEntry { public string ConnectionString { get; set; } = string.Empty; }
public sealed class GatewayRedisEntry { public string ConnectionString { get; set; } = string.Empty; }
public sealed class GatewayStorageEntry
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
}
public sealed class GatewayDockerEntry
{
    public string SocketProxyUrl { get; set; } = string.Empty;
    public string InstanceImage { get; set; } = string.Empty;
}
public sealed class GatewayCaddyEntry { public string AdminUrl { get; set; } = string.Empty; }
public sealed class GatewayLiveKitEntry
{
    public string Host { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
public sealed class GatewayCapacityEntry
{
    public int TenantSlots { get; set; }
    public int MemoryMbPerTenant { get; set; }
    public int CpuMillicoresPerTenant { get; set; }
}
