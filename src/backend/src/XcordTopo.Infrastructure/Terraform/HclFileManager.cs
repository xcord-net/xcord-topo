using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Storage;

namespace XcordTopo.Infrastructure.Terraform;

public sealed class HclFileManager : IHclFileManager
{
    private readonly string _basePath;
    private readonly ILogger<HclFileManager> _logger;

    public HclFileManager(IOptions<DataOptions> options, ILogger<HclFileManager> logger)
    {
        _basePath = Path.Combine(options.Value.BasePath, "terraform");
        _logger = logger;
    }

    public string GetTerraformDirectory(Guid topologyId)
    {
        var dir = Path.Combine(_basePath, topologyId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task WriteFilesAsync(Guid topologyId, Dictionary<string, string> files, CancellationToken ct = default)
    {
        var dir = GetTerraformDirectory(topologyId);
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(dir, fileName);
            await File.WriteAllTextAsync(filePath, content, ct);
            _logger.LogDebug("Wrote {File} for topology {Id}", fileName, topologyId);
        }
    }

    public async Task<Dictionary<string, string>> ReadFilesAsync(Guid topologyId, CancellationToken ct = default)
    {
        var dir = GetTerraformDirectory(topologyId);
        var files = new Dictionary<string, string>();
        if (!Directory.Exists(dir)) return files;

        foreach (var file in Directory.GetFiles(dir, "*.tf"))
        {
            var content = await File.ReadAllTextAsync(file, ct);
            files[Path.GetFileName(file)] = content;
        }

        return files;
    }

    public async Task<string?> ReadStateAsync(Guid topologyId, CancellationToken ct = default)
    {
        var statePath = Path.Combine(GetTerraformDirectory(topologyId), "terraform.tfstate");
        if (!File.Exists(statePath)) return null;
        return await File.ReadAllTextAsync(statePath, ct);
    }
}
