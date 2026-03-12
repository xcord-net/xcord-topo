using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Credentials;

public interface ICredentialStore
{
    Task<CredentialStatus> GetStatusAsync(Guid topologyId, string providerKey, CancellationToken ct = default);
    Task SaveAsync(Guid topologyId, string providerKey, Dictionary<string, string> variables, CancellationToken ct = default);
}
