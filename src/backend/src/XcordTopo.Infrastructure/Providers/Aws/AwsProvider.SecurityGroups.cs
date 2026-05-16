using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider
{
    private static string GenerateSecurityGroups(Topology topology, List<TopologyHelpers.HostEntry> hosts, ImagePluginRegistry imageRegistry)
    {
        var name = TopologyHelpers.SanitizeName(topology.Name);
        var sg = new HclBuilder();

        // Check entire topology for LiveKit - it may be on hosts, standalone Caddies, or elastic
        var hasLiveKit = topology.Containers.Any(c => HasLiveKitRecursive(c));

        bool HasLiveKitRecursive(Container c)
        {
            if (c.Images.Any(i => imageRegistry.GetDescriptor(i)?.IsPublicEndpoint == true && imageRegistry.GetPorts(i).Any(p => p == 7880))) return true;
            return c.Children.Any(HasLiveKitRecursive);
        }

        sg.Block($"resource \"aws_security_group\" \"{name}\"", b =>
        {
            b.Attribute("name", $"{topology.Name}-sg");
            b.Attribute("description", "Security group for xcord-topo deployment");
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();

            // SSH is required for Terraform provisioners to connect.
            // ssh_cidr_blocks narrows access; default allows all since topo's IP may not be known.
            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "SSH");
                ib.Attribute("from_port", 22);
                ib.Attribute("to_port", 22);
                ib.Attribute("protocol", "tcp");
                ib.RawAttribute("cidr_blocks", "length(var.ssh_cidr_blocks) > 0 ? var.ssh_cidr_blocks : [\"0.0.0.0/0\"]");
            });

            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "HTTP");
                ib.Attribute("from_port", 80);
                ib.Attribute("to_port", 80);
                ib.Attribute("protocol", "tcp");
                ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
            });

            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "HTTPS");
                ib.Attribute("from_port", 443);
                ib.Attribute("to_port", 443);
                ib.Attribute("protocol", "tcp");
                ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
            });

            if (hasLiveKit)
            {
                b.Block("ingress", ib =>
                {
                    ib.Attribute("description", "LiveKit TCP");
                    ib.Attribute("from_port", 7880);
                    ib.Attribute("to_port", 7882);
                    ib.Attribute("protocol", "tcp");
                    ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
                });

                b.Block("ingress", ib =>
                {
                    ib.Attribute("description", "LiveKit UDP");
                    ib.Attribute("from_port", 7880);
                    ib.Attribute("to_port", 7882);
                    ib.Attribute("protocol", "udp");
                    ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
                });

                b.Block("ingress", ib =>
                {
                    ib.Attribute("description", "LiveKit WebRTC media");
                    ib.Attribute("from_port", 50000);
                    ib.Attribute("to_port", 60000);
                    ib.Attribute("protocol", "udp");
                    ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
                });
            }

            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "Internal VPC traffic");
                ib.Attribute("from_port", 0);
                ib.Attribute("to_port", 0);
                ib.Attribute("protocol", "-1");
                ib.RawAttribute("self", "true");
            });

            b.Block("egress", eb =>
            {
                eb.Attribute("from_port", 0);
                eb.Attribute("to_port", 0);
                eb.Attribute("protocol", "-1");
                eb.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
            });

            b.Line();
            b.MapBlock("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-sg");
                tb.Attribute("Project", "xcord-topo");
            });
        });

        return sg.ToString();
    }
}
