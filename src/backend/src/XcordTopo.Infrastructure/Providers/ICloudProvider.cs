using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public interface ICloudProvider
{
    string Key { get; }
    ProviderInfo GetInfo();
    List<Region> GetRegions();
    List<ComputePlan> GetPlans();
    List<CredentialField> GetCredentialSchema();
    Dictionary<string, string> GenerateHcl(Topology topology);
}
