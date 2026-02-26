using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class AwsProvider : ICloudProvider
{
    public string Key => "aws";

    public ProviderInfo GetInfo() => new()
    {
        Key = "aws",
        Name = "Amazon Web Services",
        Description = "AWS EC2 instances with VPC networking. The most widely used cloud platform.",
        SupportedContainerKinds = ["Host", "Network", "Caddy", "FederationGroup", "ComputePool", "Dns"]
    };

    public List<Region> GetRegions() =>
    [
        new() { Id = "us-east-1", Label = "US East (Virginia)", Country = "US" },
        new() { Id = "us-east-2", Label = "US East (Ohio)", Country = "US" },
        new() { Id = "us-west-2", Label = "US West (Oregon)", Country = "US" },
        new() { Id = "eu-west-1", Label = "Europe (Ireland)", Country = "IE" },
        new() { Id = "eu-central-1", Label = "Europe (Frankfurt)", Country = "DE" },
        new() { Id = "ap-southeast-1", Label = "Asia Pacific (Singapore)", Country = "SG" },
        new() { Id = "ap-northeast-1", Label = "Asia Pacific (Tokyo)", Country = "JP" },
        new() { Id = "ap-southeast-2", Label = "Asia Pacific (Sydney)", Country = "AU" },
    ];

    public List<ComputePlan> GetPlans() =>
    [
        new() { Id = "t3.micro", Label = "T3 Micro (1GB)", VCpus = 2, MemoryMb = 1024, DiskGb = 8, PriceMonthly = 7.60m },
        new() { Id = "t3.small", Label = "T3 Small (2GB)", VCpus = 2, MemoryMb = 2048, DiskGb = 20, PriceMonthly = 15.20m },
        new() { Id = "t3.medium", Label = "T3 Medium (4GB)", VCpus = 2, MemoryMb = 4096, DiskGb = 40, PriceMonthly = 30.40m },
        new() { Id = "t3.large", Label = "T3 Large (8GB)", VCpus = 2, MemoryMb = 8192, DiskGb = 80, PriceMonthly = 60.70m },
        new() { Id = "t3.xlarge", Label = "T3 XLarge (16GB)", VCpus = 4, MemoryMb = 16384, DiskGb = 160, PriceMonthly = 121.50m },
        new() { Id = "m5.large", Label = "M5 Large (8GB)", VCpus = 2, MemoryMb = 8192, DiskGb = 80, PriceMonthly = 70.00m },
        new() { Id = "m5.xlarge", Label = "M5 XLarge (16GB)", VCpus = 4, MemoryMb = 16384, DiskGb = 160, PriceMonthly = 140.00m },
    ];

    public List<CredentialField> GetCredentialSchema() =>
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
                    "Choose the region closest to your users",
                    "All instances will be deployed in this region",
                    "Consider data residency and compliance requirements"
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

    public Dictionary<string, string> GenerateHcl(Topology topology)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(topology.Containers, null);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology);
        var dnsContainers = TopologyHelpers.CollectDnsContainers(topology.Containers);
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver);
        files["network.tf"] = GenerateNetwork(topology);
        files["security_groups.tf"] = GenerateSecurityGroups(topology, hosts);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools);
        files["outputs.tf"] = GenerateOutputs(hosts, pools);

        if (dnsContainers.Count > 0)
            files["dns.tf"] = GenerateDnsRecords(dnsContainers, hosts, resolver, topology);

        return files;
    }

    public Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(ownedContainers.ToList(), null);
        var pools = TopologyHelpers.CollectComputePools(ownedContainers.ToList(), topology);
        var dnsContainers = ownedContainers.Where(c => c.Kind == ContainerKind.Dns).ToList();
        var resolver = new WireResolver(topology);

        files["main_aws.tf"] = GenerateMain();
        files["variables_aws.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver);
        files["network_aws.tf"] = GenerateNetwork(topology);
        files["security_groups_aws.tf"] = GenerateSecurityGroups(topology, hosts);
        files["instances_aws.tf"] = GenerateInstances(topology, hosts, pools);
        files["provisioning_aws.tf"] = GenerateProvisioning(hosts, resolver, topology, pools);
        files["outputs_aws.tf"] = GenerateOutputs(hosts, pools);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers, null);
        if (dnsContainers.Count > 0)
            files["dns_aws.tf"] = GenerateDnsRecords(dnsContainers, allHosts, resolver, topology);

        return files;
    }

    internal string SelectPlan(int requiredRamMb)
    {
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var plan in plans)
        {
            if (plan.MemoryMb >= requiredRamMb)
                return plan.Id;
        }
        return plans.Last().Id;
    }

    // --- DNS record generation ---

    private string GenerateDnsRecords(
        List<Container> dnsContainers,
        List<TopologyHelpers.HostEntry> allHosts,
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

            var wiredHosts = TopologyHelpers.CollectHostsWiredToDns(dnsContainer, resolver, allHosts);
            foreach (var entry in wiredHosts)
            {
                var hostName = TopologyHelpers.SanitizeName(entry.Host.Name);
                var providerKey = TopologyHelpers.ResolveProviderKey(entry.Host, topology);
                var ipRef = GetIpReference(hostName, providerKey, TopologyHelpers.IsReplicatedHost(entry));

                dns.Block($"resource \"aws_route53_record\" \"{hostName}\"", b =>
                {
                    b.RawAttribute("zone_id", $"data.aws_route53_zone.{sanitizedDomain}.zone_id");
                    b.RawAttribute("name", $"\"{hostName}.${{var.domain}}\"");
                    b.Attribute("type", "A");
                    b.Attribute("ttl", 300);
                    b.RawAttribute("records", $"[{ipRef}]");
                });
                dns.Line();
            }
        }

        return dns.ToString();
    }

    internal static string GetIpReference(string hostName, string providerKey, bool isReplicated)
    {
        if (string.Equals(providerKey, "linode", StringComparison.OrdinalIgnoreCase))
            return isReplicated ? $"linode_instance.{hostName}[0].ip_address" : $"linode_instance.{hostName}.ip_address";

        return isReplicated ? $"aws_instance.{hostName}[0].public_ip" : $"aws_instance.{hostName}.public_ip";
    }

    // --- File generators ---

    private static string GenerateMain()
    {
        var main = new HclBuilder();
        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                p.Block("aws", ap =>
                {
                    ap.Attribute("source", "hashicorp/aws");
                    ap.Attribute("version", "~> 5.0");
                });
                p.Block("random", rp =>
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
            b.Attribute("type", "string");
            b.Attribute("description", "AWS access key ID");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"aws_secret_access_key\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("description", "AWS secret access key");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"region\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("region", "us-east-1"));
            b.Attribute("description", "AWS region");
        });
        vars.Line();
        vars.Block("variable \"domain\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("description", "Primary domain name");
        });
        vars.Line();
        vars.Block("variable \"ssh_public_key\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("default", "");
            b.Attribute("description", "SSH public key for instance access");
        });

        // FederationGroup instance_count variables
        CollectFedGroupVariables(topology.Containers, vars);

        // Host replica variables
        var hosts = TopologyHelpers.CollectHosts(topology.Containers, null);
        CollectHostReplicaVariables(hosts, vars);

        // ComputePool variables
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
            var sharedOverhead = ImageOperationalMetadata.CalculateSharedOverheadMb();
            var minHostRam = sharedOverhead + fedMemory;
            var selectedPlan = plans.FirstOrDefault(p => p.MemoryMb >= minHostRam) ?? plans.Last();
            var tenantsPerHost = ImageOperationalMetadata.CalculateTenantsPerHost(selectedPlan.MemoryMb, pool.TierProfile);
            var hostsRequired = ImageOperationalMetadata.CalculateHostsRequired(pool.TargetTenants, tenantsPerHost);

            vars.Line();
            vars.Block($"variable \"{poolName}_host_count\"", b =>
            {
                b.Attribute("type", "number");
                b.Attribute("default", hostsRequired);
                b.Attribute("description", $"Number of compute hosts for pool '{pool.Pool.Name}' ({pool.TierProfile.Name}, {pool.TargetTenants} tenants)");
            });
            vars.Line();
            vars.Block($"variable \"{poolName}_tenants_per_host\"", b =>
            {
                b.Attribute("type", "number");
                b.Attribute("default", tenantsPerHost > 0 ? tenantsPerHost : 1);
                b.Attribute("description", $"Number of tenants per host in pool '{pool.Pool.Name}'");
            });
        }

        // Service key variables
        foreach (var field in ServiceKeySchema.GetSchema())
        {
            if (!topology.ServiceKeys.ContainsKey(field.Key)) continue;

            vars.Line();
            vars.Block($"variable \"{field.Key}\"", b =>
            {
                b.Attribute("type", "string");
                if (field.Sensitive)
                    b.RawAttribute("sensitive", "true");
                b.Attribute("description", field.Label);
                b.Attribute("default", "");
            });
        }

        return vars.ToString();
    }

    private static void CollectFedGroupVariables(List<Container> containers, HclBuilder vars)
    {
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.FederationGroup)
            {
                var countValue = container.Config.GetValueOrDefault("instanceCount", "1");
                var varName = $"{TopologyHelpers.SanitizeName(container.Name)}_instance_count";
                vars.Line();
                vars.Block($"variable \"{varName}\"", b =>
                {
                    b.Attribute("type", "number");
                    if (TopologyHelpers.IsVariableRef(countValue))
                        b.Attribute("default", 1);
                    else
                        b.Attribute("default", int.TryParse(countValue, out var n) ? n : 1);
                    b.Attribute("description", $"Number of instances in federation group '{container.Name}'");
                });
            }
            foreach (var child in container.Children)
            {
                if (child.Kind == ContainerKind.FederationGroup)
                    CollectFedGroupVariables([child], vars);
            }
        }
    }

    private static void CollectHostReplicaVariables(List<TopologyHelpers.HostEntry> hosts, HclBuilder vars)
    {
        foreach (var entry in hosts)
        {
            if (entry.FedGroup != null) continue;

            var (literal, varRef) = TopologyHelpers.ParseHostReplicas(entry.Host);
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");
            var needsVariable = varRef != null || (literal.HasValue && literal.Value > 1 && hasMinMax);
            if (!needsVariable) continue;

            var varName = varRef != null ? TopologyHelpers.SanitizeName(varRef) : $"{TopologyHelpers.SanitizeName(entry.Host.Name)}_replicas";
            var defaultValue = literal ?? 1;

            var minStr = entry.Host.Config.GetValueOrDefault("minReplicas", "");
            var maxStr = entry.Host.Config.GetValueOrDefault("maxReplicas", "");
            var hasMin = int.TryParse(minStr, out var minVal);
            var hasMax = int.TryParse(maxStr, out var maxVal);

            vars.Line();
            vars.Block($"variable \"{varName}\"", b =>
            {
                b.Attribute("type", "number");
                b.Attribute("default", defaultValue);
                b.Attribute("description", $"Number of replicas for host '{entry.Host.Name}'");

                if (hasMin || hasMax)
                {
                    b.Block("validation", vb =>
                    {
                        if (hasMin && hasMax)
                        {
                            vb.RawAttribute("condition", $"var.{varName} >= {minVal} && var.{varName} <= {maxVal}");
                            vb.Attribute("error_message", $"Replicas must be between {minVal} and {maxVal}.");
                        }
                        else if (hasMin)
                        {
                            vb.RawAttribute("condition", $"var.{varName} >= {minVal}");
                            vb.Attribute("error_message", $"Replicas must be at least {minVal}.");
                        }
                        else
                        {
                            vb.RawAttribute("condition", $"var.{varName} <= {maxVal}");
                            vb.Attribute("error_message", $"Replicas must be at most {maxVal}.");
                        }
                    });
                }
            });
        }
    }

    private static string GenerateSecrets(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver)
    {
        var secrets = new HclBuilder();
        foreach (var entry in hosts)
        {
            var allSecrets = TopologyHelpers.CollectSecrets(entry, resolver);
            foreach (var secret in allSecrets)
            {
                secrets.Block($"resource \"random_password\" \"{secret.ResourceName}\"", b =>
                {
                    b.Attribute("length", 32);
                    b.RawAttribute("special", "false");
                });
                secrets.Line();
            }
        }
        return secrets.ToString();
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
            b.Block("tags", tb =>
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
            b.Block("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-subnet");
            });
        });
        net.Line();

        net.Block($"resource \"aws_internet_gateway\" \"{name}\"", b =>
        {
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();
            b.Block("tags", tb =>
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
            b.Block("tags", tb =>
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

        var hasLiveKit = hosts.Any(e =>
            e.FedGroup == null && TopologyHelpers.CollectImages(e.Host).Any(i => i.Kind == ImageKind.LiveKit));

        sg.Block($"resource \"aws_security_group\" \"{name}\"", b =>
        {
            b.Attribute("name", $"{topology.Name}-sg");
            b.Attribute("description", "Security group for xcord-topo deployment");
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();

            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "SSH");
                ib.Attribute("from_port", 22);
                ib.Attribute("to_port", 22);
                ib.Attribute("protocol", "tcp");
                ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
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
            b.Block("tags", tb =>
            {
                tb.Attribute("Name", $"{topology.Name}-sg");
                tb.Attribute("Project", "xcord-topo");
            });
        });

        return sg.ToString();
    }

    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools)
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
            var instanceType = SelectPlan(ramRequired);
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
                });

                b.Line();
                b.Block("tags", tb =>
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

        // ComputePool instances
        var allPlans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
            var sharedOverhead = ImageOperationalMetadata.CalculateSharedOverheadMb();
            var minHostRam = sharedOverhead + fedMemory;
            var selectedPlan = allPlans.FirstOrDefault(p => p.MemoryMb >= minHostRam) ?? allPlans.Last();

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
                });
                b.Line();
                b.Block("tags", tb =>
                {
                    tb.RawAttribute("Name", $"\"{topology.Name}-{pool.Pool.Name}-${{count.index}}\"");
                    tb.Attribute("Project", "xcord-topo");
                    tb.Attribute("Topology", topology.Name);
                });
            });
            instances.Line();
        }

        return instances.ToString();
    }

    private static string GenerateProvisioning(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver, Topology topology, List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var provisioning = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var hostName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var images = TopologyHelpers.CollectImages(entry.Host);
            var caddies = TopologyHelpers.CollectCaddyContainers(entry.Host);
            var isFederation = entry.FedGroup != null;
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);

            if (images.Count == 0 && caddies.Count == 0) continue;

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                var depsList = new List<string> { $"aws_instance.{resourceName}" };

                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                if (!isFederation)
                {
                    var secrets = TopologyHelpers.CollectSecrets(entry, resolver);
                    foreach (var secret in secrets)
                        depsList.Add($"random_password.{secret.ResourceName}");
                }

                b.RawAttribute("depends_on", $"[{string.Join(", ", depsList)}]");
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", isReplicated
                        ? $"aws_instance.{resourceName}[count.index].public_ip"
                        : $"aws_instance.{resourceName}.public_ip");
                    cb.Attribute("user", "ubuntu");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");

                    if (!isFederation)
                    {
                        b.Line("  \"sudo docker network create xcord-bridge\",");

                        foreach (var image in images)
                        {
                            var dockerImage = image.DockerImage ?? TopologyHelpers.GetDefaultDockerImage(image.Kind);
                            var containerName = TopologyHelpers.SanitizeName(image.Name);
                            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);

                            var flags = new List<string>
                            {
                                "-d",
                                $"--name {containerName}",
                                "--network xcord-bridge",
                                "--restart unless-stopped"
                            };

                            var envVars = TopologyHelpers.BuildEnvVars(image, entry, resolver, topology);
                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
                            if (!string.IsNullOrEmpty(volumeSize) && meta?.MountPath != null)
                                flags.Add($"-v {containerName}_data:{meta.MountPath}");

                            if (image.Kind == ImageKind.LiveKit && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                    flags.Add($"-p {port}:{port}");
                            }

                            var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, entry, resolver);
                            var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"sudo docker run {flagStr} {dockerImage}{cmd}\",");
                        }

                        foreach (var caddy in caddies)
                        {
                            var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver);
                            var caddyName = TopologyHelpers.SanitizeName(caddy.Name);

                            b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                            var escapedCaddyfile = caddyfile.Replace("\"", "\\\"").Replace("$", "\\$");
                            var caddyfileLines = escapedCaddyfile.Split('\n');
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                            b.Line($"  \"sudo docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }

                        var backupCommands = TopologyHelpers.GenerateBackupCommands(images, entry.Host);
                        foreach (var cmd in backupCommands)
                            b.Line($"  \"{cmd}\",");
                    }

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // ComputePool provisioning
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
            var fedCpuMillicores = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.CpuMillicores ?? 250;
            var cpuLimit = fedCpuMillicores / 1000.0;

            provisioning.Block($"resource \"null_resource\" \"provision_{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.RawAttribute("depends_on", $"[aws_instance.{poolName}]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"aws_instance.{poolName}[count.index].public_ip");
                    cb.Attribute("user", "ubuntu");
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
                    b.Line("  \"sudo docker run -d --name shared-postgres --network xcord-bridge --restart unless-stopped --memory 1024m -v pgdata:/var/lib/postgresql/data -e POSTGRES_PASSWORD=changeme -e POSTGRES_USER=postgres postgres:17-alpine\",");
                    b.Line("  \"sudo docker run -d --name shared-redis --network xcord-bridge --restart unless-stopped --memory 512m -v redisdata:/data redis:7-alpine redis-server --requirepass changeme\",");
                    b.Line("  \"sudo docker run -d --name shared-minio --network xcord-bridge --restart unless-stopped --memory 512m -v miniodata:/data -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin minio/minio:latest server /data --console-address :9001\",");
                    b.Line($"  \"sudo docker run -d --name caddy --network xcord-bridge --restart unless-stopped --memory 128m -p 80:80 -p 443:443 -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    b.Line($"  \"for i in $(seq 0 $((var.{poolName}_tenants_per_host - 1))); do sudo docker run -d --name tenant-$i --network xcord-bridge --restart unless-stopped --memory {fedMemory}m --cpus {cpuLimit:F1} ghcr.io/xcord/fed:latest; done\",");
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        return provisioning.ToString();
    }

    private static string GenerateOutputs(List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var outputs = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            if (TopologyHelpers.IsReplicatedHost(entry))
            {
                outputs.Block($"output \"{resourceName}_ips\"", b =>
                {
                    b.RawAttribute("value", $"aws_instance.{resourceName}[*].public_ip");
                    b.Attribute("description", $"Public IPs of {entry.Host.Name} instances");
                });
                outputs.Line();
                outputs.Block($"output \"{resourceName}_private_ips\"", b =>
                {
                    b.RawAttribute("value", $"aws_instance.{resourceName}[*].private_ip");
                    b.Attribute("description", $"Private IPs of {entry.Host.Name} instances");
                });
            }
            else
            {
                outputs.Block($"output \"{resourceName}_ip\"", b =>
                {
                    b.RawAttribute("value", $"aws_instance.{resourceName}.public_ip");
                    b.Attribute("description", $"Public IP of {entry.Host.Name}");
                });
            }
            outputs.Line();
        }

        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            outputs.Block($"output \"{poolName}_ips\"", b =>
            {
                b.RawAttribute("value", $"aws_instance.{poolName}[*].public_ip");
                b.Attribute("description", $"Public IPs of compute pool '{pool.Pool.Name}'");
            });
            outputs.Line();
        }

        return outputs.ToString();
    }
}
