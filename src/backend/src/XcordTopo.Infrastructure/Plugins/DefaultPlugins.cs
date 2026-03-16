using XcordTopo.Infrastructure.Plugins.Images;

namespace XcordTopo.Infrastructure.Plugins;

/// <summary>
/// Factory for creating the default ImagePluginRegistry with all built-in plugins.
/// Used for backward-compatible constructors and test setup.
/// </summary>
public static class DefaultPlugins
{
    public static ImagePluginRegistry CreateRegistry() => new(
    [
        new PostgreSqlImagePlugin(),
        new RedisImagePlugin(),
        new MinIOImagePlugin(),
        new HubServerImagePlugin(),
        new FederationServerImagePlugin(),
        new LiveKitImagePlugin(),
        new RegistryImagePlugin(),
        new CustomImagePlugin()
    ]);
}
