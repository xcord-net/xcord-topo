using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public interface IInfrastructureProvider
{
    string Key { get; }
    ProviderInfo GetInfo();
    List<Region> GetRegions();
    List<ComputePlan> GetPlans();
    Dictionary<string, string> GenerateHcl(Topology topology);
}
