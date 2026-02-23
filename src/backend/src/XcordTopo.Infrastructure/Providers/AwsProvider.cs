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
        SupportedContainerKinds = ["Host", "Network", "Caddy", "FederationGroup"]
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
            }
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
            }
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
            }
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
            }
        }
    ];

    public Dictionary<string, string> GenerateHcl(Topology topology)
    {
        var files = new Dictionary<string, string>();
        var hosts = LinodeProvider.CollectHosts(topology.Containers, null);
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["variables.tf"] = GenerateVariables(topology);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver);
        files["network.tf"] = GenerateNetwork(topology);
        files["security_groups.tf"] = GenerateSecurityGroups(topology, hosts);
        files["instances.tf"] = GenerateInstances(topology, hosts);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology);
        files["outputs.tf"] = GenerateOutputs(hosts);

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

    private static string GenerateVariables(Topology topology)
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
        var hosts = LinodeProvider.CollectHosts(topology.Containers, null);
        CollectHostReplicaVariables(hosts, vars);

        return vars.ToString();
    }

    private static void CollectFedGroupVariables(List<Container> containers, HclBuilder vars)
    {
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.FederationGroup)
            {
                var countValue = container.Config.GetValueOrDefault("instanceCount", "1");
                var varName = $"{LinodeProvider.SanitizeName(container.Name)}_instance_count";
                vars.Line();
                vars.Block($"variable \"{varName}\"", b =>
                {
                    b.Attribute("type", "number");
                    if (IsVariableRef(countValue))
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

    private static void CollectHostReplicaVariables(List<LinodeProvider.HostEntry> hosts, HclBuilder vars)
    {
        foreach (var entry in hosts)
        {
            if (entry.FedGroup != null) continue;

            var (literal, varRef) = LinodeProvider.ParseHostReplicas(entry.Host);
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");
            var needsVariable = varRef != null || (literal.HasValue && literal.Value > 1 && hasMinMax);
            if (!needsVariable) continue;

            var varName = varRef != null ? LinodeProvider.SanitizeName(varRef) : $"{LinodeProvider.SanitizeName(entry.Host.Name)}_replicas";
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

    private static bool IsVariableRef(string value) =>
        value.StartsWith('$') && value.Length > 1 && !value.Contains(' ');

    private static string GenerateSecrets(List<LinodeProvider.HostEntry> hosts, WireResolver resolver)
    {
        var secrets = new HclBuilder();
        foreach (var entry in hosts)
        {
            var allSecrets = LinodeProvider.CollectSecrets(entry, resolver);
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
        var name = LinodeProvider.SanitizeName(topology.Name);
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

    private static string GenerateSecurityGroups(Topology topology, List<LinodeProvider.HostEntry> hosts)
    {
        var name = LinodeProvider.SanitizeName(topology.Name);
        var sg = new HclBuilder();

        var hasLiveKit = hosts.Any(e =>
            e.FedGroup == null && LinodeProvider.CollectImages(e.Host).Any(i => i.Kind == ImageKind.LiveKit));

        sg.Block($"resource \"aws_security_group\" \"{name}\"", b =>
        {
            b.Attribute("name", $"{topology.Name}-sg");
            b.Attribute("description", "Security group for xcord-topo deployment");
            b.RawAttribute("vpc_id", $"aws_vpc.{name}.id");
            b.Line();

            // SSH
            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "SSH");
                ib.Attribute("from_port", 22);
                ib.Attribute("to_port", 22);
                ib.Attribute("protocol", "tcp");
                ib.RawAttribute("cidr_blocks", "[\"0.0.0.0/0\"]");
            });

            // HTTP/HTTPS
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

            // Internal traffic within VPC
            b.Block("ingress", ib =>
            {
                ib.Attribute("description", "Internal VPC traffic");
                ib.Attribute("from_port", 0);
                ib.Attribute("to_port", 0);
                ib.Attribute("protocol", "-1");
                ib.RawAttribute("self", "true");
            });

            // All outbound
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

    private string GenerateInstances(Topology topology, List<LinodeProvider.HostEntry> hosts)
    {
        var topoName = LinodeProvider.SanitizeName(topology.Name);
        var instances = new HclBuilder();

        // Key pair (conditional on ssh key being set)
        instances.Block($"resource \"aws_key_pair\" \"{topoName}\"", b =>
        {
            b.RawAttribute("count", "var.ssh_public_key != \"\" ? 1 : 0");
            b.Attribute("key_name", $"{topology.Name}-key");
            b.RawAttribute("public_key", "var.ssh_public_key");
        });
        instances.Line();

        // Ubuntu 24.04 AMI lookup
        instances.Block($"data \"aws_ami\" \"ubuntu\"", b =>
        {
            b.RawAttribute("most_recent", "true");
            b.RawAttribute("owners", "[\"099720109477\"]"); // Canonical

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
            var resourceName = LinodeProvider.SanitizeName(entry.Host.Name);
            var ramRequired = LinodeProvider.CalculateHostRam(entry.Host);
            var instanceType = SelectPlan(ramRequired);
            var isReplicated = LinodeProvider.IsReplicatedHost(entry);

            instances.Block($"resource \"aws_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = LinodeProvider.GetHostCountExpression(entry);
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
        return instances.ToString();
    }

    private static string GenerateProvisioning(List<LinodeProvider.HostEntry> hosts, WireResolver resolver, Topology topology)
    {
        var provisioning = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = LinodeProvider.SanitizeName(entry.Host.Name);
            var hostName = LinodeProvider.SanitizeName(entry.Host.Name);
            var images = LinodeProvider.CollectImages(entry.Host);
            var caddies = LinodeProvider.CollectCaddyContainers(entry.Host);
            var isFederation = entry.FedGroup != null;
            var isReplicated = LinodeProvider.IsReplicatedHost(entry);

            if (images.Count == 0 && caddies.Count == 0) continue;

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                var depsList = new List<string> { $"aws_instance.{resourceName}" };

                var countExpr = LinodeProvider.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                if (!isFederation)
                {
                    var secrets = LinodeProvider.CollectSecrets(entry, resolver);
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

                    // Docker installation
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"sudo usermod -aG docker ubuntu\",");
                    b.Line("  \"sudo systemctl enable docker\",");
                    b.Line("  \"sudo systemctl start docker\",");

                    if (!isFederation)
                    {
                        b.Line("  \"sudo docker network create xcord-bridge\",");

                        foreach (var image in images)
                        {
                            var dockerImage = image.DockerImage ?? GetDefaultDockerImage(image.Kind);
                            var containerName = LinodeProvider.SanitizeName(image.Name);
                            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);

                            var flags = new List<string>
                            {
                                "-d",
                                $"--name {containerName}",
                                "--network xcord-bridge",
                                "--restart unless-stopped"
                            };

                            var envVars = LinodeProvider.BuildEnvVars(image, entry, resolver);
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

                            var cmdOverride = LinodeProvider.ResolveCommandOverride(image, entry, resolver);
                            var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"sudo docker run {flagStr} {dockerImage}{cmd}\",");
                        }

                        foreach (var caddy in caddies)
                        {
                            var caddyfile = LinodeProvider.GenerateCaddyfile(caddy, resolver);
                            var caddyName = LinodeProvider.SanitizeName(caddy.Name);

                            b.Line($"  \"sudo mkdir -p /opt/caddy\",");
                            var escapedCaddyfile = caddyfile.Replace("\"", "\\\"").Replace("$", "\\$");
                            var caddyfileLines = escapedCaddyfile.Split('\n');
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                            b.Line($"  \"sudo docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }

                        var backupCommands = LinodeProvider.GenerateBackupCommands(images, entry.Host);
                        foreach (var cmd in backupCommands)
                            b.Line($"  \"{cmd}\",");
                    }

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }
        return provisioning.ToString();
    }

    private static string GenerateOutputs(List<LinodeProvider.HostEntry> hosts)
    {
        var outputs = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = LinodeProvider.SanitizeName(entry.Host.Name);
            if (LinodeProvider.IsReplicatedHost(entry))
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
        return outputs.ToString();
    }

    private static string GetDefaultDockerImage(ImageKind kind) => kind switch
    {
        ImageKind.HubServer => "ghcr.io/xcord/hub:latest",
        ImageKind.FederationServer => "ghcr.io/xcord/fed:latest",
        ImageKind.Redis => "redis:7-alpine",
        ImageKind.PostgreSQL => "postgres:17-alpine",
        ImageKind.MinIO => "minio/minio:latest",
        ImageKind.LiveKit => "livekit/livekit-server:latest",
        ImageKind.Custom => "alpine:latest",
        _ => "alpine:latest"
    };
}
