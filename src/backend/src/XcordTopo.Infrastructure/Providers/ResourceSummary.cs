namespace XcordTopo.Infrastructure.Providers;

public sealed record ServiceDetail(string Name, string Kind, int RamMb);

public sealed record ResourceEntry(
    string Name,
    string Provider,
    string PlanId,
    string PlanLabel,
    int RamMb,
    int Count,
    decimal PricePerMonth,
    bool IsPool,
    string? TierProfileName = null,
    int? TenantsPerHost = null,
    List<ServiceDetail>? Services = null);

public sealed record PublicEndpoint(string Url, string Kind, string? Backend = null);

public sealed record ResourceSummary(
    List<ResourceEntry> Resources,
    List<PublicEndpoint> Endpoints,
    decimal TotalMonthly);
