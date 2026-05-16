using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider : ProviderHclBase
{
    private readonly ImagePluginRegistry _imageRegistry;
    private readonly TemplateEngine _templateEngine = new();

    public AwsProvider(ImagePluginRegistry imageRegistry)
    {
        _imageRegistry = imageRegistry;
    }

    public AwsProvider() : this(DefaultPlugins.CreateRegistry()) { }

    protected override string InstanceResourceType => "aws_instance";
    protected override string PublicIpField => "public_ip";
    protected override string PrivateIpField => "private_ip";

    public override string Key => "aws";

    public override ProviderInfo GetInfo() => new()
    {
        Key = "aws",
        Name = "Amazon Web Services",
        Description = "AWS EC2 instances with VPC networking. The most widely used cloud platform.",
        SupportedContainerKinds = ["Host", "Caddy", "ComputePool", "Dns"]
    };

    public override List<Region> GetRegions() =>
    [
        new() { Id = "us-east-1", Label = "US East (Virginia)", Country = "US" },
        new() { Id = "us-east-2", Label = "US East (Ohio)", Country = "US" },
        new() { Id = "us-west-1", Label = "US West (N. California)", Country = "US" },
        new() { Id = "us-west-2", Label = "US West (Oregon)", Country = "US" },
        new() { Id = "eu-west-1", Label = "Europe (Ireland)", Country = "IE" },
        new() { Id = "eu-central-1", Label = "Europe (Frankfurt)", Country = "DE" },
        new() { Id = "ap-southeast-1", Label = "Asia Pacific (Singapore)", Country = "SG" },
        new() { Id = "ap-northeast-1", Label = "Asia Pacific (Tokyo)", Country = "JP" },
        new() { Id = "ap-southeast-2", Label = "Asia Pacific (Sydney)", Country = "AU" },
    ];

    public override List<ComputePlan> GetPlans() =>
    [
        new() { Id = "t3.micro", Label = "T3 Micro (1GB)", VCpus = 2, MemoryMb = 1024, DiskGb = 8, PriceMonthly = 7.60m },
        new() { Id = "t3.small", Label = "T3 Small (2GB)", VCpus = 2, MemoryMb = 2048, DiskGb = 20, PriceMonthly = 15.20m },
        new() { Id = "t3.medium", Label = "T3 Medium (4GB)", VCpus = 2, MemoryMb = 4096, DiskGb = 40, PriceMonthly = 30.40m },
        new() { Id = "t3.large", Label = "T3 Large (8GB)", VCpus = 2, MemoryMb = 8192, DiskGb = 80, PriceMonthly = 60.70m },
        new() { Id = "t3.xlarge", Label = "T3 XLarge (16GB)", VCpus = 4, MemoryMb = 16384, DiskGb = 160, PriceMonthly = 121.50m },
        new() { Id = "m5.large", Label = "M5 Large (8GB)", VCpus = 2, MemoryMb = 8192, DiskGb = 80, PriceMonthly = 70.00m },
        new() { Id = "m5.xlarge", Label = "M5 XLarge (16GB)", VCpus = 4, MemoryMb = 16384, DiskGb = 160, PriceMonthly = 140.00m },
    ];

    public override List<CredentialField> GetCredentialSchema() =>
    [
        new()
        {
            Key = "aws_access_key_id",
            Label = "Access Key ID",
            Type = "text",
            Sensitive = true,
            Required = true,
            Placeholder = "AKIA...",
            Help = new()
            {
                Summary = "IAM access key for programmatic AWS access",
                Steps =
                [
                    "Log in to the AWS Management Console",
                    "Go to IAM → Users → select your user",
                    "Click \"Security credentials\" tab",
                    "Click \"Create access key\"",
                    "Select \"Third-party service\" use case",
                    "Copy both the Access Key ID and Secret Access Key"
                ],
                Permissions = "EC2: RunInstances, TerminateInstances, DescribeInstances, DescribeImages, DescribeInstanceTypes, CreateTags, CreateKeyPair, DeleteKeyPair, DescribeKeyPairs | VPC: CreateVpc, DeleteVpc, DescribeVpcs, ModifyVpcAttribute, CreateSubnet, DeleteSubnet, DescribeSubnets, CreateInternetGateway, DeleteInternetGateway, AttachInternetGateway, DetachInternetGateway, DescribeInternetGateways, CreateRouteTable, DeleteRouteTable, CreateRoute, DescribeRouteTables, AssociateRouteTable, DisassociateRouteTable | Security Groups: CreateSecurityGroup, DeleteSecurityGroup, DescribeSecurityGroups, AuthorizeSecurityGroupIngress, AuthorizeSecurityGroupEgress, RevokeSecurityGroupIngress, RevokeSecurityGroupEgress (or use the AmazonEC2FullAccess managed policy)",
                Note = "AWS will recommend using IAM roles instead - this is expected. IAM roles are for workloads running on AWS infrastructure; static access keys are the correct choice for external management tools.",
                Url = "https://console.aws.amazon.com/iam/home#/users"
            },
            Validation = [new() { Type = "pattern", Value = @"^AKIA[A-Z0-9]{16}$", Message = "Must be a valid AWS access key ID (starts with AKIA, 20 characters)" }]
        },
        new()
        {
            Key = "aws_secret_access_key",
            Label = "Secret Access Key",
            Type = "password",
            Sensitive = true,
            Required = true,
            Placeholder = "Enter AWS secret access key",
            Help = new()
            {
                Summary = "Secret key paired with your Access Key ID",
                Steps =
                [
                    "This is shown only once when creating the access key",
                    "If you lost it, create a new access key pair",
                    "Store it securely - it grants full API access"
                ],
                Permissions = "Same as Access Key ID - they are a pair",
                Url = "https://console.aws.amazon.com/iam/home#/users"
            },
            Validation = [new() { Type = "minLength", Value = "20", Message = "Secret access key must be at least 20 characters" }]
        },
        new()
        {
            Key = "aws_region",
            Label = "Region",
            Type = "select",
            Sensitive = false,
            Required = true,
            Placeholder = "Select region...",
            Help = new()
            {
                Summary = "AWS region for your EC2 instances and VPC",
                Steps =
                [
                    "Pick the region closest to your users for lowest latency",
                    "All instances in this topology will share the same region",
                    "Consider data residency or compliance requirements"
                ],
                Url = "https://aws.amazon.com/about-aws/global-infrastructure/regions_az/"
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
                    "Create a hosted zone in Route53 for your domain",
                    "Update your registrar's nameservers to the Route53 NS records",
                    "Terraform will create the necessary DNS records"
                ],
                Url = "https://console.aws.amazon.com/route53/v2/hostedzones"
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
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, _imageRegistry, pools, standaloneCaddies);
        files["network.tf"] = GenerateNetwork(topology);
        files["security_groups.tf"] = GenerateSecurityGroups(topology, hosts, _imageRegistry);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
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

        files["main_aws.tf"] = GenerateMain();
        files["variables_aws.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, _imageRegistry, pools, standaloneCaddies);
        files["network_aws.tf"] = GenerateNetwork(topology);
        files["security_groups_aws.tf"] = GenerateSecurityGroups(topology, hosts, _imageRegistry);
        files["instances_aws.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        files["provisioning_aws.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["outputs_aws.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers);
        if (dnsContainers.Count > 0)
            files["dns_aws.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

        var coldStorage = GenerateColdStorage(topology);
        if (coldStorage != null)
            files["coldstorage_aws.tf"] = coldStorage;

        return files;
    }
}
