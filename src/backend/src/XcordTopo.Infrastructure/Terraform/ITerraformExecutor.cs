using System.Threading.Channels;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Terraform;

public interface ITerraformExecutor
{
    Task<ChannelReader<TerraformOutputLine>> ExecuteAsync(
        Guid topologyId,
        TerraformCommand command,
        string providerKey = "linode",
        CancellationToken ct = default);

    ChannelReader<TerraformOutputLine>? GetOutputStream(Guid topologyId);
    bool IsRunning(Guid topologyId);
    void Cancel(Guid topologyId);
}
