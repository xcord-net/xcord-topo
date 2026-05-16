using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider : ProviderHclBase
{
    private readonly ImagePluginRegistry _imageRegistry;
    private readonly TemplateEngine _templateEngine = new();

    public LinodeProvider(ImagePluginRegistry imageRegistry)
    {
        _imageRegistry = imageRegistry;
    }

    public LinodeProvider() : this(DefaultPlugins.CreateRegistry()) { }

    protected override string InstanceResourceType => "linode_instance";
    protected override string PublicIpField => "ip_address";
    protected override string PrivateIpField => "private_ip_address";

    public override string Key => "linode";

    public override ProviderInfo GetInfo() => new()
    {
        Key = "linode",
        Name = "Linode (Akamai)",
        Description = "Akamai Connected Cloud (formerly Linode). Affordable VPS hosting with global regions.",
        SupportedContainerKinds = ["Host", "Caddy", "ComputePool", "Dns"]
    };

    public override List<Region> GetRegions() =>
    [
        new() { Id = "us-east", Label = "Newark, NJ", Country = "US" },
        new() { Id = "us-central", Label = "Dallas, TX", Country = "US" },
        new() { Id = "us-west", Label = "Fremont, CA", Country = "US" },
        new() { Id = "us-lax", Label = "Los Angeles, CA", Country = "US" },
        new() { Id = "us-southeast", Label = "Atlanta, GA", Country = "US" },
        new() { Id = "eu-west", Label = "London, UK", Country = "GB" },
        new() { Id = "eu-central", Label = "Frankfurt, DE", Country = "DE" },
        new() { Id = "ap-south", Label = "Singapore", Country = "SG" },
        new() { Id = "ap-northeast", Label = "Tokyo, JP", Country = "JP" },
        new() { Id = "ap-southeast", Label = "Sydney, AU", Country = "AU" },
    ];

    public override List<ComputePlan> GetPlans() =>
    [
        new() { Id = "g6-nanode-1", Label = "Nanode 1GB", VCpus = 1, MemoryMb = 1024, DiskGb = 25, PriceMonthly = 5m },
        new() { Id = "g6-standard-1", Label = "Linode 2GB", VCpus = 1, MemoryMb = 2048, DiskGb = 50, PriceMonthly = 12m },
        new() { Id = "g6-standard-2", Label = "Linode 4GB", VCpus = 2, MemoryMb = 4096, DiskGb = 80, PriceMonthly = 24m },
        new() { Id = "g6-standard-4", Label = "Linode 8GB", VCpus = 4, MemoryMb = 8192, DiskGb = 160, PriceMonthly = 48m },
        new() { Id = "g6-standard-6", Label = "Linode 16GB", VCpus = 6, MemoryMb = 16384, DiskGb = 320, PriceMonthly = 96m },
        new() { Id = "g6-standard-8", Label = "Linode 32GB", VCpus = 8, MemoryMb = 32768, DiskGb = 640, PriceMonthly = 192m },
    ];

    public override List<CredentialField> GetCredentialSchema() =>
    [
        new()
        {
            Key = "linode_token",
            Label = "API Token",
            Type = "password",
            Sensitive = true,
            Required = true,
            Placeholder = "Enter Linode API token",
            Help = new()
            {
                Summary = "Personal Access Token from your Akamai/Linode account",
                Steps =
                [
                    "Log in to cloud.linode.com",
                    "Click your profile icon → API Tokens",
                    "Click \"Create a Personal Access Token\"",
                    "Set expiry and select scopes (see permissions below)",
                    "Copy the token - it's only shown once"
                ],
                Permissions = "Linodes: Read/Write, Domains: Read/Write, Firewalls: Read/Write, Volumes: Read/Write",
                Url = "https://cloud.linode.com/profile/tokens"
            },
            Validation = [new() { Type = "minLength", Value = "10", Message = "API token must be at least 10 characters" }]
        },
        new()
        {
            Key = "linode_region",
            Label = "Region",
            Type = "select",
            Sensitive = false,
            Required = true,
            Placeholder = "Select region...",
            Help = new()
            {
                Summary = "Linode data center region for your instances",
                Steps =
                [
                    "Pick the region closest to your users for lowest latency",
                    "All instances in this topology will share the same region",
                    "Consider data residency or compliance requirements"
                ],
                Url = "https://www.linode.com/global-infrastructure/"
            }
        },
        new()
        {
            Key = "domain",
            Label = "Domain",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "example.com",
            Help = new()
            {
                Summary = "Primary domain for your deployment",
                Steps =
                [
                    "Register a domain with any registrar",
                    "Point the domain's nameservers to Linode (ns1-ns5.linode.com)",
                    "Add the domain to Linode's DNS Manager",
                    "Terraform will create the necessary DNS records"
                ],
                Url = "https://techdocs.akamai.com/cloud-computing/docs/dns-manager"
            },
            Validation = [new() { Type = "pattern", Value = @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$", Message = "Enter a valid domain name (e.g. example.com)" }]
        },
    ];

    public override Dictionary<string, string> GenerateHcl(
        Topology topology,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var dnsContainers = TopologyHelpers.CollectDnsContainers(topology.Containers);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology, _imageRegistry);

        files["main.tf"] = GenerateMain();
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, _imageRegistry, pools, standaloneCaddies);
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        files["firewall.tf"] = GenerateFirewall(topology, hosts, pools, standaloneCaddies, _imageRegistry);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes.tf"] = GenerateVolumes(hosts);
        files["outputs.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        if (dnsContainers.Count > 0)
            files["dns.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

        var coldStorage = GenerateColdStorage(topology);
        if (coldStorage != null)
            files["coldstorage.tf"] = coldStorage;

        return files;
    }

    public override Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null,
        List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(ownedContainers.ToList())
            .Where(h => TopologyHelpers.ResolveProviderKey(h.Host, topology)
                .Equals(Key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pools = TopologyHelpers.CollectComputePools(ownedContainers.ToList(), topology, poolSelections)
            .Where(p => TopologyHelpers.ResolveProviderKey(p.Pool, topology)
                .Equals(Key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var dnsContainers = ownedContainers.Where(c => c.Kind == ContainerKind.Dns).ToList();
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddies(ownedContainers.ToList())
            .Where(c => TopologyHelpers.ResolveProviderKey(c, topology)
                .Equals(Key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var resolver = new WireResolver(topology, _imageRegistry);

        files["main_linode.tf"] = GenerateMain();
        files["variables_linode.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, _imageRegistry, pools, standaloneCaddies);
        files["instances_linode.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        if (hosts.Count > 0 || pools.Count > 0 || standaloneCaddies.Count > 0)
            files["firewall_linode.tf"] = GenerateFirewall(topology, hosts, pools, standaloneCaddies, _imageRegistry);
        files["provisioning_linode.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes_linode.tf"] = GenerateVolumes(hosts);
        files["outputs_linode.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers);
        if (dnsContainers.Count > 0)
            files["dns_linode.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

        var coldStorage = GenerateColdStorage(topology);
        if (coldStorage != null)
            files["coldstorage_linode.tf"] = coldStorage;

        return files;
    }
}
