using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class WireResolver
{
    private readonly Dictionary<Guid, object> _nodeById = new();
    private readonly Dictionary<Guid, Port> _portById = new();
    private readonly Dictionary<Guid, Guid> _portToNode = new();
    private readonly Dictionary<Guid, Container> _nodeToHost = new();
    private readonly List<Wire> _wires;

    public WireResolver(Topology topology)
    {
        _wires = topology.Wires;
        IndexContainers(topology.Containers, null);
    }

    private void IndexContainers(List<Container> containers, Container? hostAncestor)
    {
        foreach (var container in containers)
        {
            _nodeById[container.Id] = container;
            foreach (var port in container.Ports)
            {
                _portById[port.Id] = port;
                _portToNode[port.Id] = container.Id;
            }

            var currentHost = container.Kind == ContainerKind.Host ? container : hostAncestor;
            if (currentHost != null)
                _nodeToHost[container.Id] = currentHost;

            foreach (var image in container.Images)
            {
                _nodeById[image.Id] = image;
                foreach (var port in image.Ports)
                {
                    _portById[port.Id] = port;
                    _portToNode[port.Id] = image.Id;
                }
                if (currentHost != null)
                    _nodeToHost[image.Id] = currentHost;
            }

            IndexContainers(container.Children, currentHost);
        }
    }

    /// <summary>
    /// Find the single target node+port wired from this node's named output port.
    /// </summary>
    public (object Node, Port Port)? ResolveOutgoing(Guid nodeId, string portName)
    {
        var sourcePort = FindPort(nodeId, portName);
        if (sourcePort == null) return null;

        var wire = _wires.FirstOrDefault(w => w.FromPortId == sourcePort.Id);
        if (wire == null) return null;

        if (_nodeById.TryGetValue(wire.ToNodeId, out var targetNode) &&
            _portById.TryGetValue(wire.ToPortId, out var targetPort))
        {
            return (targetNode, targetPort);
        }
        return null;
    }

    /// <summary>
    /// Find all source nodes+ports wired into this node's named input port.
    /// </summary>
    public List<(object Node, Port Port)> ResolveIncoming(Guid nodeId, string portName)
    {
        var targetPort = FindPort(nodeId, portName);
        if (targetPort == null) return [];

        var results = new List<(object, Port)>();
        foreach (var wire in _wires.Where(w => w.ToPortId == targetPort.Id))
        {
            if (_nodeById.TryGetValue(wire.FromNodeId, out var sourceNode) &&
                _portById.TryGetValue(wire.FromPortId, out var sourcePort))
            {
                results.Add((sourceNode, sourcePort));
            }
        }
        return results;
    }

    /// <summary>
    /// Find the nearest Host ancestor for a node.
    /// </summary>
    public Container? FindHostFor(Guid nodeId) =>
        _nodeToHost.GetValueOrDefault(nodeId);

    /// <summary>
    /// Check whether two nodes share the same Host ancestor.
    /// </summary>
    public bool AreOnSameHost(Guid nodeId1, Guid nodeId2)
    {
        var host1 = FindHostFor(nodeId1);
        var host2 = FindHostFor(nodeId2);
        return host1 != null && host2 != null && host1.Id == host2.Id;
    }

    /// <summary>
    /// For a Caddy container, resolve all upstream images wired to it via its 'upstream' port.
    /// Returns (Image, upstreamPath) pairs.
    /// </summary>
    public List<(Image Image, string UpstreamPath)> ResolveCaddyUpstreams(Container caddyContainer)
    {
        var results = new List<(Image, string)>();

        // Caddy's images with upstreamPath config are the upstream targets
        foreach (var image in caddyContainer.Images)
        {
            var upstreamPath = image.Config.GetValueOrDefault("upstreamPath", "");
            if (!string.IsNullOrEmpty(upstreamPath))
                results.Add((image, upstreamPath));
        }

        return results;
    }

    /// <summary>
    /// Resolve the target image for a wire originating from a given port on a source image.
    /// Returns the target image and its port.
    /// </summary>
    public Image? ResolveWiredImage(Guid sourceImageId, string portName)
    {
        var result = ResolveOutgoing(sourceImageId, portName);
        return result?.Node as Image;
    }

    /// <summary>
    /// Get node object by ID.
    /// </summary>
    public object? GetNode(Guid nodeId) =>
        _nodeById.GetValueOrDefault(nodeId);

    private Port? FindPort(Guid nodeId, string portName)
    {
        if (!_nodeById.TryGetValue(nodeId, out var node)) return null;

        var ports = node switch
        {
            Container c => c.Ports,
            Image i => i.Ports,
            _ => null
        };

        return ports?.FirstOrDefault(p => p.Name == portName);
    }
}
