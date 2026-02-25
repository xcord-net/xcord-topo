namespace XcordTopo.Models;

public sealed class TierProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, ImageResourceSpec> ImageSpecs { get; set; } = new();
}

public sealed class ImageResourceSpec
{
    public int MemoryMb { get; set; }
    public int CpuMillicores { get; set; }
    public int DiskMb { get; set; }
}
