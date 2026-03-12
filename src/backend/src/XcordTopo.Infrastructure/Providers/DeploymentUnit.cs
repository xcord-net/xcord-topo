using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Wraps both Images and Caddy containers uniformly as services within a deployment unit.
/// </summary>
public sealed record ServiceEntry(
    string Name,
    string Kind,
    string DockerImage,
    int RamMb,
    ImageScaling Scaling,
    object Source)
{
    /// <summary>
    /// Create a ServiceEntry from a topology Image node.
    /// RAM is looked up from ImageOperationalMetadata; defaults to 256 if not found.
    /// </summary>
    public static ServiceEntry FromImage(Image image, string? registry = null)
    {
        var ramMb = ImageOperationalMetadata.Images.TryGetValue(image.Kind, out var meta)
            ? meta.MinRamMb
            : 256;

        var dockerImage = image.DockerImage ?? TopologyHelpers.GetDefaultDockerImage(image.Kind, registry);

        return new ServiceEntry(
            Name: image.Name,
            Kind: image.Kind.ToString(),
            DockerImage: dockerImage,
            RamMb: ramMb,
            Scaling: image.Scaling,
            Source: image);
    }

    /// <summary>
    /// Create a ServiceEntry from a Caddy container.
    /// Uses CaddyContainerMetadata for RAM and docker image.
    /// </summary>
    public static ServiceEntry FromCaddy(Container container)
    {
        var caddy = ImageOperationalMetadata.Caddy;

        return new ServiceEntry(
            Name: container.Name,
            Kind: "Caddy",
            DockerImage: caddy.DockerImage,
            RamMb: caddy.MinRamMb,
            Scaling: ImageScaling.Shared,
            Source: container);
    }
}

/// <summary>
/// Base type for all deployment units produced by walking the topology tree.
/// </summary>
public abstract record DeploymentUnit(Container? Container, string ProviderKey);

/// <summary>
/// A single-host deployment unit (e.g. a VPS running one or more services).
/// </summary>
public sealed record InstanceUnit(
    Container? Container,
    string ProviderKey,
    List<ServiceEntry> Services,
    int MinReplicas,
    int MaxReplicas) : DeploymentUnit(Container, ProviderKey)
{
    public int TotalRamMb => Services.Sum(s => s.RamMb);
}

/// <summary>
/// A compute pool deployment unit for multi-tenant hosting.
/// </summary>
public sealed record PoolUnit(
    Container? Container,
    string ProviderKey,
    List<ServiceEntry> Services,
    TierProfile? TierProfile,
    int TargetTenants,
    string? SelectedPlanId) : DeploymentUnit(Container, ProviderKey);

/// <summary>
/// A DNS zone deployment unit.
/// </summary>
public sealed record DnsUnit(
    Container? Container,
    string ProviderKey,
    string Domain) : DeploymentUnit(Container, ProviderKey);
