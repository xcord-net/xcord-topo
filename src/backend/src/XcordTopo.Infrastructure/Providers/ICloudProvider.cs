using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public interface ICloudProvider
{
    string Key { get; }
    ProviderInfo GetInfo();
    List<Region> GetRegions();
    List<ComputePlan> GetPlans();
    List<CredentialField> GetCredentialSchema();
    Dictionary<string, string> GenerateHcl(
        Topology topology, List<TopologyHelpers.PoolSelection>? poolSelections = null);

    /// <summary>
    /// Generate HCL files for only the containers owned by this provider.
    /// Used in multi-provider topologies where each provider generates its own resources.
    /// </summary>
    Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null);
}
