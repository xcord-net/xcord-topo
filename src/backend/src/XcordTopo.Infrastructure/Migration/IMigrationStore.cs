using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Migration;

public interface IMigrationStore
{
    Task<MigrationPlan?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(MigrationPlan plan, CancellationToken ct = default);
    Task<List<MigrationPlan>> ListAsync(CancellationToken ct = default);
}
