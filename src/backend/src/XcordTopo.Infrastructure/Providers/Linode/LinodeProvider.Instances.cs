using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var instances = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host, _imageRegistry);
            var plan = SelectPlan(entry.Host.Name, ramRequired, infraSelections);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.Attribute("label", TopologyHelpers.IsReplicatedHost(entry)
                    ? $"{topology.Name}-{entry.Host.Name}-${{count.index}}"
                    : $"{topology.Name}-{entry.Host.Name}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, entry.Host.Kind.ToString().ToLowerInvariant()]);

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                });
            });
            instances.Line();
        }

        // ComputePool instances - one resource block per tier
        var allPlans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            var selectedPlan = ResolvePoolPlan(pool, allPlans);

            instances.Block($"resource \"linode_instance\" \"{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.Attribute("label", $"{topology.Name}-{pool.TierProfile.Name}-${{count.index}}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", selectedPlan.Id);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, pool.TierProfile.Id]);

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                });
            });
            instances.Line();
        }

        // Elastic image instances (replicas > 1, break out from hosts/caddies)
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var desc = _imageRegistry.GetDescriptor(image);
            var ramRequired = desc?.MinRamMb ?? 256;
            var plan = SelectPlan(image.Name, ramRequired, infraSelections);
            var varName = $"{resourceName}_replicas";

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("count", $"var.{varName}");
                b.Attribute("label", $"{topology.Name}-{image.Name}-${{count.index}}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "elastic"]);

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                });
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var plan = SelectPlan(caddy.Name, TopologyHelpers.CalculateStandaloneCaddyRam(caddy, _imageRegistry), infraSelections);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.Attribute("label", $"{topology.Name}-{caddy.Name}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "caddy"]);

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                });
            });
            instances.Line();
        }

        return instances.ToString();
    }

}
