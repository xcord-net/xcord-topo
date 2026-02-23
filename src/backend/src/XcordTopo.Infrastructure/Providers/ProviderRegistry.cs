namespace XcordTopo.Infrastructure.Providers;

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ICloudProvider> _providers;

    public ProviderRegistry(IEnumerable<ICloudProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public ICloudProvider? Get(string key) =>
        _providers.GetValueOrDefault(key);

    public IReadOnlyList<ICloudProvider> GetAll() =>
        _providers.Values.ToList();
}
