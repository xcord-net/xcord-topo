using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
    // --- Environment variable building ---

    public static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver, Topology? topology = null,
        Container? resolveFrom = null,
        Dictionary<Guid, Dictionary<int, int>>? portAssignments = null) =>
        BuildEnvVars(image, entry, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine(), topology, resolveFrom, portAssignments);

    public static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine,
        Topology? topology = null,
        Container? resolveFrom = null,
        Dictionary<Guid, Dictionary<int, int>>? portAssignments = null)
    {
        var plugin = registry.GetForImage(image);
        if (plugin == null) return [];

        var hostName = SanitizeName(entry.Host.Name);
        var imgName = SanitizeName(image.Name);
        var sourceHost = resolveFrom ?? entry.Host;

        if (plugin.HasCustomEnvVarBuilder)
        {
            var context = new EnvVarContext(
                HostName: hostName,
                ImageName: imgName,
                SecretRef: secretName =>
                    $"${{nonsensitive(random_password.{hostName}_{imgName}_{secretName}.result)}}",
                ResolveWire: portName =>
                {
                    var target = resolver.ResolveWiredImage(image.Id, portName);
                    if (target == null) return null;
                    var targetName = SanitizeName(target.Name);
                    var targetHost = resolver.FindHostFor(target.Id);
                    var targetHostName = targetHost != null ? SanitizeName(targetHost.Name) : hostName;
                    var host = topology != null
                        ? ResolveServiceHost(target, sourceHost, resolver, topology)
                        : targetName;
                    var desc = registry.GetDescriptor(target);
                    var port = desc?.Ports.FirstOrDefault()?.Port ?? 80;
                    var resolvedPort = ResolveServicePort(target, sourceHost, port, resolver, portAssignments);
                    var secretRef = $"${{nonsensitive(random_password.{targetHostName}_{targetName}_password.result)}}";
                    return new WireResolution(host, resolvedPort, secretRef);
                },
                ServiceKeys: topology?.ServiceKeys ?? new Dictionary<string, string>(),
                ServiceKeyRef: serviceKey =>
                {
                    if (topology == null) return null;
                    var prefix = serviceKey.Split('_')[0];
                    var schema = ServiceKeySchema.GetSchema(topology);
                    var field = schema.FirstOrDefault(f => f.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
                    var groupHasAnyKey = schema
                        .Where(f => f.Key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) || f.Key == prefix)
                        .Any(f => topology.ServiceKeys.ContainsKey(f.Key));
                    if (!groupHasAnyKey) return null;
                    return field?.Sensitive == true
                        ? $"${{nonsensitive(var.{serviceKey})}}"
                        : $"${{var.{serviceKey}}}";
                },
                BaseDomain: topology?.ServiceKeys.ContainsKey("hub_base_domain") == true
                    ? "${var.hub_base_domain}" : null);

            return plugin.BuildEnvVars(context)
                .Select(e => (e.Key, e.Value))
                .ToList();
        }

        // Declarative plugins: resolve templates
        var templates = plugin.GetEnvVarTemplates();
        if (templates.Count == 0) return [];

        var dbName = DeriveDbName(image, resolver, registry);
        var templateContext = new TemplateContext
        {
            HostName = hostName,
            ImageName = imgName,
            ResolveWire = portName =>
            {
                var target = resolver.ResolveWiredImage(image.Id, portName);
                if (target == null) return null;
                var targetName = SanitizeName(target.Name);
                var targetHost = resolver.FindHostFor(target.Id);
                var targetHostName = targetHost != null ? SanitizeName(targetHost.Name) : hostName;
                var host = topology != null
                    ? ResolveServiceHost(target, sourceHost, resolver, topology)
                    : targetName;
                var desc = registry.GetDescriptor(target);
                var port = desc?.Ports.FirstOrDefault()?.Port ?? 80;
                var resolvedPort = ResolveServicePort(target, sourceHost, port, resolver, portAssignments);
                var secretRef = $"${{nonsensitive(random_password.{targetHostName}_{targetName}_password.result)}}";
                return (host, resolvedPort, secretRef);
            },
            Registry = topology != null ? ResolveRegistry(topology) : null,
            ImageConfig = image.Config,
            DerivedDbName = dbName
        };

        return templates
            .Select(t => (t.Key, templateEngine.Resolve(t.ValueTemplate, templateContext)))
            .ToList();
    }


    private static void AddServiceKeyEnvVar(
        List<(string Key, string Value)> envVars,
        Topology topology,
        string serviceKey,
        string envVarName)
    {
        // Always reference the Terraform variable - even keys not in topology.ServiceKeys
        // (e.g., smtp_password stored in credential store) get declared as variables
        // via group-based emission in GenerateServiceKeyVariables.
        // If no keys from the group are defined at all, skip entirely.
        var prefix = serviceKey.Split('_')[0];
        var schema = ServiceKeySchema.GetSchema(topology);
        var field = schema.FirstOrDefault(f => f.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
        var groupHasAnyKey = schema
            .Where(f => f.Key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) || f.Key == prefix)
            .Any(f => topology.ServiceKeys.ContainsKey(f.Key));

        if (groupHasAnyKey)
        {
            // Wrap sensitive vars with nonsensitive() to prevent Terraform from
            // suppressing all provisioner output when these appear in inline commands.
            var varRef = field?.Sensitive == true
                ? $"${{nonsensitive(var.{serviceKey})}}"
                : $"${{var.{serviceKey}}}";
            envVars.Add((envVarName, varRef));
        }
    }

    public static string? ResolveCommandOverride(Image image, HostEntry entry, WireResolver resolver) =>
        ResolveCommandOverride(image, entry, resolver, DefaultPlugins.CreateRegistry(), new TemplateEngine());

    public static string? ResolveCommandOverride(
        Image image, HostEntry entry, WireResolver resolver,
        ImagePluginRegistry registry, TemplateEngine templateEngine)
    {
        var plugin = registry.GetForImage(image);
        var cmdTemplate = plugin?.GetCommandOverride();
        if (cmdTemplate == null) return null;

        var hostName = SanitizeName(entry.Host.Name);
        var imgName = SanitizeName(image.Name);
        var templateContext = new TemplateContext
        {
            HostName = hostName,
            ImageName = imgName,
            ImageConfig = image.Config
        };
        return templateEngine.Resolve(cmdTemplate, templateContext);
    }

}
