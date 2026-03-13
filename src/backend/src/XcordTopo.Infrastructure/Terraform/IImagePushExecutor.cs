using System.Threading.Channels;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Terraform;

public sealed record ImageBuildSpec(string RepoUrl, string GitRef, string RegistryName);

public interface IImagePushExecutor
{
    Task<ChannelReader<TerraformOutputLine>> ExecuteAsync(
        Guid topologyId,
        string registryUrl,
        string registryUsername,
        string registryPassword,
        IReadOnlyList<ImageBuildSpec> images,
        CancellationToken ct = default);

    ChannelReader<TerraformOutputLine>? GetOutputStream(Guid topologyId);
    void ReleaseOutputStream(Guid topologyId);
    bool IsRunning(Guid topologyId);
    void Cancel(Guid topologyId);
}
