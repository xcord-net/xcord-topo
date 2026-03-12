using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class LinodeProvider : ProviderHclBase
{
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

    public override Dictionary<string, string> GenerateHcl(
        Topology topology, List<TopologyHelpers.PoolSelection>? poolSelections = null)
    {
        var files = new Dictionary<string, string>();
        var hosts = TopologyHelpers.CollectHosts(topology.Containers);
        var pools = TopologyHelpers.CollectComputePools(topology.Containers, topology, poolSelections);
        var dnsContainers = TopologyHelpers.CollectDnsContainers(topology.Containers);
        var standaloneCaddies = TopologyHelpers.CollectStandaloneCaddiesRecursive(topology.Containers);
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, pools, standaloneCaddies);
        files["variables.tf"] = GenerateVariables(topology, pools);
        files["instances.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies);
        files["firewall.tf"] = GenerateFirewall(topology, hosts, pools, standaloneCaddies);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes.tf"] = GenerateVolumes(hosts);
        files["outputs.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        if (dnsContainers.Count > 0)
            files["dns.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

        return files;
    }

    public override Dictionary<string, string> GenerateHclForContainers(
        Topology topology,
        IReadOnlyList<Container> ownedContainers,
        List<TopologyHelpers.PoolSelection>? poolSelections = null)
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

        files["main_linode.tf"] = GenerateMain();
        files["variables_linode.tf"] = GenerateVariables(topology, pools);
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, pools, standaloneCaddies);
        files["instances_linode.tf"] = GenerateInstances(topology, hosts, pools, standaloneCaddies);
        if (hosts.Count > 0 || pools.Count > 0 || standaloneCaddies.Count > 0)
            files["firewall_linode.tf"] = GenerateFirewall(topology, hosts, pools, standaloneCaddies);
        files["provisioning_linode.tf"] = GenerateProvisioning(hosts, resolver, topology, pools, standaloneCaddies);
        files["volumes_linode.tf"] = GenerateVolumes(hosts);
        files["outputs_linode.tf"] = GenerateOutputs(hosts, pools, standaloneCaddies);

        var allHosts = TopologyHelpers.CollectHosts(topology.Containers);
        if (dnsContainers.Count > 0)
            files["dns_linode.tf"] = GenerateDnsRecords(dnsContainers, resolver, topology);

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

            dns.Block($"data \"linode_domain\" \"{sanitizedDomain}\"", b =>
            {
                b.RawAttribute("domain", "var.domain");
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

                dns.Block($"resource \"linode_domain_record\" \"{containerName}\"", b =>
                {
                    b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                    b.Attribute("name", containerName);
                    b.Attribute("record_type", "A");
                    b.RawAttribute("target", ipRef);
                    b.Attribute("ttl_sec", 300);
                });
                dns.Line();

                // Caddy containers handle subdomain routing — create wildcard + bare domain records
                if (container.Kind == ContainerKind.Caddy && !wildcardCreated)
                {
                    dns.Block($"resource \"linode_domain_record\" \"wildcard\"", b =>
                    {
                        b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                        b.Attribute("name", "*");
                        b.Attribute("record_type", "A");
                        b.RawAttribute("target", ipRef);
                        b.Attribute("ttl_sec", 300);
                    });
                    dns.Line();

                    // Bare domain (apex) record when Caddy domain matches DNS zone
                    var caddyDomain = container.Config.GetValueOrDefault("domain", "");
                    if (!string.IsNullOrEmpty(caddyDomain) && caddyDomain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    {
                        dns.Block($"resource \"linode_domain_record\" \"apex\"", b =>
                        {
                            b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                            b.Attribute("name", "");
                            b.Attribute("record_type", "A");
                            b.RawAttribute("target", ipRef);
                            b.Attribute("ttl_sec", 300);
                        });
                        dns.Line();
                    }

                    wildcardCreated = true;
                }

                // Registry images co-located with this container need a DNS record for their domain
                var registryImages = TopologyHelpers.CollectRegistryImagesRecursive(container);
                foreach (var reg in registryImages)
                {
                    var regDomain = reg.Config.GetValueOrDefault("domain", "");
                    if (string.IsNullOrEmpty(regDomain))
                        regDomain = topology.Registry ?? "";
                    if (string.IsNullOrEmpty(regDomain)) continue;

                    // Extract subdomain from registry domain (e.g., "docker" from "docker.xcord.net")
                    var regSubdomain = regDomain.Split('.')[0];
                    var regResourceName = TopologyHelpers.SanitizeName(regDomain);

                    dns.Block($"resource \"linode_domain_record\" \"{regResourceName}\"", b =>
                    {
                        b.RawAttribute("domain_id", $"data.linode_domain.{sanitizedDomain}.id");
                        b.Attribute("name", regSubdomain);
                        b.Attribute("record_type", "A");
                        b.RawAttribute("target", ipRef);
                        b.Attribute("ttl_sec", 300);
                    });
                    dns.Line();
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
        vars.Block("variable \"region\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("default", topology.ProviderConfig.GetValueOrDefault("region", "us-east"));
            b.Attribute("description", "Linode region");
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
            b.Attribute("default", "latest");
            b.Attribute("description", "Version tag for xcord application images (hub, fed)");
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

        // ComputePool instances — one resource block per tier
        var allPlans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            var selectedPlan = ResolvePoolPlan(pool, allPlans);

            instances.Block($"resource \"linode_instance\" \"{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.Attribute("label", $"{topology.Name}-{pool.TierProfile.Name}-${{count.index}}");
                b.RawAttribute("region", "var.region");
                b.Attribute("type", selectedPlan.Id);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[var.ssh_public_key]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, pool.TierProfile.Id]);
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
            var plan = SelectPlan(ramRequired);
            var varName = $"{resourceName}_replicas";

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("count", $"var.{varName}");
                b.Attribute("label", $"{topology.Name}-{image.Name}-${{count.index}}");
                b.RawAttribute("region", "var.region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[var.ssh_public_key]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "elastic"]);
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var plan = SelectPlan(TopologyHelpers.CalculateStandaloneCaddyRam(caddy));

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

    private static string GenerateFirewall(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies)
    {
        var firewall = new HclBuilder();

        // Check entire topology for LiveKit — it may be on hosts, standalone Caddies, or pools
        var hasLiveKit = topology.Containers.Any(HasLiveKitRecursive);

        static bool HasLiveKitRecursive(Container c)
        {
            if (c.Images.Any(i => i.Kind == ImageKind.LiveKit)) return true;
            return c.Children.Any(HasLiveKitRecursive);
        }

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

            foreach (var pool in pools)
            {
                refs.Add($"linode_instance.{pool.ResourceName}[*].id");
            }

            // Elastic images get their own instances — include them in the firewall
            var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
            foreach (var image in elasticImages)
            {
                var resourceName = TopologyHelpers.SanitizeName(image.Name);
                refs.Add($"linode_instance.{resourceName}[*].id");
            }

            if (refs.Any(r => r.Contains("[*]")))
            {
                var concatArgs = string.Join(", ", refs);
                b.RawAttribute("linodes", $"concat({concatArgs})");
            }
            else
            {
                var idRefs = hosts.Select(e => $"linode_instance.{TopologyHelpers.SanitizeName(e.Host.Name)}.id")
                    .Concat(standaloneCaddies.Select(c => $"linode_instance.{TopologyHelpers.SanitizeName(c.Name)}.id"))
                    .Concat(pools.Select(p => $"linode_instance.{p.ResourceName}.id"))
                    .Concat(elasticImages.Select(i => $"linode_instance.{TopologyHelpers.SanitizeName(i.Name)}[*].id"));
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
            var images = TopologyHelpers.CollectImages(entry.Host);
            var caddies = TopologyHelpers.CollectCaddyContainers(entry.Host);
            var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
            var useSwarm = TopologyHelpers.HostNeedsSwarmMode(entry.Host);

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
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");

                    if (useSwarm)
                    {
                        // Swarm mode — enables replicated services with built-in DNS load balancing
                        b.Line("  \"docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                        b.Line("  \"docker network create --driver overlay --attachable xcord-bridge\",");
                    }
                    else
                    {
                        b.Line("  \"docker network create xcord-bridge\",");
                    }

                    // Docker login for private registry images
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    foreach (var image in images)
                    {
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
                                    flags.Add($"-p {port}:{port}");
                            }

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"docker service create {flagStr} {dockerImage}{cmd}\",");
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
                            b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                        }
                    }

                    foreach (var caddy in caddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);

                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");

                        if (useSwarm)
                            b.Line($"  \"docker service create --name {caddyName} --replicas 1 --network xcord-bridge --restart-condition any -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        else
                            b.Line($"  \"docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        // Always-on rate limiting for Caddy hosts
                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"{rlCmd}\",");
                    }
                    // Registry images: configure htpasswd auth
                    foreach (var image in images.Where(i => i.Kind == ImageKind.Registry))
                    {
                        var domain = image.Config.GetValueOrDefault("domain", "");
                        if (string.IsNullOrEmpty(domain)) continue;

                        var registryName = TopologyHelpers.SanitizeName(image.Name);

                        // htpasswd auth when credentials are configured
                        b.Line($"  \"mkdir -p /opt/registry/auth\",");
                        b.Line($"  \"bash -c 'if [ -n \\\"${{var.registry_username}}\\\" ]; then apt-get install -y -qq apache2-utils && htpasswd -Bbn \\\"${{var.registry_username}}\\\" \\\"${{var.registry_password}}\\\" > /opt/registry/auth/htpasswd; fi'\",");

                        // Restart registry container with auth if htpasswd was created
                        b.Line($"  \"bash -c 'if [ -f /opt/registry/auth/htpasswd ]; then docker stop {registryName} 2>/dev/null; docker rm {registryName} 2>/dev/null; docker run -d --name {registryName} --network xcord-bridge --restart unless-stopped -v {registryName}_data:/var/lib/registry -v /opt/registry/auth:/auth -e REGISTRY_AUTH=htpasswd -e REGISTRY_AUTH_HTPASSWD_REALM=Registry -e REGISTRY_AUTH_HTPASSWD_PATH=/auth/htpasswd -p 5000:5000 registry:2; fi'\",");

                        // Only deploy a standalone Caddy sidecar for TLS if this host doesn't already have a Caddy container
                        if (caddies.Count == 0)
                        {
                            var registryCaddyfile = $"{domain} {{\\n  reverse_proxy {registryName}:5000\\n}}";
                            b.Line($"  \"mkdir -p /opt/caddy\",");
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{registryCaddyfile}\\nCADDYEOF\",");
                            b.Line($"  \"docker run -d --name caddy_registry --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
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

            // Build depends_on from actual pool secrets (shared across tiers)
            var poolSecrets = TopologyHelpers.CollectPoolSecrets(pool);
            var secretDeps = string.Join(", ", poolSecrets.Select(s => $"random_password.{s.ResourceName}"));
            var dependsOn = string.IsNullOrEmpty(secretDeps)
                ? $"[linode_instance.{poolName}]"
                : $"[linode_instance.{poolName}, {secretDeps}]";

            // Manager provisioning (host 0) — init Swarm + deploy shared services
            provisioning.Block($"resource \"null_resource\" \"provision_{poolName}_manager\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count > 0 ? 1 : 0");
                b.RawAttribute("depends_on", dependsOn);
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{poolName}[0].ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                    b.Line("  \"docker network create --driver overlay --attachable xcord-pool\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");
                    b.Line("  \"docker swarm join-token -q worker > /var/swarm-worker-token\",");
                    b.Line("  \"cd /var && nohup python3 -m http.server 9999 &\",");

                    // Deploy shared services from actual pool images (data-driven)
                    foreach (var image in pool.Pool.Images)
                    {
                        var cmd = TopologyHelpers.GenerateSwarmServiceCommand(image, poolName, resolver, useSudo: false, pool.Pool.Images);
                        if (cmd != null)
                            b.Line($"  \"{cmd}\",");
                    }

                    // Deploy Caddy containers with generated Caddyfiles
                    var poolCaddies = TopologyHelpers.CollectCaddyContainers(pool.Pool);
                    foreach (var caddy in poolCaddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);
                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                        b.Line($"  \"docker service create --name {caddyName} --mode global --network xcord-pool -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"{rlCmd}\",");
                    }
                    if (poolCaddies.Count == 0)
                    {
                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n\\nCADDYEOF\",");
                        b.Line($"  \"docker service create --name caddy --mode global --network xcord-pool -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
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
                    cb.RawAttribute("host", $"linode_instance.{poolName}[count.index + 1].ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");
                    b.Line($"  \"TOKEN=$(curl -sf --retry 10 --retry-delay 3 http://${{linode_instance.{poolName}[0].private_ip_address}}:9999/swarm-worker-token) && docker swarm join --token $TOKEN ${{linode_instance.{poolName}[0].private_ip_address}}:2377\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Pre-compute host port assignments for all standalone Caddies
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
            var depsList = new List<string> { $"linode_instance.{resourceName}" };
            foreach (var secret in caddySecrets)
                depsList.Add($"random_password.{secret.ResourceName}");

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                b.RawAttribute("depends_on", $"[{string.Join(", ", depsList)}]");
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{resourceName}.ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    // Deploy co-located non-elastic images on the Caddy host
                    var caddyEntry = new TopologyHelpers.HostEntry(caddy);
                    var coLocatedImages = TopologyHelpers.CollectImagesExcludingPools(caddy);
                    foreach (var image in coLocatedImages)
                    {
                        var (imgMin, imgMax) = TopologyHelpers.ParseReplicaRange(image.Config);
                        if (imgMin > 1 || imgMax > 1) continue; // Elastic — gets its own instance

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

                        if (meta?.MountPath != null)
                            flags.Add($"-v {containerName}_data:{meta.MountPath}");

                        var flagStr = string.Join(" ", flags);
                        b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                    }

                    b.Line($"  \"mkdir -p /opt/caddy\",");
                    var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                    var caddyfileLines = escapedCaddyfile.Split('\n');
                    b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                    b.Line($"  \"docker run -d --name {resourceName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                    // Always-on rate limiting for standalone Caddy
                    var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                    foreach (var rlCmd in rateLimitCmds)
                        b.Line($"  \"{rlCmd}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Elastic image provisioning — images with replicas > 1 get their own instances
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
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
                b.RawAttribute("depends_on", $"[linode_instance.{resourceName}]");
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{resourceName}[count.index].ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "var.ssh_private_key");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

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
                    b.Line($"  \"docker run {flagStr} {dockerImage}\",");

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
}
