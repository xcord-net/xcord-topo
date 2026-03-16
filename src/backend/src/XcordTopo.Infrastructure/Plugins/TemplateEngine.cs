using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins;

/// <summary>
/// Resolves template expressions in plugin env var values, command overrides, etc.
/// Templates use {token:arg} syntax. Unknown templates pass through unchanged.
/// </summary>
public sealed class TemplateEngine
{
    /// <summary>
    /// Resolve all template expressions in a string.
    /// </summary>
    public string Resolve(string template, TemplateContext context)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains('{'))
            return template;

        var result = template;
        var startIdx = 0;
        while (startIdx < result.Length)
        {
            var open = result.IndexOf('{', startIdx);
            if (open < 0) break;

            var close = result.IndexOf('}', open + 1);
            if (close < 0) break;

            var token = result[(open + 1)..close];
            var resolved = ResolveToken(token, context);
            if (resolved != null)
            {
                result = result[..open] + resolved + result[(close + 1)..];
                startIdx = open + resolved.Length;
            }
            else
            {
                // Skip past this token if we can't resolve it
                // (might be a Terraform expression like ${var.x})
                startIdx = close + 1;
            }
        }

        return result;
    }

    private static string? ResolveToken(string token, TemplateContext context)
    {
        var parts = token.Split(':');
        return parts[0] switch
        {
            "secret" when parts.Length >= 2 => ResolveSecret(parts[1], context),
            "wire" when parts.Length >= 3 => ResolveWire(parts, context),
            "derived" when parts.Length >= 2 => ResolveDerived(parts[1], context),
            "registry" => context.Registry,
            "config" when parts.Length >= 2 => ResolveConfig(parts[1], context),
            "var" when parts.Length >= 2 => $"${{var.{parts[1]}}}",
            "serviceKey" when parts.Length >= 2 => context.ServiceKeyRef?.Invoke(parts[1]),
            "containerName" => context.ImageName,
            "backupDir" => context.BackupDir,
            "privateIp" => context.PrivateIp,
            _ => null  // Unknown token - leave as-is
        };
    }

    private static string ResolveSecret(string secretName, TemplateContext context)
    {
        var resourceName = $"{context.HostName}_{context.ImageName}_{secretName}";
        return $"${{nonsensitive(random_password.{resourceName}.result)}}";
    }

    private static string? ResolveWire(string[] parts, TemplateContext context)
    {
        // {wire:portName:host}, {wire:portName:port}, {wire:portName:secret:secretName}
        var portName = parts[1];
        var field = parts[2];

        if (context.ResolveWire == null) return null;

        var wire = context.ResolveWire(portName);
        if (wire == null) return null;

        return field switch
        {
            "host" => wire.Value.Host,
            "port" => wire.Value.Port.ToString(),
            "secret" when parts.Length >= 4 => wire.Value.SecretRef.Replace("password", parts[3]),
            _ => null
        };
    }

    private static string? ResolveDerived(string derivation, TemplateContext context)
    {
        return derivation switch
        {
            "dbName" => context.DerivedDbName ?? "app",
            _ => null
        };
    }

    private static string? ResolveConfig(string key, TemplateContext context)
    {
        return context.ImageConfig?.GetValueOrDefault(key);
    }
}

/// <summary>
/// Context for template resolution. Populated differently for HCL generation vs manifest generation.
/// </summary>
public sealed class TemplateContext
{
    public required string HostName { get; init; }
    public required string ImageName { get; init; }

    /// <summary>Resolve a wire port name to its connection details.</summary>
    public Func<string, (string Host, int Port, string SecretRef)?>? ResolveWire { get; init; }

    /// <summary>The topology's resolved registry URL.</summary>
    public string? Registry { get; init; }

    /// <summary>The image's Config dictionary.</summary>
    public IReadOnlyDictionary<string, string>? ImageConfig { get; init; }

    /// <summary>Derived DB name (from consumer plugin's DbNameWhenConsuming).</summary>
    public string? DerivedDbName { get; init; }

    /// <summary>Service key reference resolver (returns TF var ref or null if group not present).</summary>
    public Func<string, string?>? ServiceKeyRef { get; init; }

    /// <summary>Backup directory path (for backup command templates).</summary>
    public string? BackupDir { get; init; }

    /// <summary>Private IP for manifest resolution.</summary>
    public string? PrivateIp { get; init; }
}
