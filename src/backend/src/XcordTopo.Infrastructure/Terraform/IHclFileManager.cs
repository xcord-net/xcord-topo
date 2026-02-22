namespace XcordTopo.Infrastructure.Terraform;

public interface IHclFileManager
{
    string GetTerraformDirectory(Guid topologyId);
    Task WriteFilesAsync(Guid topologyId, Dictionary<string, string> files, CancellationToken ct = default);
    Task<Dictionary<string, string>> ReadFilesAsync(Guid topologyId, CancellationToken ct = default);
    Task<string?> ReadStateAsync(Guid topologyId, CancellationToken ct = default);
}
