namespace XcordTopo.Infrastructure.Providers;

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IInfrastructureProvider> _providers;

    public ProviderRegistry(IEnumerable<IInfrastructureProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IInfrastructureProvider? Get(string key) =>
        _providers.GetValueOrDefault(key);

    public IReadOnlyList<IInfrastructureProvider> GetAll() =>
        _providers.Values.ToList();
}
