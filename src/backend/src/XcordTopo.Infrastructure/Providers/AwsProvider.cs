using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class AwsProvider : ProviderHclBase
{
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
                Permissions = "EC2 full access, VPC full access, Route53 full access (or AmazonEC2FullAccess + AmazonRoute53FullAccess managed policies)",
                Note = "AWS will recommend using IAM roles instead — this is expected. IAM roles are for workloads running on AWS infrastructure; static access keys are the correct choice for external management tools.",
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
                    "Store it securely — it grants full API access"
                ],
                Permissions = "Same as Access Key ID — they are a pair",
                Url = "https://console.aws.amazon.com/iam/home#/users"
            },
            Validation = [new() { Type = "minLength", Value = "20", Message = "Secret access key must be at least 20 characters" }]
        },
        new()
        {
            Key = "region",
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
        new()
        {
            Key = "ssh_public_key",
            Label = "SSH Public Key",
            Type = "textarea",
            Sensitive = false,
            Required = false,
            Placeholder = "ssh-rsa AAAA...",
            Help = new()
            {
                Summary = "SSH public key for EC2 instance access",
                Steps =
                [
                    "Generate a key pair: ssh-keygen -t ed25519",
                    "Copy the public key: cat ~/.ssh/id_ed25519.pub",
                    "Paste the full public key here",
                    "The key will be imported as an AWS key pair"
                ],
                Url = "https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/ec2-key-pairs.html"
            },
            Validation = [new() { Type = "pattern", Value = @"^ssh-(rsa|ed25519|ecdsa)\s+[A-Za-z0-9+/=]+", Message = "Must be a valid SSH public key (ssh-rsa, ssh-ed25519, or ssh-ecdsa)" }]
        }
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
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, pools, standaloneCaddies);
        files["network.tf"] = GenerateNetwork(topology);
        files["security_groups.tf"] = GenerateSecurityGroups(topology, hosts);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["outputs.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        if (dnsContainers.Count > 0)
            files["dns.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

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
        var resolver = new WireResolver(topology);

        files["main_aws.tf"] = GenerateMain();
        files["variables_aws.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, pools, standaloneCaddies);
        files["network_aws.tf"] = GenerateNetwork(topology);
        files["security_groups_aws.tf"] = GenerateSecurityGroups(topology, hosts);
        files["instances_aws.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies, infraSelections);
        files["provisioning_aws.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["outputs_aws.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers);
        if (dnsContainers.Count > 0)
            files["dns_aws.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

        return files;
    }

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

                // Caddy handles subdomain routing — add wildcard + bare domain records
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

    // --- File generators ---

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
            });
        });
        main.Line();
        main.Block("provider \"aws\"", b =>
        {
            b.RawAttribute("access_key", "var.aws_access_key_id");
            b.RawAttribute("secret_key", "var.aws_secret_access_key");
            b.RawAttribute("region", "var.region");
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
        vars.Block("variable \"region\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("region", "us-east-1"));
            b.Attribute("description", "AWS region");
        });
        vars.Line();
        vars.Block("variable \"domain\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Primary domain name");
        });
        vars.Line();
        vars.Block("variable \"ssh_public_key\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "SSH public key for instance access");
        });
        vars.Line();
        vars.Block("variable \"ssh_private_key\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "SSH private key for provisioner authentication");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"registry_url\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", TopologyHelpers.ResolveRegistry(topology));
            b.Attribute("description", "Docker registry URL for pulling xcord images");
        });
        vars.Line();
        vars.Block("variable \"app_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Version tag for xcord application images (hub, fed)");
        });
        vars.Line();
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
            b.Attribute("description", "CIDR blocks allowed for SSH access (empty = no SSH)");
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

    private static string GenerateSecurityGroups(Topology topology, List<TopologyHelpers.HostEntry> hosts)
    {
        var name = TopologyHelpers.SanitizeName(topology.Name);
        var sg = new HclBuilder();

        // Check entire topology for LiveKit — it may be on hosts, standalone Caddies, or elastic
        var hasLiveKit = topology.Containers.Any(c => HasLiveKitRecursive(c));

        static bool HasLiveKitRecursive(Container c)
        {
            if (c.Images.Any(i => i.Kind == ImageKind.LiveKit)) return true;
            return c.Children.Any(HasLiveKitRecursive);
        }

        sg.Block($"resource \"aws_security_group\" \"{name}\"", b =>
        {
            b.Attribute("name", $"{topology.Name}-sg");
            b.Attribute("description", "Security group for xcord-topo deployment");
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();

            b.Block("dynamic \"ingress\"", db =>
            {
                db.RawAttribute("for_each", "length(var.ssh_cidr_blocks) > 0 ? [1] : []");
                db.Block("content", cb =>
                {
                    cb.Attribute("description", "SSH");
                    cb.Attribute("from_port", 22);
                    cb.Attribute("to_port", 22);
                    cb.Attribute("protocol", "tcp");
                    cb.RawAttribute("cidr_blocks", "var.ssh_cidr_blocks");
                });
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

    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var topoName = TopologyHelpers.SanitizeName(topology.Name);
        var instances = new HclBuilder();

        instances.Block($"resource \"aws_key_pair\" \"{topoName}\"", b =>
        {
            b.RawAttribute("count", "var.ssh_public_key != \"\" ? 1 : 0");
            b.Attribute("key_name", $"{topology.Name}-key");
            b.RawAttribute("public_key", "var.ssh_public_key");
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
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host);
            var instanceType = SelectPlan(entry.Host.Name, ramRequired, infraSelections);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"var.ssh_public_key != \"\" ? aws_key_pair.{topoName}[0].key_name : null");
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
                    tb.RawAttribute("Name", isReplicated
                        ? $"\"{topology.Name}-{entry.Host.Name}-${{count.index}}\""
                        : $"\"{topology.Name}-{entry.Host.Name}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });
            });
            instances.Line();
        }

        // ComputePool instances — one resource block per tier
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
                b.RawAttribute("key_name", $"var.ssh_public_key != \"\" ? aws_key_pair.{topoName}[0].key_name : null");
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
                    tb.RawAttribute("Name", $"\"{topology.Name}-{pool.TierProfile.Name}-${{count.index}}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });
            });
            instances.Line();
        }

        // Elastic image instances (replicas > 1, break out from hosts/caddies)
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
            var ramRequired = meta?.MinRamMb ?? 256;
            var instanceType = SelectPlan(image.Name, ramRequired, infraSelections);
            var varName = $"{resourceName}_replicas";

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("count", $"var.{varName}");
                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"var.ssh_public_key != \"\" ? aws_key_pair.{topoName}[0].key_name : null");
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
                    tb.RawAttribute("Name", $"\"{topology.Name}-{image.Name}-${{count.index}}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var ramRequired = TopologyHelpers.CalculateStandaloneCaddyRam(caddy);
            var instanceType = SelectPlan(caddy.Name, ramRequired, infraSelections);

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("ami", "data.aws_ami.ubuntu.id");
                b.Attribute("instance_type", instanceType);
                b.RawAttribute("subnet_id", $"aws_subnet.{topoName}.id");
                b.RawAttribute("vpc_security_group_ids", $"[aws_security_group.{topoName}.id]");
                b.RawAttribute("key_name", $"var.ssh_public_key != \"\" ? aws_key_pair.{topoName}[0].key_name : null");
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
            });
            instances.Line();
        }

        return instances.ToString();
    }

    private static string GenerateProvisioning(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver, Topology topology, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies)
    {
        var provisioning = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var images = TopologyHelpers.CollectImages(entry.Host);
            var caddies = TopologyHelpers.CollectCaddyContainers(entry.Host);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
            var useSwarm = TopologyHelpers.HostNeedsSwarmMode(entry.Host);

            if (images.Count == 0 && caddies.Count == 0) continue;

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                var depsList = new List<string> { $"aws_instance.{resourceName}" };

                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                var secrets = TopologyHelpers.CollectSecrets(entry, resolver);
                foreach (var secret in secrets)
                    depsList.Add($"random_password.{secret.ResourceName}");

                b.RawAttribute("depends_on", $"[{string.Join(", ", depsList)}]");
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", isReplicated
                        ? $"aws_instance.{resourceName}[count.index].public_ip"
                        : $"aws_instance.{resourceName}.public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");

                    if (useSwarm)
                    {
                        // Swarm mode — enables replicated services with built-in DNS load balancing
                        b.Line("  \"sudo docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                        b.Line("  \"sudo docker network create --driver overlay --attachable xcord-bridge\",");
                    }
                    else
                    {
                        b.Line("  \"sudo docker network create xcord-bridge\",");
                    }

                    // Docker login for private registry images
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    if (entry.Host.Kind == ContainerKind.DataPool)
                    {
                        b.Line("  \"PRIVATE_IP=$(hostname -I | awk '{print $1}')\",");
                    }

                    foreach (var image in images)
                    {
                        // Private registry images (Hub, Fed) are deployed post-push via deploy_apps phase
                        if (TopologyHelpers.RequiresPrivateRegistry(image.Kind))
                            continue;

                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
                        var envVars = TopologyHelpers.BuildEnvVars(image, entry, resolver, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, entry, resolver);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        // Publish ports if the image has cross-host consumers, needs direct external access,
                        // or lives on a DataPool (DataPool images are always accessed from other hosts)
                        var publishPorts = meta?.Ports.Length > 0 &&
                            (image.Kind == ImageKind.LiveKit ||
                             entry.Host.Kind == ContainerKind.DataPool ||
                             TopologyHelpers.HasCrossHostConsumers(image, entry.Host, resolver));

                        if (useSwarm)
                        {
                            var replicas = TopologyHelpers.GetImageReplicaExpression(image);
                            var flags = new List<string>
                            {
                                $"--name {containerName}",
                                $"--replicas {replicas}",
                                "--network xcord-bridge",
                                "--restart-condition any"
                            };

                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            if (meta?.MountPath != null)
                                flags.Add($"--mount type=volume,source={containerName}_data,target={meta.MountPath}");

                            if (publishPorts && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                {
                                    var bindPrefix = entry.Host.Kind == ContainerKind.DataPool ? "$PRIVATE_IP:" : "";
                                    flags.Add($"-p {bindPrefix}{port}:{port}");
                                }
                            }

                            if (image.Kind == ImageKind.Registry)
                                flags.Add("-p 5000:5000");

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"sudo docker service create {flagStr} {dockerImage}{cmd}\",");
                        }
                        else
                        {
                            var flags = new List<string>
                            {
                                "-d",
                                $"--name {containerName}",
                                "--network xcord-bridge",
                                "--restart unless-stopped"
                            };

                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            if (meta?.MountPath != null)
                                flags.Add($"-v {containerName}_data:{meta.MountPath}");

                            if (publishPorts && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                {
                                    var bindPrefix = entry.Host.Kind == ContainerKind.DataPool ? "$PRIVATE_IP:" : "";
                                    flags.Add($"-p {bindPrefix}{port}:{port}");
                                }
                            }

                            if (image.Kind == ImageKind.Registry)
                                flags.Add("-p 5000:5000");

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"sudo docker run {flagStr} {dockerImage}{cmd}\",");
                        }
                    }

                    foreach (var caddy in caddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);

                        b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");

                        if (useSwarm)
                            b.Line($"  \"sudo docker service create --name {caddyName} --replicas 1 --network xcord-bridge --restart-condition any -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        else
                            b.Line($"  \"sudo docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        // Always-on rate limiting for Caddy hosts
                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"sudo {rlCmd}\",");
                    }

                    // Registry images: configure htpasswd auth
                    foreach (var image in images.Where(i => i.Kind == ImageKind.Registry))
                    {
                        var registryName = TopologyHelpers.SanitizeName(image.Name);
                        var registrySubdomain = registryName;

                        // htpasswd auth when credentials are configured
                        b.Line($"  \"sudo mkdir -p /opt/registry/auth\",");
                        b.Line($"  \"sudo bash -c 'if [ -n \\\"${{var.registry_username}}\\\" ]; then sudo apt-get install -y -qq apache2-utils && sudo htpasswd -Bbn \\\"${{var.registry_username}}\\\" \\\"${{var.registry_password}}\\\" > /opt/registry/auth/htpasswd; fi'\",");

                        // Restart registry container with auth if htpasswd was created
                        b.Line($"  \"sudo bash -c 'if [ -f /opt/registry/auth/htpasswd ]; then sudo docker stop {registryName} 2>/dev/null; sudo docker rm {registryName} 2>/dev/null; sudo docker run -d --name {registryName} --network xcord-bridge --restart unless-stopped -v {registryName}_data:/var/lib/registry -v /opt/registry/auth:/auth -e REGISTRY_AUTH=htpasswd -e REGISTRY_AUTH_HTPASSWD_REALM=Registry -e REGISTRY_AUTH_HTPASSWD_PATH=/auth/htpasswd -p 5000:5000 registry:2; fi'\",");

                        // Only deploy a standalone Caddy sidecar for TLS if this host doesn't already have a Caddy container
                        // (when co-located with Caddy, the registry domain route is merged into the main Caddyfile)
                        if (caddies.Count == 0)
                        {
                            var registryCaddyfile = $"{registrySubdomain}.${{var.domain}} {{\\n  reverse_proxy {registryName}:5000\\n}}";
                            b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{registryCaddyfile}\\nCADDYEOF\",");
                            b.Line($"  \"sudo docker run -d --name caddy_registry --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }
                    }

                    var backupCommands = TopologyHelpers.GenerateBackupCommands(images, entry.Host);
                    foreach (var cmd in backupCommands)
                        b.Line($"  \"{cmd}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // ComputePool provisioning — one Swarm cluster per tier
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            // Secrets are shared across all tiers in the same pool — use pool-level name prefix
            var poolSecretPrefix = TopologyHelpers.SanitizeName(pool.Pool.Name);

            // Build depends_on from actual pool secrets (shared across tiers)
            var poolSecrets = TopologyHelpers.CollectPoolSecrets(pool);
            var secretDeps = string.Join(", ", poolSecrets.Select(s => $"random_password.{s.ResourceName}"));
            var dependsOn = string.IsNullOrEmpty(secretDeps)
                ? $"[aws_instance.{poolName}]"
                : $"[aws_instance.{poolName}, {secretDeps}]";

            // Manager provisioning (host 0) — init Swarm + deploy shared services
            provisioning.Block($"resource \"null_resource\" \"provision_{poolName}_manager\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count > 0 ? 1 : 0");
                b.RawAttribute("depends_on", dependsOn);
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{poolName}[0].public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");
                    b.Line("  \"sudo docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                    b.Line("  \"sudo docker network create --driver overlay --attachable xcord-pool\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");
                    b.Line("  \"sudo docker swarm join-token -q worker | sudo tee /tmp/swarm-worker-token > /dev/null\",");
                    b.Line("  \"cd /tmp && nohup sudo python3 -m http.server 9999 &\",");

                    // Deploy shared services from actual pool images (data-driven)
                    foreach (var image in pool.Pool.Images)
                    {
                        var cmd = TopologyHelpers.GenerateSwarmServiceCommand(image, poolSecretPrefix, resolver, useSudo: true, pool.Pool.Images);
                        if (cmd != null)
                            b.Line($"  \"{cmd}\",");
                    }

                    // Deploy Caddy containers with generated Caddyfiles
                    var poolCaddies = TopologyHelpers.CollectCaddyContainers(pool.Pool);
                    foreach (var caddy in poolCaddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);
                        b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                        b.Line($"  \"sudo docker service create --name {caddyName} --mode global --network xcord-pool -p 80:80 -p 443:443 -p 2019:2019 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"sudo {rlCmd}\",");
                    }
                    if (poolCaddies.Count == 0)
                    {
                        b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n\\nCADDYEOF\",");
                        b.Line($"  \"sudo docker service create --name caddy --mode global --network xcord-pool -p 80:80 -p 443:443 -p 2019:2019 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    }

                    pb.Line("]");
                });
            });
            provisioning.Line();

            // Worker provisioning (hosts 1+) — join Swarm
            provisioning.Block($"resource \"null_resource\" \"provision_{poolName}_workers\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count > 1 ? var.{poolName}_host_count - 1 : 0");
                b.RawAttribute("depends_on", $"[null_resource.provision_{poolName}_manager]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{poolName}[count.index + 1].public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");
                    b.Line($"  \"TOKEN=$(curl -sf --retry 10 --retry-delay 3 http://${{aws_instance.{poolName}[0].private_ip}}:9999/swarm-worker-token) && sudo docker swarm join --token $TOKEN ${{aws_instance.{poolName}[0].private_ip}}:2377\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Pre-compute host port assignments for all standalone Caddies (shared with elastic image env vars)
        var allPortAssignments = new Dictionary<Guid, Dictionary<int, int>>();
        foreach (var caddy in standaloneCaddies)
        {
            var assignments = TopologyHelpers.ComputeHostPortAssignments(caddy, resolver);
            foreach (var (imgId, ports) in assignments)
                allPortAssignments[imgId] = ports;
        }

        // Standalone Caddy provisioning
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, topology);

            // Collect secret dependencies for co-located images
            var caddySecrets = TopologyHelpers.CollectSecrets(new TopologyHelpers.HostEntry(caddy), resolver, excludePools: true);
            var depsList = new List<string> { $"aws_instance.{resourceName}" };
            foreach (var secret in caddySecrets)
                depsList.Add($"random_password.{secret.ResourceName}");

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                b.RawAttribute("depends_on", $"[{string.Join(", ", depsList)}]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{resourceName}.public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");
                    b.Line("  \"sudo docker network create xcord-bridge\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    // Deploy co-located non-elastic images on the Caddy host
                    var caddyEntry = new TopologyHelpers.HostEntry(caddy);
                    var coLocatedImages = TopologyHelpers.CollectImagesExcludingPools(caddy);
                    foreach (var image in coLocatedImages)
                    {
                        var (imgMin, imgMax) = TopologyHelpers.ParseReplicaRange(image.Config);
                        if (imgMin > 1 || imgMax > 1) continue; // Elastic — gets its own instance
                        if (TopologyHelpers.RequiresPrivateRegistry(image.Kind)) continue;

                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
                        var envVars = TopologyHelpers.BuildEnvVars(image, caddyEntry, resolver, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, caddyEntry, resolver);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        var flags = new List<string>
                        {
                            "-d",
                            $"--name {containerName}",
                            "--network xcord-bridge",
                            "--restart unless-stopped"
                        };

                        foreach (var (key, value) in envVars)
                            flags.Add($"-e {key}={value}");

                        // Use pre-computed port assignments to avoid host port conflicts
                        if (allPortAssignments.TryGetValue(image.Id, out var portMap))
                        {
                            foreach (var (containerPort, hostPort) in portMap)
                                flags.Add($"-p {hostPort}:{containerPort}");
                        }

                        if (image.Kind == ImageKind.Registry)
                            flags.Add("-p 5000:5000");

                        if (meta?.MountPath != null)
                            flags.Add($"-v {containerName}_data:{meta.MountPath}");

                        var flagStr = string.Join(" ", flags);
                        b.Line($"  \"sudo docker run {flagStr} {dockerImage}{cmd}\",");
                    }

                    b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                    var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                    var caddyfileLines = escapedCaddyfile.Split('\n');
                    b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                    b.Line($"  \"sudo docker run -d --name {resourceName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                    // Always-on rate limiting for standalone Caddy
                    var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                    foreach (var rlCmd in rateLimitCmds)
                        b.Line($"  \"sudo {rlCmd}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Elastic image provisioning — images with replicas > 1 get their own instances
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            if (TopologyHelpers.RequiresPrivateRegistry(image.Kind)) continue;

            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);

            // Find the parent container (Host or Caddy) for secret name resolution
            var parentContainer = resolver.FindHostFor(image.Id) ?? resolver.FindCaddyFor(image.Id);
            var parentEntry = parentContainer != null
                ? new TopologyHelpers.HostEntry(parentContainer)
                : new TopologyHelpers.HostEntry(new Container { Name = resourceName });
            // Elastic images run on their own instances — use a synthetic source host
            // so ResolveServiceHost knows this is NOT co-located with the parent
            var resolveFrom = new Container { Id = Guid.NewGuid(), Name = resourceName };
            var envVars = TopologyHelpers.BuildEnvVars(image, parentEntry, resolver, topology, resolveFrom, allPortAssignments);

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                var varName = $"{resourceName}_replicas";
                b.RawAttribute("count", $"var.{varName}");
                b.RawAttribute("depends_on", $"[aws_instance.{resourceName}]");
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{resourceName}[count.index].public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");
                    b.Line("  \"sudo docker network create xcord-bridge\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    var flags = new List<string>
                    {
                        "-d",
                        $"--name {resourceName}",
                        "--network xcord-bridge",
                        "--restart unless-stopped"
                    };

                    foreach (var (key, value) in envVars)
                        flags.Add($"-e {key}={value}");

                    if (meta?.MountPath != null)
                        flags.Add($"-v {resourceName}_data:{meta.MountPath}");

                    if (meta?.Ports.Length > 0)
                    {
                        foreach (var port in meta.Ports)
                            flags.Add($"-p {port}:{port}");
                    }

                    var flagStr = string.Join(" ", flags);
                    b.Line($"  \"sudo docker run {flagStr} {dockerImage}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Application deployment — private registry images, gated by deploy_apps variable.
        // These run after image push via a second `terraform apply -var deploy_apps=true`.
        GenerateAppDeployResources(provisioning, hosts, standaloneCaddies, elasticImages, resolver, topology, allPortAssignments);

        return provisioning.ToString();
    }

    private static void GenerateAppDeployResources(
        HclBuilder provisioning,
        List<TopologyHelpers.HostEntry> hosts,
        List<Container> standaloneCaddies,
        List<Image> elasticImages,
        WireResolver resolver,
        Topology topology,
        Dictionary<Guid, Dictionary<int, int>> allPortAssignments)
    {
        // Hosts with private-registry images
        foreach (var entry in hosts)
        {
            var images = TopologyHelpers.CollectImages(entry.Host);
            var privateImages = images.Where(i => TopologyHelpers.RequiresPrivateRegistry(i.Kind)).ToList();
            if (privateImages.Count == 0) continue;

            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
            var useSwarm = TopologyHelpers.HostNeedsSwarmMode(entry.Host);

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (isReplicated)
                    b.RawAttribute("count", $"var.deploy_apps ? {countExpr ?? "1"} : 0");
                else
                    b.RawAttribute("count", "var.deploy_apps ? 1 : 0");

                b.RawAttribute("depends_on", $"[null_resource.provision_{resourceName}]");
                b.Line();

                // Force recreation when app version changes
                b.MapBlock("triggers", tb =>
                {
                    tb.RawAttribute("app_version", "var.app_version");
                });
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", isReplicated
                        ? $"aws_instance.{resourceName}[count.index].public_ip"
                        : $"aws_instance.{resourceName}.public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    foreach (var image in privateImages)
                    {
                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
                        var envVars = TopologyHelpers.BuildEnvVars(image, entry, resolver, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, entry, resolver);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        var publishPorts = meta?.Ports.Length > 0 &&
                            (TopologyHelpers.HasCrossHostConsumers(image, entry.Host, resolver));

                        // Remove existing container (re-deploy case), pull, and run
                        b.Line($"  \"sudo docker rm -f {containerName} 2>/dev/null || true\",");
                        b.Line($"  \"sudo docker pull {dockerImage}\",");

                        if (useSwarm)
                        {
                            var replicas = TopologyHelpers.GetImageReplicaExpression(image);
                            var flags = new List<string>
                            {
                                $"--name {containerName}",
                                $"--replicas {replicas}",
                                "--network xcord-bridge",
                                "--restart-condition any"
                            };

                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            if (meta?.MountPath != null)
                                flags.Add($"--mount type=volume,source={containerName}_data,target={meta.MountPath}");

                            if (publishPorts && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                    flags.Add($"-p {port}:{port}");
                            }

                            var flagStr = string.Join(" ", flags);
                            // For swarm re-deploy, remove service then recreate
                            b.Line($"  \"sudo docker service rm {containerName} 2>/dev/null || true\",");
                            b.Line($"  \"sudo docker service create {flagStr} {dockerImage}{cmd}\",");
                        }
                        else
                        {
                            var flags = new List<string>
                            {
                                "-d",
                                $"--name {containerName}",
                                "--network xcord-bridge",
                                "--restart unless-stopped"
                            };

                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            if (meta?.MountPath != null)
                                flags.Add($"-v {containerName}_data:{meta.MountPath}");

                            if (publishPorts && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                    flags.Add($"-p {port}:{port}");
                            }

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"sudo docker run {flagStr} {dockerImage}{cmd}\",");
                        }
                    }

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Standalone Caddies with private-registry co-located images
        foreach (var caddy in standaloneCaddies)
        {
            var coLocatedImages = TopologyHelpers.CollectImagesExcludingPools(caddy);
            var privateImages = coLocatedImages
                .Where(i => TopologyHelpers.RequiresPrivateRegistry(i.Kind))
                .Where(i =>
                {
                    var (min, max) = TopologyHelpers.ParseReplicaRange(i.Config);
                    return min <= 1 && max <= 1; // Non-elastic only
                })
                .ToList();
            if (privateImages.Count == 0) continue;

            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var caddyEntry = new TopologyHelpers.HostEntry(caddy);

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}_apps\"", b =>
            {
                b.RawAttribute("count", "var.deploy_apps ? 1 : 0");
                b.RawAttribute("depends_on", $"[null_resource.provision_{resourceName}]");
                b.Line();
                b.MapBlock("triggers", tb => tb.RawAttribute("app_version", "var.app_version"));
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{resourceName}.public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    foreach (var image in privateImages)
                    {
                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
                        var envVars = TopologyHelpers.BuildEnvVars(image, caddyEntry, resolver, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, caddyEntry, resolver);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        b.Line($"  \"sudo docker rm -f {containerName} 2>/dev/null || true\",");
                        b.Line($"  \"sudo docker pull {dockerImage}\",");

                        var flags = new List<string> { "-d", $"--name {containerName}", "--network xcord-bridge", "--restart unless-stopped" };
                        foreach (var (key, value) in envVars) flags.Add($"-e {key}={value}");
                        if (allPortAssignments.TryGetValue(image.Id, out var portMap))
                            foreach (var (containerPort, hostPort) in portMap) flags.Add($"-p {hostPort}:{containerPort}");
                        if (meta?.MountPath != null) flags.Add($"-v {containerName}_data:{meta.MountPath}");

                        b.Line($"  \"sudo docker run {string.Join(" ", flags)} {dockerImage}{cmd}\",");
                    }
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Elastic private-registry images
        foreach (var image in elasticImages)
        {
            if (!TopologyHelpers.RequiresPrivateRegistry(image.Kind)) continue;

            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology));
            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
            var parentContainer = resolver.FindHostFor(image.Id) ?? resolver.FindCaddyFor(image.Id);
            var parentEntry = parentContainer != null
                ? new TopologyHelpers.HostEntry(parentContainer)
                : new TopologyHelpers.HostEntry(new Container { Name = resourceName });
            var resolveFrom = new Container { Id = Guid.NewGuid(), Name = resourceName };
            var envVars = TopologyHelpers.BuildEnvVars(image, parentEntry, resolver, topology, resolveFrom, allPortAssignments);

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}\"", b =>
            {
                var varName = $"{resourceName}_replicas";
                b.RawAttribute("count", $"var.deploy_apps ? var.{varName} : 0");
                b.RawAttribute("depends_on", $"[aws_instance.{resourceName}]");
                b.Line();
                b.MapBlock("triggers", tb => tb.RawAttribute("app_version", "var.app_version"));
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{resourceName}[count.index].public_ip");
                    cb.Attribute("user", "ubuntu");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: true)}\",");

                    b.Line($"  \"sudo docker rm -f {resourceName} 2>/dev/null || true\",");
                    b.Line($"  \"sudo docker pull {dockerImage}\",");

                    var flags = new List<string> { "-d", $"--name {resourceName}", "--network xcord-bridge", "--restart unless-stopped" };
                    foreach (var (key, value) in envVars) flags.Add($"-e {key}={value}");
                    if (meta?.MountPath != null) flags.Add($"-v {resourceName}_data:{meta.MountPath}");
                    if (meta?.Ports.Length > 0)
                        foreach (var port in meta.Ports) flags.Add($"-p {port}:{port}");

                    b.Line($"  \"sudo docker run {string.Join(" ", flags)} {dockerImage}\",");
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }
    }
}
