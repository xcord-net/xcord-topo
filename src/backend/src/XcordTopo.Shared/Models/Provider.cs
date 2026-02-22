namespace XcordTopo.Models;

public sealed class ProviderInfo
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedContainerKinds { get; set; } = [];
}

public sealed class Region
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public sealed class ComputePlan
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int VCpus { get; set; }
    public int MemoryMb { get; set; }
    public int DiskGb { get; set; }
    public decimal PriceMonthly { get; set; }
}
