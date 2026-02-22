using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Validation;

public interface ITopologyValidator
{
    List<string> Validate(Topology topology);
}
