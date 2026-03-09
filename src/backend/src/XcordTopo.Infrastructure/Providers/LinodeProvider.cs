using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class LinodeProvider : ICloudProvider
{
    public string Key => "linode";

    public ProviderInfo GetInfo() => new()
    {
        Key = "linode",
        Name = "Linode (Akamai)",
        Description = "Akamai Connected Cloud (formerly Linode). Affordable VPS hosting with global regions.",
        SupportedContainerKinds = ["Host", "Caddy", "ComputePool", "Dns"]
    };

    public List<Region> GetRegions() =>
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

    public List<ComputePlan> GetPlans() =>
    [
        new() { Id = "g6-nanode-1", Label = "Nanode 1GB", VCpus = 1, MemoryMb = 1024, DiskGb = 25, PriceMonthly = 5m },
        new() { Id = "g6-standard-1", Label = "Linode 2GB", VCpus = 1, MemoryMb = 2048, DiskGb = 50, PriceMonthly = 12m },
        new() { Id = "g6-standard-2", Label = "Linode 4GB", VCpus = 2, MemoryMb = 4096, DiskGb = 80, PriceMonthly = 24m },
        new() { Id = "g6-standard-4", Label = "Linode 8GB", VCpus = 4, MemoryMb = 8192, DiskGb = 160, PriceMonthly = 48m },
        new() { Id = "g6-standard-6", Label = "Linode 16GB", VCpus = 6, MemoryMb = 16384, DiskGb = 320, PriceMonthly = 96m },
        new() { Id = "g6-standard-8", Label = "Linode 32GB", VCpus = 8, MemoryMb = 32768, DiskGb = 640, PriceMonthly = 192m },
    ];

    public List<CredentialField> GetCredentialSchema() =>
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
                    "Copy the token — it's only shown once"
                ],
                Permissions = "Linodes: Read/Write, Domains: Read/Write, Firewalls: Read/Write, Volumes: Read/Write",
                Url = "https://cloud.linode.com/profile/tokens"
            },
            Validation = [new() { Type = "minLength", Value = "10", Message = "API token must be at least 10 characters" }]
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
                Summary = "SSH public key for instance access",
                Steps =
                [
                    "Generate a key pair: ssh-keygen -t ed25519",
                    "Copy the public key: cat ~/.ssh/id_ed25519.pub",
                    "Paste the full public key here",
                    "The key will be added to all provisioned instances"
                ],
                Url = "https://techdocs.akamai.com/cloud-computing/docs/use-public-key-authentication-with-ssh"
            },
            Validation = [new() { Type = "pattern", Value = @"^ssh-(rsa|ed25519|ecdsa)\s+[A-Za-z0-9+/=]+", Message = "Must be a valid SSH public key (ssh-rsa, ssh-ed25519, or ssh-ecdsa)" }]
        }
    ];

    public Dictionary<string, string> GenerateHcl(
        Topology topology, List<TopologyHelpers.PoolSelection>? poolSelections = null)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var dnsContainers = TopologyHelpers.CollectDnsContainers(topology.Containers);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, topology);
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies);
        files["firewall.tf"] = GenerateFirewall(topology, hosts, standaloneCaddies);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes.tf"] = GenerateVolumes(hosts);
        files["outputs.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        if (dnsContainers.Count > 0)
            files["dns.tf"] = GenerateDnsRecords(dnsContainers, hosts, resolver, topology);

        return files;
    }

    public Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(ownedContainers.ToList());
        var pools = TopologyHelpers.CollectComputePools(ownedContainers.ToList(), topology, poolSelections);
        var dnsContainers = ownedContainers.Where(c => c.Kind == ContainerKind.Dns).ToList();
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddies(ownedContainers.ToList());
        var resolver = new WireResolver(topology);

        files["main_linode.tf"] = GenerateMain();
        files["variables_linode.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, topology);
        files["instances_linode.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies);
        files["firewall_linode.tf"] = GenerateFirewall(topology, hosts, standaloneCaddies);
        files["provisioning_linode.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes_linode.tf"] = GenerateVolumes(hosts);
        files["outputs_linode.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers);
        if (dnsContainers.Count > 0)
            files["dns_linode.tf"] = GenerateDnsRecords(dnsContainers, allHosts, resolver, topology);

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

    /// <summary>
    /// Resolves the compute plan for a pool: uses SelectedPlanId if set, otherwise auto-selects cheapest viable.
    /// </summary>
    private static ComputePlan ResolvePoolPlan(TopologyHelpers.ComputePoolEntry pool, List<ComputePlan> plans)
    {
        if (pool.SelectedPlanId is not null)
        {
            var selected = plans.FirstOrDefault(p => p.Id == pool.SelectedPlanId);
            if (selected is not null) return selected;
        }

        var fedMemory = pool.TierProfile.ImageSpecs.GetValueOrDefault("FederationServer")?.MemoryMb ?? 256;
        var sharedOverhead = ImageOperationalMetadata.CalculateSharedOverheadMb();
        var minHostRam = sharedOverhead + fedMemory;
        return plans.FirstOrDefault(p => p.MemoryMb >= minHostRam) ?? plans.Last();
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

            dns.Block($"data \"linode_domain\" \"{sanitizedDomain}\"", b =>
            {
                b.RawAttribute("domain", "var.domain");
            });
            dns.Line();

            var wiredHosts = TopologyHelpers.CollectHostsWiredToDns(dnsContainer, resolver, allHosts);
            foreach (var entry in wiredHosts)
            {
                var hostName = TopologyHelpers.SanitizeName(entry.Host.Name);
                var providerKey = TopologyHelpers.ResolveProviderKey(entry.Host, topology);
                var ipRef = GetIpReference(hostName, providerKey, TopologyHelpers.IsReplicatedHost(entry));

                dns.Block($"resource \"linode_domain_record\" \"{hostName}\"", b =>
                {
                    b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                    b.Attribute("name", hostName);
                    b.Attribute("record_type", "A");
                    b.RawAttribute("target", ipRef);
                    b.Attribute("ttl_sec", 300);
                });
                dns.Line();
            }
        }

        return dns.ToString();
    }

    /// <summary>
    /// Get the Terraform IP reference for a host based on which provider owns it.
    /// </summary>
    internal static string GetIpReference(string hostName, string providerKey, bool isReplicated)
    {
        if (string.Equals(providerKey, "aws", StringComparison.OrdinalIgnoreCase))
            return isReplicated ? $"aws_instance.{hostName}[0].public_ip" : $"aws_instance.{hostName}.public_ip";

        return isReplicated ? $"linode_instance.{hostName}[0].ip_address" : $"linode_instance.{hostName}.ip_address";
    }

    // --- File generators ---

    private static string GenerateMain()
    {
        var main = new HclBuilder();
        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                p.Block("linode", lp =>
                {
                    lp.Attribute("source", "linode/linode");
                    lp.Attribute("version", "~> 2.0");
                });
                p.Block("random", rp =>
                {
                    rp.Attribute("source", "hashicorp/random");
                    rp.Attribute("version", "~> 3.0");
                });
            });
        });
        main.Line();
        main.Block("provider \"linode\"", b =>
        {
            b.RawAttribute("token", "var.linode_token");
        });
        return main.ToString();
    }

    private static string GenerateSecrets(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver, Topology topology)
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

    private string GenerateVariables(Topology topology, List<TopologyHelpers.ComputePoolEntry> pools)
    {
        var vars = new HclBuilder();
        vars.Block("variable \"linode_token\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("description", "Linode API token");
            b.RawAttribute("sensitive", "true");
        });
        vars.Line();
        vars.Block("variable \"region\"", b =>
        {
            b.Attribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("region", "us-east"));
            b.Attribute("description", "Linode region");
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

        // Host replica $VAR variables
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        CollectHostReplicaVariables(hosts, vars);

        // ComputePool variables
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var selectedPlan = ResolvePoolPlan(pool, plans);
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

    private static void CollectHostReplicaVariables(List<TopologyHelpers.HostEntry> hosts, HclBuilder vars)
    {
        foreach (var entry in hosts)
        {
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

    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies)
    {
        var instances = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host);
            var plan = SelectPlan(ramRequired);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.Attribute("label", TopologyHelpers.IsReplicatedHost(entry)
                    ? $"{topology.Name}-{entry.Host.Name}-${{count.index}}"
                    : $"{topology.Name}-{entry.Host.Name}");
                b.RawAttribute("region", "var.region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[var.ssh_public_key]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, entry.Host.Kind.ToString().ToLowerInvariant()]);
            });
            instances.Line();
        }

        // ComputePool instances
        var allPlans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = TopologyHelpers.SanitizeName(pool.Pool.Name);
            var selectedPlan = ResolvePoolPlan(pool, allPlans);

            instances.Block($"resource \"linode_instance\" \"{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.Attribute("label", $"{topology.Name}-{pool.Pool.Name}-${{count.index}}");
                b.RawAttribute("region", "var.region");
                b.Attribute("type", selectedPlan.Id);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[var.ssh_public_key]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "compute-pool"]);
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var plan = SelectPlan(ImageOperationalMetadata.Caddy.MinRamMb);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.Attribute("label", $"{topology.Name}-{caddy.Name}");
                b.RawAttribute("region", "var.region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[var.ssh_public_key]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "caddy"]);
            });
            instances.Line();
        }

        return instances.ToString();
    }

    private static string GenerateFirewall(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<Container> standaloneCaddies)
    {
        var firewall = new HclBuilder();

        var hasLiveKit = hosts.Any(e =>
            TopologyHelpers.CollectImages(e.Host).Any(i => i.Kind == ImageKind.LiveKit));

        firewall.Block("resource \"linode_firewall\" \"main\"", b =>
        {
            b.Attribute("label", $"{topology.Name}-firewall");

            b.Block("inbound", ib =>
            {
                ib.Attribute("label", "allow-ssh");
                ib.Attribute("action", "ACCEPT");
                ib.Attribute("protocol", "TCP");
                ib.Attribute("ports", "22");
                ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
            });

            b.Block("inbound", ib =>
            {
                ib.Attribute("label", "allow-http");
                ib.Attribute("action", "ACCEPT");
                ib.Attribute("protocol", "TCP");
                ib.Attribute("ports", "80,443");
                ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
            });

            if (hasLiveKit)
            {
                b.Block("inbound", ib =>
                {
                    ib.Attribute("label", "allow-livekit-tcp");
                    ib.Attribute("action", "ACCEPT");
                    ib.Attribute("protocol", "TCP");
                    ib.Attribute("ports", "7880-7882");
                    ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
                });

                b.Block("inbound", ib =>
                {
                    ib.Attribute("label", "allow-livekit-udp");
                    ib.Attribute("action", "ACCEPT");
                    ib.Attribute("protocol", "UDP");
                    ib.Attribute("ports", "7880-7882");
                    ib.ListAttribute("ipv4", ["0.0.0.0/0"]);
                });
            }

            b.Block("inbound_policy", ib =>
            {
                ib.Attribute("action", "DROP");
            });

            b.Block("outbound_policy", ob =>
            {
                ob.Attribute("action", "ACCEPT");
            });

            var refs = new List<string>();
            foreach (var entry in hosts)
            {
                var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
                if (TopologyHelpers.IsReplicatedHost(entry))
                    refs.Add($"linode_instance.{resourceName}[*].id");
                else
                    refs.Add($"[linode_instance.{resourceName}.id]");
            }

            foreach (var caddy in standaloneCaddies)
            {
                var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
                refs.Add($"[linode_instance.{resourceName}.id]");
            }

            if (refs.Any(r => r.Contains("[*]")))
            {
                var concatArgs = string.Join(", ", refs);
                b.RawAttribute("linodes", $"concat({concatArgs})");
            }
            else
            {
                var idRefs = hosts.Select(e => $"linode_instance.{TopologyHelpers.SanitizeName(e.Host.Name)}.id")
                    .Concat(standaloneCaddies.Select(c => $"linode_instance.{TopologyHelpers.SanitizeName(c.Name)}.id"));
                b.RawAttribute("linodes", $"[{string.Join(", ", idRefs)}]");
            }
        });
        return firewall.ToString();
    }

    private string GenerateProvisioning(List<TopologyHelpers.HostEntry> hosts, WireResolver resolver, Topology topology, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies)
    {
        var provisioning = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var hostName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var images = TopologyHelpers.CollectImages(entry.Host);
            var caddies = TopologyHelpers.CollectCaddyContainers(entry.Host);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);

            if (images.Count == 0 && caddies.Count == 0) continue;

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                var depsList = new List<string> { $"linode_instance.{resourceName}" };

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
                        ? $"linode_instance.{resourceName}[count.index].ip_address"
                        : $"linode_instance.{resourceName}.ip_address");
                    cb.Attribute("user", "root");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");

                    b.Line("  \"docker network create xcord-bridge\",");

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
                        b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                    }

                    foreach (var caddy in caddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);

                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"").Replace("$", "\\$");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");

                        b.Line($"  \"docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    }
                    var backupCommands = TopologyHelpers.GenerateBackupCommands(images, entry.Host);
                    foreach (var cmd in backupCommands)
                        b.Line($"  \"{cmd}\",");

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
                b.RawAttribute("depends_on", $"[linode_instance.{poolName}]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{poolName}[count.index].ip_address");
                    cb.Attribute("user", "root");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge\",");

                    b.Line("  \"docker run -d --name shared-postgres --network xcord-bridge --restart unless-stopped --memory 1024m -v pgdata:/var/lib/postgresql/data -e POSTGRES_PASSWORD=changeme -e POSTGRES_USER=postgres postgres:17-alpine\",");
                    b.Line("  \"docker run -d --name shared-redis --network xcord-bridge --restart unless-stopped --memory 512m -v redisdata:/data redis:7-alpine redis-server --requirepass changeme\",");
                    b.Line("  \"docker run -d --name shared-minio --network xcord-bridge --restart unless-stopped --memory 512m -v miniodata:/data -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin minio/minio:latest server /data --console-address :9001\",");
                    b.Line($"  \"docker run -d --name caddy --network xcord-bridge --restart unless-stopped --memory 128m -p 80:80 -p 443:443 -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                    b.Line($"  \"for i in $(seq 0 $((var.{poolName}_tenants_per_host - 1))); do docker run -d --name tenant-$i --network xcord-bridge --restart unless-stopped --memory {fedMemory}m --cpus {cpuLimit:F1} ghcr.io/xcord/fed:latest; done\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Standalone Caddy provisioning
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver);

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                b.RawAttribute("depends_on", $"[linode_instance.{resourceName}]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{resourceName}.ip_address");
                    cb.Attribute("user", "root");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge\",");
                    b.Line($"  \"mkdir -p /opt/caddy\",");
                    var escapedCaddyfile = caddyfile.Replace("\"", "\\\"").Replace("$", "\\$");
                    var caddyfileLines = escapedCaddyfile.Split('\n');
                    b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                    b.Line($"  \"docker run -d --name {resourceName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        return provisioning.ToString();
    }

    private static string GenerateVolumes(List<TopologyHelpers.HostEntry> hosts)
    {
        var volumes = new HclBuilder();
        foreach (var entry in hosts)
        {
            var images = TopologyHelpers.CollectImages(entry.Host);
            foreach (var image in images)
            {
                var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
                if (string.IsNullOrEmpty(volumeSize)) continue;

                var size = int.TryParse(volumeSize, out var s) ? s : 25;
                var resourceName = $"{TopologyHelpers.SanitizeName(entry.Host.Name)}_{TopologyHelpers.SanitizeName(image.Name)}";

                var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
                volumes.Block($"resource \"linode_volume\" \"{resourceName}_vol\"", b =>
                {
                    if (isReplicated)
                    {
                        var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                        b.RawAttribute("count", countExpr!);
                        b.Attribute("label", $"{image.Name}-vol-${{count.index}}");
                    }
                    else
                    {
                        b.Attribute("label", $"{image.Name}-vol");
                    }

                    b.RawAttribute("region", "var.region");
                    b.Attribute("size", size);
                    b.RawAttribute("linode_id", isReplicated
                        ? $"linode_instance.{TopologyHelpers.SanitizeName(entry.Host.Name)}[count.index].id"
                        : $"linode_instance.{TopologyHelpers.SanitizeName(entry.Host.Name)}.id");
                });
                volumes.Line();
            }
        }
        return volumes.ToString();
    }

    private static string GenerateOutputs(List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies)
    {
        var outputs = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            if (TopologyHelpers.IsReplicatedHost(entry))
            {
                outputs.Block($"output \"{resourceName}_ips\"", b =>
                {
                    b.RawAttribute("value", $"linode_instance.{resourceName}[*].ip_address");
                    b.Attribute("description", $"Public IPs of {entry.Host.Name} instances");
                });
                outputs.Line();
                outputs.Block($"output \"{resourceName}_private_ips\"", b =>
                {
                    b.RawAttribute("value", $"linode_instance.{resourceName}[*].private_ip_address");
                    b.Attribute("description", $"Private IPs of {entry.Host.Name} instances");
                });
            }
            else
            {
                outputs.Block($"output \"{resourceName}_ip\"", b =>
                {
                    b.RawAttribute("value", $"linode_instance.{resourceName}.ip_address");
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
                b.RawAttribute("value", $"linode_instance.{poolName}[*].ip_address");
                b.Attribute("description", $"Public IPs of compute pool '{pool.Pool.Name}'");
            });
            outputs.Line();
        }

        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            outputs.Block($"output \"{resourceName}_ip\"", b =>
            {
                b.RawAttribute("value", $"linode_instance.{resourceName}.ip_address");
                b.Attribute("description", $"Public IP of {caddy.Name}");
            });
            outputs.Line();
        }

        return outputs.ToString();
    }
}
