using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Storage;

public interface ITopologyStore
{
    Task<List<Topology>> ListAsync(CancellationToken ct = default);
    Task<Topology?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Topology topology, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
