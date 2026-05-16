using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

/// <summary>
/// Shared topology tree-walking helpers used by all cloud providers.
/// Extracted from LinodeProvider to break cross-provider coupling.
/// </summary>
public static partial class TopologyHelpers
{
    // --- Records ---

    public record HostEntry(Container Host);
    public record ComputePoolEntry(Container Pool, TierProfile TierProfile, int TargetTenants, string? SelectedPlanId = null)
    {
        /// <summary>
        /// Tier-qualified resource name: "compute_pool_free", "compute_pool_pro", etc.
        /// </summary>
        public string ResourceName => SanitizeName(Pool.Name) + "_" + SanitizeName(TierProfile.Id);
    }
    public record PoolSelection(string PoolName, string PlanId, int TargetTenants, string? TierProfileId = null);
    public record InfraSelection(string ImageName, string PlanId);
    public record SecretEntry(string ResourceName, string Description, int Length = 32);


    // --- Utilities ---

    public static string SanitizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_')
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Aggregate("", (current, c) => current + c);

    // Private-registry images (HubServer, FederationServer) are deployed via Terraform variable
    // substitution in GetDockerImageForHcl, which uses ${var.fed_version}/${var.hub_version} (default "0.1").
    // The `:latest` defaults below are only reached by non-HCL callers (manifest generation, descriptive
    // dispatch in DeploymentUnit) and are not the actual deployed image references.
    public static string GetDefaultDockerImage(ImageKind kind, string? registry = null) => kind switch
    {
        ImageKind.HubServer => $"{registry ?? "docker.xcord.net"}/hub:latest",
        ImageKind.FederationServer => $"{registry ?? "docker.xcord.net"}/fed:latest",
        ImageKind.Redis => "redis:7-alpine",
        ImageKind.PostgreSQL => "postgres:17-alpine",
        ImageKind.MinIO => "minio/minio:RELEASE.2025-02-28T09-55-16Z",
        ImageKind.LiveKit => "livekit/livekit-server:v1.8.3",
        ImageKind.Registry => "registry:2",
        ImageKind.Custom => "alpine:3.21",
        _ => "alpine:3.21"
    };

    public static string GetDefaultDockerImage(string typeId, string? registry, ImagePluginRegistry pluginRegistry)
    {
        var plugin = pluginRegistry.Get(typeId);
        if (plugin == null) return "alpine:3.21";
        var desc = plugin.GetDescriptor();
        var behavior = plugin.GetDockerBehavior();

        if (behavior.RequiresPrivateRegistry)
        {
            var reg = registry ?? "docker.xcord.net";
            var shortName = typeId switch
            {
                "HubServer" => "hub",
                "FederationServer" => "fed",
                _ => typeId.ToLowerInvariant()
            };
            return $"{reg}/{shortName}:latest";
        }

        return desc.DefaultDockerImage ?? "alpine:3.21";
    }

    public static bool RequiresPrivateRegistry(ImageKind kind) =>
        kind is ImageKind.HubServer or ImageKind.FederationServer;

    public static bool RequiresPrivateRegistry(string typeId, ImagePluginRegistry pluginRegistry) =>
        pluginRegistry.Get(typeId)?.GetDockerBehavior().RequiresPrivateRegistry ?? false;

    /// <summary>
    /// Collects all distinct version variable names for private-registry images in the topology.
    /// </summary>
    public static HashSet<string> CollectVersionVariables(Topology topology, ImagePluginRegistry pluginRegistry)
    {
        var vars = new HashSet<string>();
        void Walk(List<Container> containers)
        {
            foreach (var c in containers)
            {
                foreach (var img in c.Images)
                {
                    var typeId = img.ResolveTypeId();
                    if (RequiresPrivateRegistry(typeId, pluginRegistry))
                    {
                        var plugin = pluginRegistry.Get(typeId);
                        var versionVar = plugin?.GetDockerBehavior().VersionVariableName;
                        if (versionVar != null) vars.Add(versionVar);
                    }
                }
                Walk(c.Children);
            }
        }
        Walk(topology.Containers);
        return vars;
    }

    public static string GetDockerImageForHcl(Image image, string resolvedRegistry) =>
        GetDockerImageForHcl(image, resolvedRegistry, DefaultPlugins.CreateRegistry());

