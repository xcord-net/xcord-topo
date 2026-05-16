using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
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

            dns.Block($"data \"linode_domain\" \"{sanitizedDomain}\"", b =>
            {
                b.RawAttribute("domain", "var.domain");
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

                dns.Block($"resource \"linode_domain_record\" \"{containerName}\"", b =>
                {
                    b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                    b.Attribute("name", containerName);
                    b.Attribute("record_type", "A");
                    b.RawAttribute("target", ipRef);
                    b.Attribute("ttl_sec", 300);
                });
                dns.Line();

                // Caddy containers handle subdomain routing - create wildcard + bare domain records
                if (container.Kind == ContainerKind.Caddy && !wildcardCreated)
                {
                    dns.Block($"resource \"linode_domain_record\" \"wildcard\"", b =>
                    {
                        b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                        b.Attribute("name", "*");
                        b.Attribute("record_type", "A");
                        b.RawAttribute("target", ipRef);
                        b.Attribute("ttl_sec", 300);
                    });
                    dns.Line();

                    // Bare domain (apex) record when Caddy domain matches DNS zone
                    var caddyDomain = container.Config.GetValueOrDefault("domain", "");
                    if (!string.IsNullOrEmpty(caddyDomain) && caddyDomain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        dns.Block($"resource \"linode_domain_record\" \"apex\"", b =>
                        {
                            b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                            b.Attribute("name", "");
                            b.Attribute("record_type", "A");
                            b.RawAttribute("target", ipRef);
                            b.Attribute("ttl_sec", 300);
                        });
                        dns.Line();
                    }

                    wildcardCreated = true;
                }

                // Registry images co-located with this container need a DNS record for their subdomain
                var registryImages = TopologyHelpers.CollectRegistryImagesRecursive(container);
                foreach (var reg in registryImages)
                {
                    var regSubdomain = TopologyHelpers.SanitizeName(reg.Name);
                    var regResourceName = $"{regSubdomain}_dns";

                    dns.Block($"resource \"linode_domain_record\" \"{regResourceName}\"", b =>
                    {
                        b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                        b.Attribute("name", regSubdomain);
                        b.Attribute("record_type", "A");
                        b.RawAttribute("target", ipRef);
                        b.Attribute("ttl_sec", 300);
                    });
                    dns.Line();
                }
            }
        }

        return dns.ToString();
    }

    // --- Cold storage generation ---

}
