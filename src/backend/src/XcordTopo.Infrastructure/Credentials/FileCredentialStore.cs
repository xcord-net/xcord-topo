using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Credentials;

public sealed class FileCredentialStore : ICredentialStore
{
    private static readonly HashSet<string> SensitivePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "secret", "password", "key"
    };

    private readonly string _credentialsPath;
    private readonly ILogger<FileCredentialStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileCredentialStore(IOptions<DataOptions> options, ILogger<FileCredentialStore> logger)
    {
        _credentialsPath = Path.Combine(options.Value.BasePath, "credentials");
        Directory.CreateDirectory(_credentialsPath);
        _logger = logger;
    }

    public async Task<CredentialStatus> GetStatusAsync(string providerKey, CancellationToken ct = default)
    {
        var filePath = GetFilePath(providerKey);
        if (!File.Exists(filePath))
            return new CredentialStatus { HasCredentials = false };

        var variables = await ParseTfVarsAsync(filePath, ct);

        var status = new CredentialStatus
        {
            HasCredentials = variables.Count > 0,
            SetVariables = variables.Keys.ToList()
        };

        foreach (var (key, value) in variables)
        {
            if (!IsSensitive(key))
                status.NonSensitiveValues[key] = value;
        }

        return status;
    }

    public async Task SaveAsync(string providerKey, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var filePath = GetFilePath(providerKey);

            // Merge with existing variables (new values overwrite, empty values remove)
            var existing = File.Exists(filePath)
                ? await ParseTfVarsAsync(filePath, ct)
                : new Dictionary<string, string>();

            foreach (var (key, value) in variables)
            {
                if (string.IsNullOrEmpty(value))
                    existing.Remove(key);
                else
                    existing[key] = value;
            }

            var lines = existing.Select(kv => $"{kv.Key} = \"{EscapeTfValue(kv.Value)}\"");
            await File.WriteAllTextAsync(filePath, string.Join('\n', lines) + '\n', ct);

            _logger.LogDebug("Saved credentials for provider {Provider} to {Path}", providerKey, filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetFilePath(string providerKey) =>
        Path.Combine(_credentialsPath, $"{providerKey}.tfvars");

    internal static bool IsSensitive(string key) =>
        SensitivePatterns.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    internal static async Task<Dictionary<string, string>> ParseTfVarsAsync(string filePath, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = await File.ReadAllLinesAsync(filePath, ct);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var raw = trimmed[(eqIdx + 1)..].Trim();

            // Strip surrounding quotes
            if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                raw = raw[1..^1];

            // Unescape
            raw = raw.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");

            result[key] = raw;
        }

        return result;
    }

    internal static string EscapeTfValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
