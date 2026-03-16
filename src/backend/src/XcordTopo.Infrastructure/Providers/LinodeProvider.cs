using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class LinodeProvider : ProviderHclBase
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

                // Caddy containers handle subdomain routing - create wildcard + bare domain records
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

                // Registry images co-located with this container need a DNS record for their subdomain
                var registryImages = TopologyHelpers.CollectRegistryImagesRecursive(container);
                foreach (var reg in registryImages)
                {
                    var regSubdomain = TopologyHelpers.SanitizeName(reg.Name);
                    var regResourceName = $"{regSubdomain}_dns";

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

    // --- Cold storage generation ---

    private static string? GenerateColdStorage(Topology topology)
    {
        if (topology.BackupTarget is null || topology.BackupTarget.Kind != BackupTargetKind.LinodeObjectStorage)
            return null;

        var hcl = new HclBuilder();

        hcl.Block("resource \"linode_object_storage_bucket\" \"backups\"", b =>
        {
            b.RawAttribute("cluster", "\"${var.linode_region}-1\"");
            b.RawAttribute("label", "var.coldstore_bucket");
        });
        hcl.Line();

        hcl.Block("resource \"linode_object_storage_key\" \"backups\"", b =>
        {
            b.RawAttribute("label", "\"xcord-backups-${var.domain}\"");
            b.Line();
            b.Block("bucket_access", ba =>
            {
                ba.RawAttribute("cluster", "linode_object_storage_bucket.backups.cluster");
                ba.RawAttribute("bucket_name", "linode_object_storage_bucket.backups.label");
                ba.Attribute("permissions", "read_write");
            });
        });
        hcl.Line();

        hcl.Block("output \"coldstore_endpoint\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_bucket.backups.hostname");
            b.Attribute("description", "Cold storage endpoint for backups");
        });
        hcl.Line();

        hcl.Block("output \"coldstore_access_key\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_key.backups.access_key");
            b.Attribute("sensitive", true);
            b.Attribute("description", "Cold storage access key");
        });
        hcl.Line();

        hcl.Block("output \"coldstore_secret_key\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_key.backups.secret_key");
            b.Attribute("sensitive", true);
            b.Attribute("description", "Cold storage secret key");
        });

        return hcl.ToString();
    }

    // --- File generators ---

    private static string GenerateMain()
    {
        var main = new HclBuilder();
        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                p.MapBlock("linode", lp =>
                {
                    lp.Attribute("source", "linode/linode");
                    lp.Attribute("version", "~> 2.0");
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
        main.Block("provider \"linode\"", b =>
        {
            b.RawAttribute("token", "var.linode_token");
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
        vars.Block("variable \"hub_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Version tag for hub server image (e.g. v0.1.5)");
        });
        vars.Line();
        vars.Block("variable \"fed_version\"", b =>
        {
            b.RawAttribute("type", "string");
            b.Attribute("description", "Version tag for federation server image (e.g. v0.1.5)");
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

    private string GenerateInstances(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, List<TopologyHelpers.InfraSelection>? infraSelections = null)
    {
        var instances = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = TopologyHelpers.SanitizeName(entry.Host.Name);
            var ramRequired = TopologyHelpers.CalculateHostRam(entry.Host, _imageRegistry);
            var plan = SelectPlan(entry.Host.Name, ramRequired, infraSelections);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.Attribute("label", TopologyHelpers.IsReplicatedHost(entry)
                    ? $"{topology.Name}-{entry.Host.Name}-${{count.index}}"
                    : $"{topology.Name}-{entry.Host.Name}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, entry.Host.Kind.ToString().ToLowerInvariant()]);
            });
            instances.Line();
        }

        // ComputePool instances - one resource block per tier
        var allPlans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            var selectedPlan = ResolvePoolPlan(pool, allPlans);

            instances.Block($"resource \"linode_instance\" \"{poolName}\"", b =>
            {
                b.RawAttribute("count", $"var.{poolName}_host_count");
                b.Attribute("label", $"{topology.Name}-{pool.TierProfile.Name}-${{count.index}}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", selectedPlan.Id);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
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
            var desc = _imageRegistry.GetDescriptor(image);
            var ramRequired = desc?.MinRamMb ?? 256;
            var plan = SelectPlan(image.Name, ramRequired, infraSelections);
            var varName = $"{resourceName}_replicas";

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.RawAttribute("count", $"var.{varName}");
                b.Attribute("label", $"{topology.Name}-{image.Name}-${{count.index}}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "elastic"]);
            });
            instances.Line();
        }

        // Standalone Caddy instances
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var plan = SelectPlan(caddy.Name, TopologyHelpers.CalculateStandaloneCaddyRam(caddy, _imageRegistry), infraSelections);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                b.Attribute("label", $"{topology.Name}-{caddy.Name}");
                b.RawAttribute("region", "var.linode_region");
                b.Attribute("type", plan);
                b.Attribute("image", "linode/ubuntu24.04");
                b.RawAttribute("authorized_keys", "[chomp(tls_private_key.deploy.public_key_openssh)]");
                b.Line();
                b.ListAttribute("tags", ["xcord-topo", topology.Name, "caddy"]);
            });
            instances.Line();
        }

        return instances.ToString();
    }

    private static string GenerateFirewall(Topology topology, List<TopologyHelpers.HostEntry> hosts, List<TopologyHelpers.ComputePoolEntry> pools, List<Container> standaloneCaddies, ImagePluginRegistry imageRegistry)
    {
        var firewall = new HclBuilder();

        // Check entire topology for LiveKit - it may be on hosts, standalone Caddies, or pools
        var hasLiveKit = topology.Containers.Any(HasLiveKitRecursive);

        bool HasLiveKitRecursive(Container c)
        {
            if (c.Images.Any(i => imageRegistry.GetDescriptor(i)?.IsPublicEndpoint == true && imageRegistry.GetPorts(i).Any(p => p == 7880))) return true;
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

            // Elastic images get their own instances - include them in the firewall
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

                var secrets = TopologyHelpers.CollectSecrets(entry, resolver, _imageRegistry);
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
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
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
                        // Swarm mode - enables replicated services with built-in DNS load balancing
                        b.Line("  \"docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                        b.Line("  \"docker network create --driver overlay --attachable xcord-bridge 2>/dev/null || true\",");
                    }
                    else
                    {
                        b.Line("  \"docker network create xcord-bridge 2>/dev/null || true\",");
                    }

                    // Docker login for private registry images
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    foreach (var image in images)
                    {
                        // Private registry images (Hub, Fed) are deployed post-push via deploy_apps phase
                        if (TopologyHelpers.RequiresPrivateRegistry(image.ResolveTypeId(), _imageRegistry))
                            continue;

                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var desc = _imageRegistry.GetDescriptor(image);
                        var envVars = TopologyHelpers.BuildEnvVars(image, entry, resolver, _imageRegistry, _templateEngine, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, entry, resolver, _imageRegistry, _templateEngine);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        // Publish ports if the image has cross-host consumers, needs direct external access,
                        // or lives on a DataPool (DataPool images are always accessed from other hosts)
                        var publishPorts = desc?.Ports.Length > 0 &&
                            ((desc?.IsPublicEndpoint ?? false) ||
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
                                flags.Add($"-e '{key}={value}'");

                            if (desc?.MountPath != null)
                                flags.Add($"--mount type=volume,source={containerName}_data,target={desc.MountPath}");

                            if (publishPorts && desc != null)
                            {
                                foreach (var port in desc.Ports.Select(p => p.Port))
                                    flags.Add($"-p {port}:{port}");
                            }

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"docker service rm {containerName} 2>/dev/null || true\",");
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
                                flags.Add($"-e '{key}={value}'");

                            if (desc?.MountPath != null)
                                flags.Add($"-v {containerName}_data:{desc.MountPath}");

                            if (publishPorts && desc != null)
                            {
                                foreach (var port in desc.Ports.Select(p => p.Port))
                                    flags.Add($"-p {port}:{port}");
                            }

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");
                            b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                        }
                    }

                    foreach (var caddy in caddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, _imageRegistry, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);

                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");

                        if (useSwarm)
                        {
                            b.Line($"  \"docker service rm {caddyName} 2>/dev/null || true\",");
                            b.Line($"  \"docker service create --name {caddyName} --replicas 1 --network xcord-bridge --restart-condition any -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }
                        else
                        {
                            b.Line($"  \"docker rm -f {caddyName} 2>/dev/null || true\",");
                            b.Line($"  \"docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }

                        // Always-on rate limiting for Caddy hosts
                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"{rlCmd}\",");
                    }
                    // Registry images: configure htpasswd auth
                    foreach (var image in images.Where(i => i.ResolveTypeId() == "Registry"))
                    {
                        var registryName = TopologyHelpers.SanitizeName(image.Name);
                        var registrySubdomain = registryName;

                        // htpasswd auth when credentials are configured
                        b.Line($"  \"mkdir -p /opt/registry/auth\",");
                        b.Line($"  \"bash -c 'if [ -n \\\"${{var.registry_username}}\\\" ]; then apt-get install -y -qq apache2-utils && htpasswd -Bbn \\\"${{var.registry_username}}\\\" \\\"${{nonsensitive(var.registry_password)}}\\\" > /opt/registry/auth/htpasswd; fi'\",");

                        // Restart registry container with auth if htpasswd was created
                        b.Line($"  \"bash -c 'if [ -f /opt/registry/auth/htpasswd ]; then docker stop {registryName} 2>/dev/null; docker rm {registryName} 2>/dev/null; docker run -d --name {registryName} --network xcord-bridge --restart unless-stopped -v {registryName}_data:/var/lib/registry -v /opt/registry/auth:/auth -e REGISTRY_AUTH=htpasswd -e REGISTRY_AUTH_HTPASSWD_REALM=Registry -e REGISTRY_AUTH_HTPASSWD_PATH=/auth/htpasswd -p 5000:5000 registry:2; fi'\",");

                        // Only deploy a standalone Caddy sidecar for TLS if this host doesn't already have a Caddy container
                        if (caddies.Count == 0)
                        {
                            var registryCaddyfile = $"{registrySubdomain}.${{var.domain}} {{\\n  reverse_proxy {registryName}:5000\\n}}";
                            b.Line($"  \"mkdir -p /opt/caddy\",");
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{registryCaddyfile}\\nCADDYEOF\",");
                            b.Line($"  \"docker rm -f caddy_registry 2>/dev/null || true\",");
                            b.Line($"  \"docker run -d --name caddy_registry --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }
                    }

                    if (topology.BackupTarget != null)
                    {
                        foreach (var cmd in TopologyHelpers.GenerateColdStoreEnvSetup())
                            b.Line($"  \"{cmd}\",");
                    }

                    var backupCommands = TopologyHelpers.GenerateBackupCommands(images, entry.Host, _imageRegistry, _templateEngine, topology.BackupTarget);
                    foreach (var cmd in backupCommands)
                        b.Line($"  \"{cmd}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // ComputePool provisioning - one Swarm cluster per tier
        foreach (var pool in pools)
        {
            var poolName = pool.ResourceName;
            // Secrets are shared across all tiers in the same pool - use pool-level name prefix
            var poolSecretPrefix = TopologyHelpers.SanitizeName(pool.Pool.Name);

            // Build depends_on from actual pool secrets (shared across tiers)
            var poolSecrets = TopologyHelpers.CollectPoolSecrets(pool);
            var secretDeps = string.Join(", ", poolSecrets.Select(s => $"random_password.{s.ResourceName}"));
            var dependsOn = string.IsNullOrEmpty(secretDeps)
                ? $"[linode_instance.{poolName}]"
                : $"[linode_instance.{poolName}, {secretDeps}]";

            // Manager provisioning (host 0) - init Swarm + deploy shared services
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
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker swarm init --advertise-addr $(hostname -I | awk '{print $1}')\",");
                    b.Line("  \"docker network create --driver overlay --attachable xcord-pool 2>/dev/null || true\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");
                    b.Line("  \"docker swarm join-token -q worker > /var/swarm-worker-token\",");
                    b.Line("  \"cd /var && nohup python3 -m http.server 9999 &\",");

                    // Deploy shared services from actual pool images (data-driven)
                    foreach (var image in pool.Pool.Images)
                    {
                        var cmd = TopologyHelpers.GenerateSwarmServiceCommand(image, poolSecretPrefix, resolver, _imageRegistry, _templateEngine, useSudo: false, pool.Pool.Images);
                        if (cmd != null)
                            b.Line($"  \"{cmd}\",");
                    }

                    // Deploy Caddy containers with generated Caddyfiles
                    var poolCaddies = TopologyHelpers.CollectCaddyContainers(pool.Pool);
                    foreach (var caddy in poolCaddies)
                    {
                        var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, _imageRegistry, topology);
                        var caddyName = TopologyHelpers.SanitizeName(caddy.Name);
                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                        var caddyfileLines = escapedCaddyfile.Split('\n');
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                        b.Line($"  \"docker service rm {caddyName} 2>/dev/null || true\",");
                        b.Line($"  \"docker service create --name {caddyName} --mode global --network xcord-pool -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"{rlCmd}\",");
                    }
                    if (poolCaddies.Count == 0)
                    {
                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n\\nCADDYEOF\",");
                        b.Line($"  \"docker service rm caddy 2>/dev/null || true\",");
                        b.Line($"  \"docker service create --name caddy --mode global --network xcord-pool -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    }

                    pb.Line("]");
                });
            });
            provisioning.Line();

            // Worker provisioning (hosts 1+) - join Swarm
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
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
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
            var assignments = TopologyHelpers.ComputeHostPortAssignments(caddy, resolver, _imageRegistry);
            foreach (var (imgId, ports) in assignments)
                allPortAssignments[imgId] = ports;
        }

        // Standalone Caddy provisioning
        foreach (var caddy in standaloneCaddies)
        {
            var resourceName = TopologyHelpers.SanitizeName(caddy.Name);
            var caddyfile = TopologyHelpers.GenerateCaddyfile(caddy, resolver, _imageRegistry, topology);

            // Collect secret dependencies for co-located images
            var caddySecrets = TopologyHelpers.CollectSecrets(new TopologyHelpers.HostEntry(caddy), resolver, _imageRegistry, excludePools: true);
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
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge 2>/dev/null || true\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    // Deploy co-located non-elastic images on the Caddy host
                    var caddyEntry = new TopologyHelpers.HostEntry(caddy);
                    var coLocatedImages = TopologyHelpers.CollectImagesExcludingPools(caddy);
                    foreach (var image in coLocatedImages)
                    {
                        var (imgMin, imgMax) = TopologyHelpers.ParseReplicaRange(image.Config);
                        if (imgMin > 1 || imgMax > 1) continue; // Elastic - gets its own instance
                        if (TopologyHelpers.RequiresPrivateRegistry(image.ResolveTypeId(), _imageRegistry)) continue;

                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var desc = _imageRegistry.GetDescriptor(image);
                        var envVars = TopologyHelpers.BuildEnvVars(image, caddyEntry, resolver, _imageRegistry, _templateEngine, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, caddyEntry, resolver, _imageRegistry, _templateEngine);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        var flags = new List<string>
                        {
                            "-d",
                            $"--name {containerName}",
                            "--network xcord-bridge",
                            "--restart unless-stopped"
                        };

                        foreach (var (key, value) in envVars)
                            flags.Add($"-e '{key}={value}'");

                        // Use pre-computed port assignments to avoid host port conflicts
                        if (allPortAssignments.TryGetValue(image.Id, out var portMap))
                        {
                            foreach (var (containerPort, hostPort) in portMap)
                                flags.Add($"-p {hostPort}:{containerPort}");
                        }

                        if (desc?.MountPath != null)
                            flags.Add($"-v {containerName}_data:{desc.MountPath}");

                        var flagStr = string.Join(" ", flags);
                        b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");
                        b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                    }

                    b.Line($"  \"mkdir -p /opt/caddy\",");
                    var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                    var caddyfileLines = escapedCaddyfile.Split('\n');
                    b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                    b.Line($"  \"docker rm -f {resourceName} 2>/dev/null || true\",");
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

        // Elastic image provisioning - images with replicas > 1 get their own instances
        var elasticImages = TopologyHelpers.CollectElasticImages(hosts, standaloneCaddies);
        foreach (var image in elasticImages)
        {
            if (TopologyHelpers.RequiresPrivateRegistry(image.ResolveTypeId(), _imageRegistry)) continue;

            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
            var desc = _imageRegistry.GetDescriptor(image);

            // Find the parent container (Host or Caddy) for secret name resolution
            var parentContainer = resolver.FindHostFor(image.Id) ?? resolver.FindCaddyFor(image.Id);
            var parentEntry = parentContainer != null
                ? new TopologyHelpers.HostEntry(parentContainer)
                : new TopologyHelpers.HostEntry(new Container { Name = resourceName });
            // Elastic images run on their own instances - use a synthetic source host
            // so ResolveServiceHost knows this is NOT co-located with the parent
            var resolveFrom = new Container { Id = Guid.NewGuid(), Name = resourceName };
            var envVars = TopologyHelpers.BuildEnvVars(image, parentEntry, resolver, _imageRegistry, _templateEngine, topology, resolveFrom, allPortAssignments);

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
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge 2>/dev/null || true\",");
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    var flags = new List<string>
                    {
                        "-d",
                        $"--name {resourceName}",
                        "--network xcord-bridge",
                        "--restart unless-stopped"
                    };

                    foreach (var (key, value) in envVars)
                        flags.Add($"-e '{key}={value}'");

                    if (desc?.MountPath != null)
                        flags.Add($"-v {resourceName}_data:{desc.MountPath}");

                    if (desc?.Ports.Length > 0)
                    {
                        foreach (var port in desc.Ports.Select(p => p.Port))
                            flags.Add($"-p {port}:{port}");
                    }

                    var flagStr = string.Join(" ", flags);
                    b.Line($"  \"docker rm -f {resourceName} 2>/dev/null || true\",");
                    b.Line($"  \"docker run {flagStr} {dockerImage}\",");

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Application deployment - private registry images, gated by deploy_apps variable.
        // These run after image push via a second `terraform apply -var deploy_apps=true`.
        GenerateAppDeployResources(provisioning, hosts, standaloneCaddies, elasticImages, resolver, topology, allPortAssignments);

        return provisioning.ToString();
    }

    private void GenerateAppDeployResources(
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

            var allImages = TopologyHelpers.CollectImages(entry.Host);
            var hasProvisionResource = allImages.Any(i => !TopologyHelpers.RequiresPrivateRegistry(i.Kind));

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (isReplicated)
                    b.RawAttribute("count", $"var.deploy_apps ? {countExpr ?? "1"} : 0");
                else
                    b.RawAttribute("count", "var.deploy_apps ? 1 : 0");

                b.RawAttribute("depends_on", hasProvisionResource
                    ? $"[null_resource.provision_{resourceName}]"
                    : $"[linode_instance.{resourceName}]");
                b.Line();
                b.MapBlock("triggers", tb =>
                {
                    foreach (var img in privateImages)
                    {
                        var vv = TopologyHelpers.GetVersionVariableName(img.ResolveTypeId(), _imageRegistry);
                        tb.RawAttribute(vv, $"var.{vv}");
                    }
                });
                b.Line();

                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", isReplicated
                        ? $"linode_instance.{resourceName}[count.index].ip_address"
                        : $"linode_instance.{resourceName}.ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();

                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Install Docker if this host had no provision resource (idempotent)
                    if (!hasProvisionResource)
                    {
                        b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                        b.Line("  \"systemctl enable docker\",");
                        b.Line("  \"systemctl start docker\",");
                        b.Line("  \"docker network create xcord-bridge 2>/dev/null || true\",");
                    }

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    foreach (var image in privateImages)
                    {
                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var desc = _imageRegistry.GetDescriptor(image);
                        var envVars = TopologyHelpers.BuildEnvVars(image, entry, resolver, _imageRegistry, _templateEngine, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, entry, resolver, _imageRegistry, _templateEngine);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        var publishPorts = desc?.Ports.Length > 0 &&
                            (TopologyHelpers.HasCrossHostConsumers(image, entry.Host, resolver));

                        // Pull new image while old container still serves
                        b.Line($"  \"docker pull {dockerImage}\",");

                        if (useSwarm)
                        {
                            // Rolling update - zero downtime
                            b.Line($"  \"docker service update --image {dockerImage} {containerName} 2>/dev/null || docker service create --name {containerName} --replicas {TopologyHelpers.GetImageReplicaExpression(image)} --network xcord-bridge --restart-condition any {string.Join(" ", envVars.Select(e => $"-e '{e.Key}={e.Value}'"))}{(desc?.MountPath != null ? $" --mount type=volume,source={containerName}_data,target={desc.MountPath}" : "")}{(publishPorts && desc != null ? string.Concat(desc.Ports.Select(p => $" -p {p.Port}:{p.Port}")) : "")} {dockerImage}{cmd}\",");
                        }
                        else
                        {
                            var flags = new List<string> { "-d", $"--name {containerName}", "--network xcord-bridge", "--restart unless-stopped" };
                            foreach (var (key, value) in envVars) flags.Add($"-e '{key}={value}'");
                            if (desc?.MountPath != null) flags.Add($"-v {containerName}_data:{desc.MountPath}");
                            if (publishPorts && desc != null)
                                foreach (var port in desc.Ports.Select(p => p.Port)) flags.Add($"-p {port}:{port}");

                            // Pull-then-swap: remove old container and start new one (image already cached)
                            b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");
                            b.Line($"  \"docker run {string.Join(" ", flags)} {dockerImage}{cmd}\",");
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
                    return min <= 1 && max <= 1;
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
                b.MapBlock("triggers", tb =>
                {
                    foreach (var img in privateImages)
                    {
                        var vv = TopologyHelpers.GetVersionVariableName(img.ResolveTypeId(), _imageRegistry);
                        tb.RawAttribute(vv, $"var.{vv}");
                    }
                });
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{resourceName}.ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    foreach (var image in privateImages)
                    {
                        var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
                        var containerName = TopologyHelpers.SanitizeName(image.Name);
                        var desc = _imageRegistry.GetDescriptor(image);
                        var envVars = TopologyHelpers.BuildEnvVars(image, caddyEntry, resolver, _imageRegistry, _templateEngine, topology);
                        var cmdOverride = TopologyHelpers.ResolveCommandOverride(image, caddyEntry, resolver, _imageRegistry, _templateEngine);
                        var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                        // Pull new image while old container still serves
                        b.Line($"  \"docker pull {dockerImage}\",");
                        b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");

                        var flags = new List<string> { "-d", $"--name {containerName}", "--network xcord-bridge", "--restart unless-stopped" };
                        foreach (var (key, value) in envVars) flags.Add($"-e '{key}={value}'");
                        if (allPortAssignments.TryGetValue(image.Id, out var portMap))
                            foreach (var (containerPort, hostPort) in portMap) flags.Add($"-p {hostPort}:{containerPort}");
                        if (desc?.MountPath != null) flags.Add($"-v {containerName}_data:{desc.MountPath}");

                        b.Line($"  \"docker run {string.Join(" ", flags)} {dockerImage}{cmd}\",");
                    }
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }

        // Elastic private-registry images
        foreach (var image in elasticImages)
        {
            if (!TopologyHelpers.RequiresPrivateRegistry(image.ResolveTypeId(), _imageRegistry)) continue;

            var resourceName = TopologyHelpers.SanitizeName(image.Name);
            var dockerImage = TopologyHelpers.GetDockerImageForHcl(image, TopologyHelpers.ResolveRegistry(topology), _imageRegistry);
            var desc = _imageRegistry.GetDescriptor(image);
            var parentContainer = resolver.FindHostFor(image.Id) ?? resolver.FindCaddyFor(image.Id);
            var parentEntry = parentContainer != null
                ? new TopologyHelpers.HostEntry(parentContainer)
                : new TopologyHelpers.HostEntry(new Container { Name = resourceName });
            var resolveFrom = new Container { Id = Guid.NewGuid(), Name = resourceName };
            var envVars = TopologyHelpers.BuildEnvVars(image, parentEntry, resolver, _imageRegistry, _templateEngine, topology, resolveFrom, allPortAssignments);

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}\"", b =>
            {
                var varName = $"{resourceName}_replicas";
                b.RawAttribute("count", $"var.deploy_apps ? var.{varName} : 0");
                b.RawAttribute("depends_on", $"[linode_instance.{resourceName}]");
                b.Line();
                var versionVar = TopologyHelpers.GetVersionVariableName(image.ResolveTypeId(), _imageRegistry);
                b.MapBlock("triggers", tb => tb.RawAttribute(versionVar, $"var.{versionVar}"));
                b.Line();
                b.Block("connection", cb =>
                {
                    cb.Attribute("type", "ssh");
                    cb.RawAttribute("host", $"linode_instance.{resourceName}[count.index].ip_address");
                    cb.Attribute("user", "root");
                    cb.RawAttribute("private_key", "nonsensitive(tls_private_key.deploy.private_key_pem)");
                });
                b.Line();
                b.Block("provisioner \"remote-exec\"", pb =>
                {
                    pb.RawAttribute("inline", "[");

                    // Elastic images get dedicated instances - install Docker (idempotent)
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");
                    b.Line("  \"docker network create xcord-bridge 2>/dev/null || true\",");

                    // Docker login for private registry
                    b.Line($"  \"{TopologyHelpers.GenerateDockerLoginCommand(useSudo: false)}\",");

                    // Pull new image while old container still serves
                    b.Line($"  \"docker pull {dockerImage}\",");
                    b.Line($"  \"docker rm -f {resourceName} 2>/dev/null || true\",");

                    var flags = new List<string> { "-d", $"--name {resourceName}", "--network xcord-bridge", "--restart unless-stopped" };
                    foreach (var (key, value) in envVars) flags.Add($"-e '{key}={value}'");
                    if (desc?.MountPath != null) flags.Add($"-v {resourceName}_data:{desc.MountPath}");
                    if (desc?.Ports.Length > 0)
                        foreach (var port in desc.Ports.Select(p => p.Port)) flags.Add($"-p {port}:{port}");

                    b.Line($"  \"docker run {string.Join(" ", flags)} {dockerImage}\",");
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }
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

                    b.RawAttribute("region", "var.linode_region");
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
