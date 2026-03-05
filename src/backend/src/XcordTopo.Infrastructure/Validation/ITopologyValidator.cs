using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Validation;

public sealed record TopologyValidationResult(List<TopologyValidationError> Items)
{
    public bool CanDeploy => Items.All(e => e.Severity != ValidationSeverity.Error);

    public List<TopologyValidationError> Errors =>
        Items.Where(i => i.Severity == ValidationSeverity.Error).ToList();

    public List<TopologyValidationError> Warnings =>
        Items.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
}

public interface ITopologyValidator
{
    List<string> Validate(Topology topology);
    TopologyValidationResult ValidateFull(Topology topology);
}
