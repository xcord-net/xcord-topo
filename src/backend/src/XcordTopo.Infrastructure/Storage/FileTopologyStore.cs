using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Storage;

public sealed class FileTopologyStore : ITopologyStore
{
    private readonly string _topologiesPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<FileTopologyStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileTopologyStore(IOptions<DataOptions> options, ILogger<FileTopologyStore> logger)
    {
        _topologiesPath = Path.Combine(options.Value.BasePath, "topologies");
        Directory.CreateDirectory(_topologiesPath);
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<List<Topology>> ListAsync(CancellationToken ct = default)
    {
        var topologies = new List<Topology>();
        if (!Directory.Exists(_topologiesPath)) return topologies;

        foreach (var file in Directory.GetFiles(_topologiesPath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var topology = JsonSerializer.Deserialize<Topology>(json, _jsonOptions);
                if (topology is not null) topologies.Add(topology);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read topology file: {File}", file);
            }
        }

        return topologies.OrderByDescending(t => t.UpdatedAt).ToList();
    }

    public async Task<Topology?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_topologiesPath, $"{id}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<Topology>(json, _jsonOptions);
    }

    public async Task SaveAsync(Topology topology, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            topology.UpdatedAt = DateTimeOffset.UtcNow;
            var filePath = Path.Combine(_topologiesPath, $"{topology.Id}.json");
            var json = JsonSerializer.Serialize(topology, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
            _logger.LogDebug("Saved topology {Id} to {Path}", topology.Id, filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_topologiesPath, $"{id}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted topology {Id}", id);
        }
        return Task.CompletedTask;
    }
}
