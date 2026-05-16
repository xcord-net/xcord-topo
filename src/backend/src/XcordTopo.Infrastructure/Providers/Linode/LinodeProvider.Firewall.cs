using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;
using XcordTopo.Infrastructure.Plugins;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private static string GenerateFirewall(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, ImagePluginRegistry imageRegistry)
    {
        var firewall = new HclBuilder();

        // Check entire topology for LiveKit - it may be on hosts, standalone Caddies, or pools
        var hasLiveKit = topology.Containers.Any(HasLiveKitRecursive);

        bool HasLiveKitRecursive(Container c)
        {
            if (c.Images.Any(i => imageRegistry.GetDescriptor(i)?.IsPublicEndpoint == true && imageRegistry.GetPorts(i).Any(p => p == 7880))) return true;
            return c.Children.Any(HasLiveKitRecursive);
        }

        firewall.Block("resource \"linode_firewall\" \"main\"", b =>
        {
            b.Attribute("label", $"{topology.Name}-firewall");

            b.Block("inbound", ib =>
            {
                ib.Attribute("label", "allow-ssh");
                ib.Attribute("action", "ACCEPT");
                ib.Attribute("protocol", "TCP");
                ib.Attribute("ports", "22");
                ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
            });

            b.Block("inbound", ib =>
            {
                ib.Attribute("label", "allow-http");
                ib.Attribute("action", "ACCEPT");
                ib.Attribute("protocol", "TCP");
                ib.Attribute("ports", "80,443");
                ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
            });

            if (hasLiveKit)
            {
                b.Block("inbound", ib =>
                {
                    ib.Attribute("label", "allow-livekit-tcp");
                    ib.Attribute("action", "ACCEPT");
                    ib.Attribute("protocol", "TCP");
                    ib.Attribute("ports", "7880-7882");
                    ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
                });

                b.Block("inbound", ib =>
                {
                    ib.Attribute("label", "allow-livekit-udp");
                    ib.Attribute("action", "ACCEPT");
                    ib.Attribute("protocol", "UDP");
                    ib.Attribute("ports", "7880-7882");
                    ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
                });
            }

            b.Block("inbound_policy", ib =>
            {
                ib.Attribute("action", "DROP");
            });

            b.Block("outbound_policy", ob =>
            {
                ob.Attribute("action", "ACCEPT");
            });

            var refs = new List<string>();
            foreach (var entry in hosts)
            {
                var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
                if (TopologyHelpers.IsReplicatedHost(entry))
                    refs.Add($"linode_instance.{resourceName}[*].id");
                else
                    refs.Add($"[linode_instance.{resourceName}.id]");
            }

            foreach (var caddy in standaloneCaddies)
            {
                var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
                refs.Add($"[linode_instance.{resourceName}.id]");
            }

            foreach (var pool in pools)
            {
                refs.Add($"linode_instance.{pool.ResourceName}[*].id");
            }

            // Elastic images get their own instances - include them in the firewall
            var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
            foreach (var image in elasticImages)
            {
                var resourceName = TopologyHelpers.SanitizeName(image.Name);
                refs.Add($"linode_instance.{resourceName}[*].id");
            }

            if (refs.Any(r => r.Contains("[*]")))
            {
                var concatArgs = string.Join(", ", refs);
                b.RawAttribute("linodes", $"concat({concatArgs})");
            }
            else
            {
                var idRefs = hosts.Select(e => $"linode_instance.{TopologyHelpers.SanitizeName(e.Host.Name)}.id")
                    .Concat(standaloneCaddies.Select(c => $"linode_instance.{TopologyHelpers.SanitizeName(c.Name)}.id"))
                    .Concat(pools.Select(p => $"linode_instance.{p.ResourceName}.id"))
                    .Concat(elasticImages.Select(i => $"linode_instance.{TopologyHelpers.SanitizeName(i.Name)}[*].id"));
                b.RawAttribute("linodes", $"[{string.Join(", ", idRefs)}]");
            }
        });
        return firewall.ToString();
    }

}
