using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider
{
    // --- DNS record generation ---

    private string GenerateDnsRecords(
        List<Container> dnsContainers,
        WireResolver resolver,
        Topology topology)
    {
        var dns = new HclBuilder();

        foreach (var dnsContainer in dnsContainers)
        {
            var domain = dnsContainer.Config.GetValueOrDefault("domain", "");
            if (string.IsNullOrEmpty(domain)) continue;

            var sanitizedDomain = TopologyHelpers.SanitizeName(domain);

            dns.Block($"data \"aws_route53_zone\" \"{sanitizedDomain}\"", b =>
            {
                b.RawAttribute("name", "var.domain");
            });
            dns.Line();

            var wiredContainers = TopologyHelpers.CollectContainersWiredToDns(dnsContainer, resolver);
            var wildcardCreated = false;
            foreach (var container in wiredContainers)
            {
                var containerName = TopologyHelpers.SanitizeName(container.Name);
                var providerKey = TopologyHelpers.ResolveProviderKey(container, topology);
                var isReplicated = TopologyHelpers.IsReplicatedHost(new TopologyHelpers.HostEntry(container));
                var ipRef = GetIpReference(containerName, providerKey, isReplicated);

                dns.Block($"resource \"aws_route53_record\" \"{containerName}\"", b =>
                {
                    b.RawAttribute("zone_id", $"data.aws_route53_zone.{sanitizedDomain}.zone_id");
                    b.RawAttribute("name", $"\"{containerName}.${{var.domain}}\"");
                    b.Attribute("type", "A");
                    b.Attribute("ttl", 300);
                    b.RawAttribute("records", $"[{ipRef}]");
                });
                dns.Line();

                // Caddy handles subdomain routing - add wildcard + bare domain records
                if (container.Kind == ContainerKind.Caddy && !wildcardCreated)
                {
                    dns.Block($"resource \"aws_route53_record\" \"wildcard\"", b =>
                    {
                        b.RawAttribute("zone_id", $"data.aws_route53_zone.{sanitizedDomain}.zone_id");
                        b.RawAttribute("name", $"\"*.${{var.domain}}\"");
                        b.Attribute("type", "A");
                        b.Attribute("ttl", 300);
                        b.RawAttribute("records", $"[{ipRef}]");
                    });
                    dns.Line();

                    // Bare domain (apex) record when Caddy domain matches DNS zone
                    var caddyDomain = container.Config.GetValueOrDefault("domain", "");
                    if (!string.IsNullOrEmpty(caddyDomain) && caddyDomain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        dns.Block($"resource \"aws_route53_record\" \"apex\"", b =>
                        {
                            b.RawAttribute("zone_id", $"data.aws_route53_zone.{sanitizedDomain}.zone_id");
                            b.RawAttribute("name", "var.domain");
                            b.Attribute("type", "A");
                            b.Attribute("ttl", 300);
                            b.RawAttribute("records", $"[{ipRef}]");
                        });
                        dns.Line();
                    }

                    wildcardCreated = true;
                }
            }
        }

        return dns.ToString();
    }
}
