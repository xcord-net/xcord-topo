using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Plugins;

public sealed class ImagePluginRegistry
{
    private readonly Dictionary<string, IImagePlugin> _plugins;

    public ImagePluginRegistry(IEnumerable<IImagePlugin> plugins)
    {
        _plugins = new Dictionary<string, IImagePlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (string.IsNullOrEmpty(plugin.TypeId)) continue;
            if (!_plugins.TryAdd(plugin.TypeId, plugin))
            {
                // Built-in wins over external on duplicate
                Console.WriteLine($"[PluginRegistry] Duplicate TypeId '{plugin.TypeId}' - keeping first registration");
            }
        }
    }

    public IImagePlugin? Get(string? typeId) =>
        typeId != null && _plugins.TryGetValue(typeId, out var plugin) ? plugin : null;

    public IImagePlugin GetRequired(string typeId) =>
        Get(typeId) ?? throw new InvalidOperationException($"No image plugin registered for type '{typeId}'");

    /// <summary>
    /// Resolve the plugin for an Image model, using TypeId with Kind fallback.
    /// </summary>
    public IImagePlugin? GetForImage(Image image) =>
        Get(image.ResolveTypeId());

    /// <summary>
    /// Get the descriptor for an image via its plugin. Returns null if no plugin found.
    /// </summary>
    public ImageDescriptor? GetDescriptor(Image image) =>
        GetForImage(image)?.GetDescriptor();

    /// <summary>
    /// Get the port numbers from an image's plugin descriptor.
    /// </summary>
    public int[] GetPorts(Image image)
    {
        var desc = GetDescriptor(image);
        return desc?.Ports.Select(p => p.Port).ToArray() ?? [];
    }

    public IReadOnlyList<IImagePlugin> GetAll() => _plugins.Values.ToList();

    public IReadOnlyList<CatalogEntry> GetCatalog() =>
        _plugins.Values.Select(p => p.GetCatalogEntry()).ToList();

    public int Count => _plugins.Count;
}
