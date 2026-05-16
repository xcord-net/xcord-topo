using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private string GenerateVariables(Topology topology, List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var vars = new HclBuilder();
        vars.Block("variable \"linode_token\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Linode API token");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"linode_region\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("linode_region", "us-east"));
            b.Attribute("description", "Linode region");
        });
        vars.Line();
        vars.Block("variable \"domain\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Primary domain name");
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

}
