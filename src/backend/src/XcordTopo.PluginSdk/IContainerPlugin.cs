namespace XcordTopo.PluginSdk;

/// <summary>
/// Defines a container type plugin. Designed but implementation deferred.
/// </summary>
public interface IContainerPlugin
{
    string TypeId { get; }
    string Label { get; }
    string Description { get; }
}
