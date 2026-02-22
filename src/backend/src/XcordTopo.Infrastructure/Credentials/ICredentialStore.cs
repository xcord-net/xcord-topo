using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Credentials;

public interface ICredentialStore
{
    Task<CredentialStatus> GetStatusAsync(string providerKey, CancellationToken ct = default);
    Task SaveAsync(string providerKey, Dictionary<string, string> variables, CancellationToken ct = default);
}
