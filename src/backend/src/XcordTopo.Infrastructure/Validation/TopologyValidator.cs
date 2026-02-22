using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Validation;

public sealed class TopologyValidator : ITopologyValidator
{
    public List<string> Validate(Topology topology)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(topology.Name))
            errors.Add("Topology name is required.");

        if (topology.Containers.Count == 0)
            errors.Add("Topology must have at least one container.");

        // Collect all node IDs and port IDs recursively
        var allPorts = new HashSet<Guid>();
        var allNodeIds = new HashSet<Guid>();

        ValidateContainers(topology.Containers, errors, allPorts, allNodeIds);

        foreach (var wire in topology.Wires)
        {
            if (!allNodeIds.Contains(wire.FromNodeId))
                errors.Add($"Wire {wire.Id} references non-existent source node {wire.FromNodeId}.");
            if (!allNodeIds.Contains(wire.ToNodeId))
                errors.Add($"Wire {wire.Id} references non-existent target node {wire.ToNodeId}.");
            if (!allPorts.Contains(wire.FromPortId))
                errors.Add($"Wire {wire.Id} references non-existent source port {wire.FromPortId}.");
            if (!allPorts.Contains(wire.ToPortId))
                errors.Add($"Wire {wire.Id} references non-existent target port {wire.ToPortId}.");
            if (wire.FromNodeId == wire.ToNodeId)
                errors.Add($"Wire {wire.Id} cannot connect a node to itself.");
        }

        // Check for duplicate wire connections
        var wireKeys = new HashSet<string>();
        foreach (var wire in topology.Wires)
        {
            var key = $"{wire.FromPortId}-{wire.ToPortId}";
            var reverseKey = $"{wire.ToPortId}-{wire.FromPortId}";
            if (!wireKeys.Add(key) || wireKeys.Contains(reverseKey))
                errors.Add($"Duplicate wire connection between ports {wire.FromPortId} and {wire.ToPortId}.");
        }

        return errors;
    }

    private static void ValidateContainers(List<Container> containers, List<string> errors,
        HashSet<Guid> allPorts, HashSet<Guid> allNodeIds)
    {
        foreach (var container in containers)
        {
            allNodeIds.Add(container.Id);
            foreach (var port in container.Ports)
                allPorts.Add(port.Id);

            foreach (var image in container.Images)
            {
                allNodeIds.Add(image.Id);
                foreach (var port in image.Ports)
                    allPorts.Add(port.Id);

                // Validate replicas config
                if (image.Config.TryGetValue("replicas", out var replicas) && !string.IsNullOrEmpty(replicas))
                {
                    if (!IsValidReplicaValue(replicas))
                        errors.Add($"Image '{image.Name}' has invalid replicas value '{replicas}'. Must be a positive integer or a $VARIABLE reference.");
                }
            }

            // Validate container has a name
            if (string.IsNullOrWhiteSpace(container.Name))
                errors.Add($"Container {container.Id} must have a name.");

            // Validate container dimensions
            if (container.Width <= 0 || container.Height <= 0)
                errors.Add($"Container '{container.Name}' must have positive dimensions.");

            // Validate FederationGroup instanceCount
            if (container.Kind == ContainerKind.FederationGroup)
            {
                if (container.Config.TryGetValue("instanceCount", out var instanceCount) && !string.IsNullOrEmpty(instanceCount))
                {
                    if (!IsValidReplicaValue(instanceCount))
                        errors.Add($"FederationGroup '{container.Name}' has invalid instanceCount value '{instanceCount}'. Must be a positive integer or a $VARIABLE reference.");
                }
            }

            // Recurse into children
            ValidateContainers(container.Children, errors, allPorts, allNodeIds);
        }
    }

    private static bool IsValidReplicaValue(string value)
    {
        // $VAR references are valid
        if (value.StartsWith('$') && value.Length > 1 && !value.Contains(' '))
            return true;

        // Must be a positive integer
        return int.TryParse(value, out var n) && n > 0;
    }
}
