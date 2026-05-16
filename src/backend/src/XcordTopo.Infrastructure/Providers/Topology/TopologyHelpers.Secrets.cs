using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
    // --- Secret helpers ---

    public static List<SecretEntry> CollectSecrets(
        HostEntry entry, WireResolver resolver, bool excludePools = false) =>
        CollectSecrets(entry, resolver, DefaultPlugins.CreateRegistry(), excludePools);

    public static List<SecretEntry> CollectSecrets(
        HostEntry entry, WireResolver resolver, ImagePluginRegistry registry, bool excludePools = false)
    {
        var secrets = new List<SecretEntry>();
        var hostName = SanitizeName(entry.Host.Name);
        var images = excludePools
            ? CollectImagesExcludingPools(entry.Host)
            : CollectImages(entry.Host);

        foreach (var image in images)
        {
            var plugin = registry.GetForImage(image);
            if (plugin == null) continue;
            var imgName = SanitizeName(image.Name);
            foreach (var secret in plugin.GetSecrets())
            {
                secrets.Add(new($"{hostName}_{imgName}_{secret.Name}", $"{secret.Description} for {image.Name}", secret.Length));
            }
        }
        return secrets;
    }

    /// <summary>
    /// Collect secrets needed for a compute pool's images (data-driven, not hardcoded).
    /// </summary>
    public static List<SecretEntry> CollectPoolSecrets(ComputePoolEntry pool) =>
        CollectPoolSecrets(pool, DefaultPlugins.CreateRegistry());

    public static List<SecretEntry> CollectPoolSecrets(ComputePoolEntry pool, ImagePluginRegistry registry)
    {
        var secrets = new List<SecretEntry>();
        var poolName = SanitizeName(pool.Pool.Name);
        foreach (var image in pool.Pool.Images)
        {
            var plugin = registry.GetForImage(image);
            if (plugin == null) continue;
            var imgName = SanitizeName(image.Name);
            foreach (var secret in plugin.GetSecrets())
            {
                secrets.Add(new($"{poolName}_{imgName}_{secret.Name}", $"{secret.Description} for {image.Name}", secret.Length));
            }
        }
        return secrets;
    }

    /// <summary>
    /// Generate a docker service create command for a pool image.
    /// Returns null if the image kind has no metadata or docker image.
    /// </summary>
    public static string? GenerateSwarmServiceCommand(
        Image image, string poolName, WireResolver resolver, bool useSudo,
        IReadOnlyList<Image>? poolImages = null) =>
        GenerateSwarmServiceCommand(image, poolName, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine(), useSudo, poolImages);

    public static string? GenerateSwarmServiceCommand(
        Image image, string poolName, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine, bool useSudo,
        IReadOnlyList<Image>? poolImages = null)
    {
        if (image.Scaling == ImageScaling.PerTenant)
            return null;

        var plugin = registry.GetForImage(image);
        if (plugin == null) return null;
        var desc = plugin.GetDescriptor();

        var dockerImage = GetDockerImageForHcl(image, "", registry);
        if (string.IsNullOrEmpty(dockerImage))
            return null;

        var imgName = SanitizeName(image.Name);
        var sudo = useSudo ? "sudo " : "";
        // Each pool gets its own isolated overlay network named xcord-pool-{poolName}.
        // Instances deployed into this pool join xcord-pool-{poolName}, not the shared network,
        // so instances across different pools cannot reach each other directly.
        var poolNetworkName = $"xcord-pool-{poolName}";
        var parts = new List<string>
        {
            $"{sudo}docker service create",
            $"--name shared-{imgName}",
            "--replicas 1",
            $"--network {poolNetworkName}"
        };

        if (desc.MountPath != null)
        {
            var volumeName = $"{imgName}data";
            parts.Add($"--mount type=volume,source={volumeName},target={desc.MountPath}");
        }

        // Use template engine for env vars
        var templates = plugin.GetEnvVarTemplates();
        var templateContext = new TemplateContext
        {
            HostName = poolName,
            ImageName = imgName,
            ResolveWire = portName =>
            {
                var target = resolver.ResolveWiredImage(image.Id, portName);
                if (target == null) return null;
                var targetName = SanitizeName(target.Name);
                var targetDesc = registry.GetDescriptor(target);
                var port = targetDesc?.Ports.FirstOrDefault()?.Port ?? 80;
                var secretRef = $"${{nonsensitive(random_password.{poolName}_{targetName}_password.result)}}";
                return ($"shared-{targetName}", port, secretRef);
            },
            ImageConfig = image.Config,
            DerivedDbName = DeriveDbName(image, resolver, registry)
        };

        if (plugin.HasCustomEnvVarBuilder)
        {
            var context = new EnvVarContext(
                HostName: poolName,
                ImageName: imgName,
                SecretRef: secretName =>
                    $"${{nonsensitive(random_password.{poolName}_{imgName}_{secretName}.result)}}",
                ResolveWire: portName =>
                {
                    var target = resolver.ResolveWiredImage(image.Id, portName);
                    if (target == null) return null;
                    var targetName = SanitizeName(target.Name);
                    var targetDesc = registry.GetDescriptor(target);
                    var port = targetDesc?.Ports.FirstOrDefault()?.Port ?? 80;
                    var secretRef = $"${{nonsensitive(random_password.{poolName}_{targetName}_password.result)}}";
                    return new WireResolution($"shared-{targetName}", port, secretRef);
                },
                ServiceKeys: new Dictionary<string, string>(),
                ServiceKeyRef: _ => null,
                BaseDomain: null);

            foreach (var e in plugin.BuildEnvVars(context))
                parts.Add($"-e {e.Key}={e.Value}");
        }
        else
        {
            foreach (var template in templates)
            {
                var value = templateEngine.Resolve(template.ValueTemplate, templateContext);
                parts.Add($"-e {template.Key}={value}");
            }
        }

        parts.Add(dockerImage);

        // Command override
        var cmdOverride = plugin.GetCommandOverride();
        if (cmdOverride != null)
        {
            var resolvedCmd = templateEngine.Resolve(cmdOverride, templateContext);
            parts.Add(resolvedCmd);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Derive DB name from the consumer wired to this PG image.
    /// HubServer -> xcord_hub, FederationServer -> xcord, otherwise -> app
    /// </summary>
    public static string DeriveDbName(Image pgImage, WireResolver resolver) =>
        DeriveDbName(pgImage, resolver, DefaultPlugins.CreateRegistry());

    public static string DeriveDbName(Image pgImage, WireResolver resolver, ImagePluginRegistry registry)
    {
        var incoming = resolver.ResolveIncoming(pgImage.Id, "postgres");
        foreach (var (node, _) in incoming)
        {
            if (node is Image consumerImage)
            {
                var consumerPlugin = registry.GetForImage(consumerImage);
                var dbName = consumerPlugin?.GetDockerBehavior().DbNameWhenConsuming;
                if (!string.IsNullOrEmpty(dbName))
                    return dbName;
            }
        }
        return "app";
    }
}