    public static string GetDockerImageForHcl(Image image, string resolvedRegistry, ImagePluginRegistry registry)
    {
        var plugin = registry.GetForImage(image);
        var behavior = plugin?.GetDockerBehavior();

        if (behavior is not { RequiresPrivateRegistry: true })
        {
            var desc = plugin?.GetDescriptor();
            var defaultImage = desc?.DefaultDockerImage ?? "alpine:3.21";
            var dockerImage = image.DockerImage ?? defaultImage;
            if (dockerImage.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
                return defaultImage;
            return dockerImage;
        }

        var versionVar = behavior.VersionVariableName ?? throw new ArgumentException(
            $"Plugin '{image.ResolveTypeId()}' requires private registry but has no VersionVariableName");
        var shortName = behavior.RegistryName ?? image.ResolveTypeId() switch
        {
            "HubServer" => "hub",
            "FederationServer" => "fed",
            _ => image.ResolveTypeId().ToLowerInvariant()
        };
        return $"${{var.registry_url}}/{shortName}:${{var.{versionVar}}}";
    }

    public static string GetVersionVariableName(ImageKind kind) => kind switch
    {
        ImageKind.HubServer => "hub_version",
        ImageKind.FederationServer => "fed_version",
        _ => throw new ArgumentException($"No version variable for image kind: {kind}")
    };

    public static string GetVersionVariableName(string typeId, ImagePluginRegistry pluginRegistry)
    {
        var plugin = pluginRegistry.GetRequired(typeId);
        return plugin.GetDockerBehavior().VersionVariableName
            ?? throw new ArgumentException($"No version variable for image type: {typeId}");
    }

    /// <summary>
    /// Generates the docker login command for use in provisioning scripts.
    /// Returns null if no auth is configured (registry_username is empty).
    /// Uses Terraform variable interpolation so credentials stay in tfvars, not HCL.
    /// </summary>
    public static string GenerateDockerLoginCommand(bool useSudo)
    {
        var sudo = useSudo ? "sudo " : "";
        return $"{sudo}bash -c 'if [ -n \\\"${{var.registry_username}}\\\" ]; then echo \\\"${{nonsensitive(var.registry_password)}}\\\" | docker login ${{var.registry_url}} -u \\\"${{var.registry_username}}\\\" --password-stdin; fi'";
    }

    /// <summary>
    /// Resolves the effective provider key for a container:
    /// container.Config["provider"] if set, otherwise topology-level provider.
    /// </summary>
    public static string ResolveProviderKey(Container container, Topology topology) =>
        container.Config.GetValueOrDefault("provider", topology.Provider);

    /// <summary>
    /// Collect all distinct provider keys used across a topology's containers.
    /// </summary>
    public static List<string> CollectActiveProviderKeys(Topology topology)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topology.Provider };
        CollectProviderKeysRecursive(topology.Containers, topology, keys);
        return keys.ToList();
    }

    private static void CollectProviderKeysRecursive(List<Container> containers, Topology topology, HashSet<string> keys)
    {
        foreach (var container in containers)
        {
            var providerOverride = container.Config.GetValueOrDefault("provider", "");
            if (!string.IsNullOrEmpty(providerOverride))
                keys.Add(providerOverride);
            CollectProviderKeysRecursive(container.Children, topology, keys);
        }
    }

    /// <summary>
    /// Generates a bash command that verifies a container started successfully.
    /// Checks that the container is running and has zero restart count after a brief settle period.
    /// Returns a quoted string with trailing comma suitable for Terraform provisioner inline arrays.
    /// </summary>
    public static string GenerateContainerHealthCheck(string containerName, bool sudo = true)
    {
        var s = sudo ? "sudo " : "";
        // Sleep lets the container settle, then verify running state and zero restarts.
        // Uses escaped double quotes for the --format argument to stay compatible with Terraform HCL
        // (Terraform only supports \", \\, \n, \r, \t escape sequences in double-quoted strings).
        // Escaping: C# {{{{ -> output {{ -> Terraform literal {{ -> shell {{ (Go template format).
        return $"\"{s}bash -c 'sleep 5 && [ \\\"$({s}docker inspect {containerName} --format \\\"{{{{.State.Running}}}}\\\" 2>/dev/null)\\\" = \\\"true\\\" ] && [ \\\"$({s}docker inspect {containerName} --format \\\"{{{{.RestartCount}}}}\\\" 2>/dev/null)\\\" = \\\"0\\\" ]'\",";
    }

    /// <summary>
    /// Generates a bash command with a retry loop for starting a container from a registry image.
    /// Registry images can fail on first pull due to port races. Retries 3 times with a brief pause.
    /// After the loop, a final inspect verifies the container is healthy (fails the provisioner if not).
    /// Returns a quoted string with trailing comma suitable for Terraform provisioner inline arrays.
    /// </summary>
    public static string GenerateRegistryRetryBlock(string containerName, string dockerImage, string extraFlags, bool sudo = true)
    {
        var s = sudo ? "sudo " : "";
        // for loop retries 3 times: rm old container, run new one, sleep, check, break on success.
        // Final docker inspect after done ensures failure if all retries exhausted.
        return $"\"{s}bash -c 'for i in 1 2 3; do {s}docker rm -f {containerName} 2>/dev/null || true; {s}docker run -d --name {containerName} {extraFlags} {dockerImage}; sleep 2; if [ \\\"$({s}docker inspect {containerName} --format \\\"{{{{.State.Running}}}}\\\" 2>/dev/null)\\\" = \\\"true\\\" ]; then break; fi; done; {s}docker inspect {containerName} --format \\\"{{{{.State.Running}}}}\\\" | grep -q true'\",";
    }
}
