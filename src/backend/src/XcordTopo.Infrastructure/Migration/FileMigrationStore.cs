using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Migration;

public sealed class FileMigrationStore : IMigrationStore
{
    private readonly string _migrationsPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<FileMigrationStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileMigrationStore(IOptions<DataOptions> options, ILogger<FileMigrationStore> logger)
    {
        _migrationsPath = Path.Combine(options.Value.BasePath, "migrations");
        Directory.CreateDirectory(_migrationsPath);
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<MigrationPlan?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_migrationsPath, $"{id}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<MigrationPlan>(json, _jsonOptions);
    }

    public async Task SaveAsync(MigrationPlan plan, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var filePath = Path.Combine(_migrationsPath, $"{plan.Id}.json");
            var json = JsonSerializer.Serialize(plan, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
            _logger.LogDebug("Saved migration plan {Id} to {Path}", plan.Id, filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<MigrationPlan>> ListAsync(CancellationToken ct = default)
    {
        var plans = new List<MigrationPlan>();
        if (!Directory.Exists(_migrationsPath)) return plans;

        foreach (var file in Directory.GetFiles(_migrationsPath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var plan = JsonSerializer.Deserialize<MigrationPlan>(json, _jsonOptions);
                if (plan is not null) plans.Add(plan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read migration plan file: {File}", file);
            }
        }

        return plans.OrderByDescending(p => p.CreatedAt).ToList();
    }
}
