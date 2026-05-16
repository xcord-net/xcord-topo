using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Models;
using XcordTopo.PluginSdk;

namespace XcordTopo.Infrastructure.Providers;

public static partial class TopologyHelpers
{
    // --- Rate limiting ---

    /// <summary>
    /// Generates iptables hashlimit commands for rate limiting HTTP/HTTPS traffic.
    /// Always-on for any host running Caddy, with configurable threshold via caddy.Config["rateLimit"].
    /// Default: 1000/min per source IP with 2x burst.
    /// </summary>
    public static List<string> GenerateRateLimitCommands(Container caddy)
    {
        var rateConfig = caddy.Config.GetValueOrDefault("rateLimit", "1000/min");
        var caddyName = SanitizeName(caddy.Name);

        // Parse rate (e.g., "100/min") and compute burst as 2x the rate number
        var burst = 200;
        var slashIdx = rateConfig.IndexOf('/');
        if (slashIdx > 0 && int.TryParse(rateConfig[..slashIdx], out var rateNum))
            burst = rateNum * 2;

        return
        [
            "modprobe xt_hashlimit 2>/dev/null || true",
            $"iptables -A INPUT -p tcp --dport 80 -m hashlimit --hashlimit-above {rateConfig} --hashlimit-burst {burst} --hashlimit-mode srcip --hashlimit-name {caddyName}_http -j DROP || true",
            $"iptables -A INPUT -p tcp --dport 443 -m hashlimit --hashlimit-above {rateConfig} --hashlimit-burst {burst} --hashlimit-mode srcip --hashlimit-name {caddyName}_https -j DROP || true",
            "mkdir -p /etc/iptables",
            "sh -c 'iptables-save > /etc/iptables/rules.v4'"
        ];
    }

    // --- Caddyfile ---

    public static string GenerateCaddyfile(Container caddy, WireResolver resolver, Topology? topology = null) =>
        GenerateCaddyfile(caddy, resolver, DefaultPlugins.CreateRegistry(), topology);

