using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider
{
    private static string GenerateMain()
    {
        var main = new HclBuilder();
        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                p.MapBlock("aws", ap =>
                {
                    ap.Attribute("source", "hashicorp/aws");
                    ap.Attribute("version", "~> 5.0");
                });
                p.MapBlock("random", rp =>
                {
                    rp.Attribute("source", "hashicorp/random");
                    rp.Attribute("version", "~> 3.0");
                });
                p.MapBlock("tls", tp =>
                {
                    tp.Attribute("source", "hashicorp/tls");
                    tp.Attribute("version", "~> 4.0");
                });
            });
        });
        main.Line();
        main.Block("provider \"aws\"", b =>
        {
            b.RawAttribute("access_key", "var.aws_access_key_id");
            b.RawAttribute("secret_key", "var.aws_secret_access_key");
            b.RawAttribute("region", "var.aws_region");
        });
        main.Line();
        main.Block("resource \"tls_private_key\" \"deploy\"", b =>
        {
            b.Attribute("algorithm", "ED25519");
        });
        return main.ToString();
    }

    private string GenerateVariables(Topology topology, List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var vars = new HclBuilder();
        vars.Block("variable \"aws_access_key_id\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "AWS access key ID");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"aws_secret_access_key\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "AWS secret access key");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"aws_region\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("aws_region", "us-east-1"));
            b.Attribute("description", "AWS region");
        });
        vars.Line();
        vars.Block("variable \"domain\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Primary domain name");
        });
        vars.Line();
        vars.Block("variable \"ssh_cidr_blocks\"", b =>
        {
            b.RawAttribute("type", "list(string)");
            b.RawAttribute("default", "[]");
            b.Attribute("description", "CIDR blocks allowed for SSH access (empty = allow all, required for provisioners)");
        });
        vars.Line();
        vars.Block("variable \"registry_url\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", TopologyHelpers.ResolveRegistry(topology));
            b.Attribute("description", "Docker registry URL for pulling xcord images");
        });
        vars.Line();
        // Dynamically generate version variables for all private-registry images in the topology
        foreach (var versionVar in TopologyHelpers.CollectVersionVariables(topology, _imageRegistry))
        {
            vars.Block($"variable \"{versionVar}\"", b =>
            {
                b.RawAttribute("type", "string");
                b.Attribute("default", "0.1");
                b.Attribute("description", $"Version tag for {versionVar.Replace("_version", "")} image");
            });
            vars.Line();
        }
        vars.Block("variable \"deploy_apps\"", b =>
        {
            b.RawAttribute("type", "bool");
            b.RawAttribute("default", "false");
            b.Attribute("description", "Set to true after images are pushed to deploy application containers");
        });
        vars.Line();
        vars.Block("variable \"registry_username\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "Docker registry username (leave empty for no auth)");
        });
        vars.Line();
        vars.Block("variable \"registry_password\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "Docker registry password or token");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"ssh_cidr_blocks\"", b =>
        {
            b.RawAttribute("type", "list(string)");
            b.RawAttribute("default", "[]");
            b.Attribute("description", "CIDR blocks allowed for SSH access (empty = allow all, required for provisioners)");
        });

        // Host replica variables
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        CollectHostReplicaVariables(hosts, vars);

        // Elastic image replica variables
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        CollectElasticImageVariables(hosts, standaloneCaddies, vars);

        // ComputePool + service key variables (shared)
        GeneratePoolAndServiceKeyVariables(vars, topology, pools);

        return vars.ToString();
    }

    private static string GenerateNetwork(Topology topology)
    {
        var name = TopologyHelpers.SanitizeName(topology.Name);
        var net = new HclBuilder();

        net.Block($"resource \"aws_vpc\" \"{name}\"", b =>
        {
            b.Attribute("cidr_block", "10.0.0.0/16");
            b.RawAttribute("enable_dns_support", "true");
            b.RawAttribute("enable_dns_hostnames", "true");
            b.Line();
            b.MapBlock("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-vpc");
                tb.Attribute("Project", "xcord-topo");
            });
        });
        net.Line();

        net.Block($"resource \"aws_subnet\" \"{name}\"", b =>
        {
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Attribute("cidr_block", "10.0.1.0/24");
            b.RawAttribute("map_public_ip_on_launch", "true");
            b.Line();
            b.MapBlock("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-subnet");
            });
        });
        net.Line();

        net.Block($"resource \"aws_internet_gateway\" \"{name}\"", b =>
        {
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();
            b.MapBlock("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-igw");
            });
        });
        net.Line();

        net.Block($"resource \"aws_route_table\" \"{name}\"", b =>
        {
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();
            b.Block("route", rb =>
            {
                rb.Attribute("cidr_block", "0.0.0.0/0");
                rb.RawAttribute("gateway_id", $"aws_internet_gateway.{name}.id");
            });
            b.Line();
            b.MapBlock("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-rt");
            });
        });
        net.Line();

        net.Block($"resource \"aws_route_table_association\" \"{name}\"", b =>
        {
            b.RawAttribute("subnet_id", $"aws_subnet.{name}.id");
            b.RawAttribute("route_table_id", $"aws_route_table.{name}.id");
        });

        return net.ToString();
    }
}
