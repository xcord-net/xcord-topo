using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider
{
    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var topoName = TopologyHelpers.SanitizeName(topology.Name);
        var instances = new HclBuilder();

        instances.Block($"resource \"aws_key_pair\" \"{topoName}\"", b =>
        {
            b.Attribute("key_name", $"{topology.Name}-key");
            b.RawAttribute("public_key", "tls_private_key.deploy.public_key_openssh");
        });
        instances.Line();

        instances.Block($"data \"aws_ami\" \"ubuntu\"", b =>
        {
            b.RawAttribute("most_recent", "true");
            b.RawAttribute("owners", "[\"099720109477\"]");

            b.Block("filter", fb =>
            {
                fb.Attribute("name", "name");
                fb.RawAttribute("values", "[\"ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*\"]");
            });

            b.Block("filter", fb =>
            {
                fb.Attribute("name", "virtualization-type");
                fb.RawAttribute("values", "[\"hvm\"]");
            });
        });
        instances.Line();

        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host, _imageRegistry);
            var instanceType = SelectPlan(entry.Host.Name, ramRequired, infraSelections);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
            var isPersistent = TopologyHelpers.HasPersistentImage(entry.Host, _imageRegistry);

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"aws_key_pair.{topoName}.key_name");
                b.Line();

                b.Block("root_block_device", rb =>
                {
                    var plan = GetPlans().FirstOrDefault(p => p.Id == instanceType);
                    rb.Attribute("volume_size", plan?.DiskGb ?? 20);
                    rb.Attribute("volume_type", "gp3");
                    rb.RawAttribute("encrypted", "true");
                    // Belt-and-suspenders with the prevent_destroy lifecycle below:
                    // even if the VM is forced-replaced, the data volume survives.
                    if (isPersistent)
                        rb.RawAttribute("delete_on_termination", "false");
                });

                b.Block("metadata_options", mb =>
                {
                    mb.Attribute("http_endpoint", "enabled");
                    mb.Attribute("http_tokens", "required");
                });

                b.Line();
                b.MapBlock("tags", tb =>
                {
                    tb.RawAttribute("Name", isReplicated
                        ? $"\"{HclBuilder.EscapeHcl(topology.Name)}-{HclBuilder.EscapeHcl(entry.Host.Name)}-${{count.index}}\""
                        : HclBuilder.Quoted($"{topology.Name}-{entry.Host.Name}"));
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                    // Data-bearing hosts (PG, Redis, MinIO, Registry) cannot be destroyed
                    // by a `terraform apply` that would remove them. Intentional teardown
                    // requires removing this protection first.
                    if (isPersistent)
                        lb.RawAttribute("prevent_destroy", "true");
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

            instances.Block($"resource \"aws_instance\" \"{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", selectedPlan.Id);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"aws_key_pair.{topoName}.key_name");
                b.Line();
                b.Block("root_block_device", rb =>
                {
                    rb.Attribute("volume_size", selectedPlan.DiskGb);
                    rb.Attribute("volume_type", "gp3");
                    rb.RawAttribute("encrypted", "true");
                });
                b.Block("metadata_options", mb =>
                {
                    mb.Attribute("http_endpoint", "enabled");
                    mb.Attribute("http_tokens", "required");
                });
                b.Line();
                b.MapBlock("tags", tb =>
                {
                    tb.RawAttribute("Name", $"\"{HclBuilder.EscapeHcl(topology.Name)}-{HclBuilder.EscapeHcl(pool.TierProfile.Name)}-${{count.index}}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });

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
            var instanceType = SelectPlan(image.Name, ramRequired, infraSelections);
            var varName = $"{resourceName}_replicas";
            var isPersistent = desc?.MountPath != null;

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("count", $"var.{varName}");
                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"aws_key_pair.{topoName}.key_name");
                b.Line();
                b.Block("root_block_device", rb =>
                {
                    var plan = GetPlans().FirstOrDefault(p => p.Id == instanceType);
                    rb.Attribute("volume_size", plan?.DiskGb ?? 20);
                    rb.Attribute("volume_type", "gp3");
                    rb.RawAttribute("encrypted", "true");
                    if (isPersistent)
                        rb.RawAttribute("delete_on_termination", "false");
                });
                b.Block("metadata_options", mb =>
                {
                    mb.Attribute("http_endpoint", "enabled");
                    mb.Attribute("http_tokens", "required");
                });
                b.Line();
                b.MapBlock("tags", tb =>
                {
                    tb.RawAttribute("Name", $"\"{HclBuilder.EscapeHcl(topology.Name)}-{HclBuilder.EscapeHcl(image.Name)}-${{count.index}}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });

                b.Line();
                b.Block("lifecycle", lb =>
                {
                    lb.RawAttribute("ignore_changes", "all");
                    // Elastic data-bearing images (broken-out PG/Redis/MinIO) keep their
                    // root volumes and refuse destroy via terraform.
                    if (isPersistent)
                        lb.RawAttribute("prevent_destroy", "true");
                });
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var ramRequired = TopologyHelpers.CalculateStandaloneCaddyRam(caddy, _imageRegistry);
            var instanceType = SelectPlan(caddy.Name, ramRequired, infraSelections);

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"aws_key_pair.{topoName}.key_name");
                b.Line();
                b.Block("root_block_device", rb =>
                {
                    var plan = GetPlans().FirstOrDefault(p => p.Id == instanceType);
                    rb.Attribute("volume_size", plan?.DiskGb ?? 20);
                    rb.Attribute("volume_type", "gp3");
                    rb.RawAttribute("encrypted", "true");
                });
                b.Block("metadata_options", mb =>
                {
                    mb.Attribute("http_endpoint", "enabled");
                    mb.Attribute("http_tokens", "required");
                });
                b.Line();
                b.MapBlock("tags", tb =>
                {
                    tb.Attribute("Name", $"{topology.Name}-{caddy.Name}");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });

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
