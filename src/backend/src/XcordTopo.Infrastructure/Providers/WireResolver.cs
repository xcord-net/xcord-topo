using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class WireResolver
{
    private readonly Dictionary<Guid, object> _nodeById = new();
    private readonly Dictionary<Guid, Port> _portById = new();
    private readonly Dictionary<Guid, Guid> _portToNode = new();
    private readonly Dictionary<Guid, Container> _nodeToHost = new();
    private readonly Dictionary<Guid, Container> _nodeToPool = new();
    private readonly Dictionary<Guid, Container> _nodeToCaddy = new();
    private readonly List<Wire> _wires;

    public WireResolver(Topology topology)
    {
        _wires = topology.Wires;
        IndexContainers(topology.Containers, null, null, null);
    }

    private void IndexContainers(List<Container> containers, Container? hostAncestor, Container? poolAncestor, Container? caddyAncestor)
    {
        foreach (var container in containers)
        {
            _nodeById[container.Id] = container;
            foreach (var port in container.Ports)
            {
                _portById[port.Id] = port;
                _portToNode[port.Id] = container.Id;
            }

            // ComputePool is its own infrastructure boundary — stop host propagation,
            // start pool tracking. Pool images are deployed on separate pool instances.
            var currentPool = container.Kind == ContainerKind.ComputePool ? container : poolAncestor;
            var currentHost = container.Kind is ContainerKind.Host or ContainerKind.DataPool ? container
                : container.Kind == ContainerKind.ComputePool ? null
                : hostAncestor;
            var currentCaddy = container.Kind == ContainerKind.Caddy ? container : caddyAncestor;

            if (currentHost != null)
                _nodeToHost[container.Id] = currentHost;
            if (currentPool != null)
                _nodeToPool[container.Id] = currentPool;
            if (currentCaddy != null)
                _nodeToCaddy[container.Id] = currentCaddy;

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
                if (currentPool != null)
                    _nodeToPool[image.Id] = currentPool;
                if (currentCaddy != null)
                    _nodeToCaddy[image.Id] = currentCaddy;
            }

            IndexContainers(container.Children, currentHost, currentPool, currentCaddy);
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
    /// Find the nearest ComputePool ancestor for a node.
    /// </summary>
    public Container? FindPoolFor(Guid nodeId) =>
        _nodeToPool.GetValueOrDefault(nodeId);

    /// <summary>
    /// Find the nearest Caddy ancestor for a node.
    /// </summary>
    public Container? FindCaddyFor(Guid nodeId) =>
        _nodeToCaddy.GetValueOrDefault(nodeId);

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
    /// For a Caddy container, resolve all HTTP-routable images in its subtree.
    /// Includes direct images and images inside child containers (e.g., Hub under its own Host).
    /// Subdomain is derived from image kind (not user-configured).
    /// </summary>
    public List<(Image Image, string Subdomain)> ResolveCaddyUpstreams(Container caddyContainer)
    {
        var results = new List<(Image, string)>();
        CollectRoutableImages(caddyContainer, results);
        return results;
    }

    private void CollectRoutableImages(Container container, List<(Image, string)> results)
    {
        foreach (var image in container.Images)
        {
            var subdomain = GetSubdomain(image);
            if (subdomain != null)
                results.Add((image, subdomain));
        }

        foreach (var child in container.Children)
        {
            // Skip pool children — their infrastructure is deferred (count=0 on initial deploy).
            // Hub configures routing to pools at runtime via Caddy admin API.
            if (child.Kind is ContainerKind.ComputePool or ContainerKind.DataPool)
                continue;
            CollectRoutableImages(child, results);
        }
    }

    private static string? GetSubdomain(Image image) => image.Kind switch
    {
        ImageKind.HubServer => "www",
        ImageKind.FederationServer => "*",
        ImageKind.Custom => ValidateSubdomain(image.Config.GetValueOrDefault("subdomain")),
        _ => DeriveSubdomainFromMetadata(image)
    };

    /// <summary>
    /// For images with IsPublicEndpoint metadata, derive a subdomain from the image name.
    /// This keeps all public endpoint routing consistent — no special domain config needed.
    /// </summary>
    private static string? DeriveSubdomainFromMetadata(Image image)
    {
        var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
        if (meta is not { IsPublicEndpoint: true }) return null;
        var name = image.Name.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        return ValidateSubdomain(name);
    }

    private static string? ValidateSubdomain(string? subdomain)
    {
        if (string.IsNullOrEmpty(subdomain)) return null;
        if (subdomain.Length > 63) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain, @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            return null;
        return subdomain;
    }

    /// <summary>
    /// Resolve the target image wired to a given port on a source image.
    /// Checks both directions — wires can be drawn from either end.
    /// </summary>
    public Image? ResolveWiredImage(Guid sourceImageId, string portName)
    {
        var outgoing = ResolveOutgoing(sourceImageId, portName);
        if (outgoing?.Node is Image outImg) return outImg;

        var incoming = ResolveIncoming(sourceImageId, portName);
        return incoming.Select(r => r.Node).OfType<Image>().FirstOrDefault();
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