    public static string GenerateCaddyfile(Container caddy, WireResolver resolver, ImagePluginRegistry registry, Topology? topology = null)
    {
        var upstreams = resolver.ResolveCaddyUpstreams(caddy);
        var domain = "${var.domain}";

        var securityHeaders = new[]
        {
            "  header {",
            "    Strict-Transport-Security \"max-age=31536000; includeSubDomains; preload\"",
            "    X-Content-Type-Options \"nosniff\"",
            "    X-Frame-Options \"SAMEORIGIN\"",
            "    Referrer-Policy \"strict-origin-when-cross-origin\"",
            "    Permissions-Policy \"camera=(self), microphone=(self), geolocation=(), payment=()\"",
            "  }"
        };

        var grouped = new Dictionary<string, List<string>>();
        foreach (var (image, subdomain) in upstreams)
        {
            var ports = registry.GetPorts(image);
            var port = ports.Length > 0 ? ports[0] : 80;
            var host = $"{subdomain}.{domain}";

            var upstreamHost = topology != null
                ? ResolveCaddyUpstreamHost(image, caddy, resolver, topology)
                : SanitizeName(image.Name);

            var backend = $"{upstreamHost}:{port}";
            if (!grouped.TryGetValue(host, out var backends))
            {
                backends = [];
                grouped[host] = backends;
            }
            if (!backends.Contains(backend))
                backends.Add(backend);
        }

        // ComputePool wildcard routes (*.domain → pool) are NOT statically configured.
        // When compute_pool_host_count=0 there are no pool instances, so a static reference
        // like compute_pool[0].private_ip would be an invalid Terraform reference.
        // Hub configures wildcard tenant routing via Caddy admin API at runtime.

        var blocks = new List<string>();
        foreach (var (host, backends) in grouped)
        {
            var block = new List<string> { $"{host} {{" };
            block.AddRange(securityHeaders);
            block.Add($"  reverse_proxy {string.Join(" ", backends)}");
            block.Add("}");
            blocks.Add(string.Join("\n", block));
        }

        // Apex domain route - find fixed "www" subdomain for hub
        var hubUpstream = upstreams.FirstOrDefault(u => u.Subdomain == "www");
        if (hubUpstream != default)
        {
            var hubHost = $"{hubUpstream.Subdomain}.{domain}";
            if (grouped.TryGetValue(hubHost, out var hubBackends))
            {
                var block = new List<string> { $"{domain} {{" };
                block.AddRange(securityHeaders);
                block.Add($"  reverse_proxy {string.Join(" ", hubBackends)}");
                block.Add("}");
                blocks.Add(string.Join("\n", block));
            }
        }

        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Recursively collects all Registry images from a container and its children.
    /// Used by DNS generators to create A records for registry domains.
    /// </summary>
    public static List<Image> CollectRegistryImagesRecursive(Container container)
    {
        var result = new List<Image>();
        result.AddRange(container.Images.Where(i => i.Kind == ImageKind.Registry));
        foreach (var child in container.Children)
            result.AddRange(CollectRegistryImagesRecursive(child));
        return result;
    }

    // --- Public endpoints ---

    /// <summary>
    /// Collects public endpoints from the topology by walking all images with IsPublicEndpoint metadata.
    /// All public images derive their subdomain from their name (same pattern as Caddy routing).
    /// Uses the topology's display domain, not Terraform variable interpolation.
    /// </summary>
    public static List<(string Url, string Kind, string? Backend)> CollectPublicEndpoints(Topology topology) =>
        CollectPublicEndpoints(topology, DefaultPlugins.CreateRegistry());

    public static List<(string Url, string Kind, string? Backend)> CollectPublicEndpoints(Topology topology, ImagePluginRegistry registry)
    {
        var endpoints = new List<(string, string, string?)>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenImageIds = new HashSet<Guid>();
        var resolver = new WireResolver(topology, registry);
        var domain = ResolveDomain(topology);

        void WalkForCaddies(List<Container> containers)
        {
            foreach (var container in containers)
            {
                if (container.Kind == ContainerKind.Caddy)
                {
                    var upstreams = resolver.ResolveCaddyUpstreams(container);
                    var grouped = new Dictionary<string, List<string>>();
                    foreach (var (image, subdomain) in upstreams)
                    {
                        seenImageIds.Add(image.Id);
                        var ports = registry.GetPorts(image);
                        var port = ports.Length > 0 ? ports[0] : 80;
                        var host = $"{subdomain}.{domain}";
                        var backend = $"{SanitizeName(image.Name)}:{port}";
                        if (!grouped.TryGetValue(host, out var backends))
                        {
                            backends = [];
                            grouped[host] = backends;
                        }
                        if (!backends.Contains(backend))
                            backends.Add(backend);
                    }

                    foreach (var (host, backends) in grouped)
                    {
                        var url = $"https://{host}";
                        if (seenUrls.Add(url))
                            endpoints.Add((url, "reverse_proxy", string.Join(" ", backends)));
                    }

                    var hubUpstream = upstreams.FirstOrDefault(u => u.Subdomain == "www");
                    if (hubUpstream != default)
                    {
                        var hubHost = $"{hubUpstream.Subdomain}.{domain}";
                        if (grouped.TryGetValue(hubHost, out var hubBackends))
                        {
                            var apexUrl = $"https://{domain}";
                            if (seenUrls.Add(apexUrl))
                                endpoints.Add((apexUrl, "apex", string.Join(" ", hubBackends)));
                        }
                    }
                }

                WalkForCaddies(container.Children);
            }
        }

        WalkForCaddies(topology.Containers);

        void WalkForPublicImages(List<Container> containers)
        {
            foreach (var container in containers)
            {
                foreach (var image in container.Images)
                {
                    if (seenImageIds.Contains(image.Id)) continue;
                    var desc = registry.GetDescriptor(image);
                    if (desc is not { IsPublicEndpoint: true }) continue;

                    var subdomain = image.Name.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
                    if (string.IsNullOrEmpty(subdomain)) continue;

                    var port = desc.Ports.FirstOrDefault()?.Port ?? 80;
                    var url = $"https://{subdomain}.{domain}";
                    if (seenUrls.Add(url))
                        endpoints.Add((url, image.ResolveTypeId().ToLowerInvariant(), $"{SanitizeName(image.Name)}:{port}"));
                }

                WalkForPublicImages(container.Children);
            }
        }

        WalkForPublicImages(topology.Containers);

        return endpoints;
    }

    /// <summary>
    /// Resolves the display domain from the topology's DNS container config.
    /// Falls back to "example.com" if no DNS container exists.
    /// </summary>
    public static string ResolveDomain(Topology topology)
    {
        static string? FindDomain(List<Container> containers)
        {
            foreach (var c in containers)
            {
                if (c.Kind == ContainerKind.Dns && c.Config.TryGetValue("domain", out var domain))
                    return domain;
                var child = FindDomain(c.Children);
                if (child != null) return child;
            }
            return null;
        }
        return FindDomain(topology.Containers) ?? "example.com";
    }

}
