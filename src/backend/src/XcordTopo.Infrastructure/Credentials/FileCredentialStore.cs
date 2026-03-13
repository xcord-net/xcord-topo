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

    private readonly string _basePath;
    private readonly ILogger<FileCredentialStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileCredentialStore(IOptions<DataOptions> options, ILogger<FileCredentialStore> logger)
    {
        _basePath = options.Value.BasePath;
        _logger = logger;
    }

    public async Task<CredentialStatus> GetStatusAsync(Guid topologyId, string providerKey, CancellationToken ct = default)
    {
        var filePath = GetFilePath(topologyId, providerKey);
        if (!File.Exists(filePath))
            return new CredentialStatus { HasCredentials = false };

        await MigrateKeysAsync(filePath, providerKey, ct);
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

    public async Task SaveAsync(Guid topologyId, string providerKey, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var filePath = GetFilePath(topologyId, providerKey);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

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

            _logger.LogDebug("Saved credentials for provider {Provider} topology {Id} to {Path}",
                providerKey, topologyId, filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, string>> GetRawVariablesAsync(Guid topologyId, string providerKey, CancellationToken ct = default)
    {
        var filePath = GetFilePath(topologyId, providerKey);
        if (!File.Exists(filePath))
            return new Dictionary<string, string>();
        await MigrateKeysAsync(filePath, providerKey, ct);
        return await ParseTfVarsAsync(filePath, ct);
    }

    /// <summary>
    /// Renames legacy tfvars keys to their current namespaced equivalents.
    /// Rewrites the file in-place so migration only happens once per topology.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> KeyMigrations = new()
    {
        ["aws"] = new() { ["region"] = "aws_region" },
        ["linode"] = new() { ["region"] = "linode_region" },
    };

    /// <summary>Keys that are no longer user-provided (e.g., Terraform now auto-generates SSH keys).</summary>
    private static readonly HashSet<string> ObsoleteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ssh_public_key", "ssh_private_key"
    };

    private async Task MigrateKeysAsync(string filePath, string providerKey, CancellationToken ct)
    {
        if (!File.Exists(filePath)) return;

        var variables = await ParseTfVarsAsync(filePath, ct);
        var changed = false;

        // Rename legacy keys
        if (KeyMigrations.TryGetValue(providerKey, out var renames))
        {
            foreach (var (oldKey, newKey) in renames)
            {
                if (variables.ContainsKey(oldKey))
                {
                    if (!variables.ContainsKey(newKey))
                        variables[newKey] = variables[oldKey];
                    variables.Remove(oldKey);
                    changed = true;
                }
            }
        }

        // Remove obsolete keys
        foreach (var key in ObsoleteKeys)
        {
            if (variables.Remove(key))
                changed = true;
        }

        if (changed)
        {
            var lines = variables.Select(kv => $"{kv.Key} = \"{EscapeTfValue(kv.Value)}\"");
            await File.WriteAllTextAsync(filePath, string.Join('\n', lines) + '\n', ct);
        }
    }

    private string GetFilePath(Guid topologyId, string providerKey) =>
        Path.Combine(_basePath, "deployments", topologyId.ToString(), "credentials", $"{providerKey}.tfvars");

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
