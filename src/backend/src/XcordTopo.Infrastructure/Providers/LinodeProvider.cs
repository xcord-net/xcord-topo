using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed class LinodeProvider : IInfrastructureProvider
{
    public string Key => "linode";

    public ProviderInfo GetInfo() => new()
    {
        Key = "linode",
        Name = "Linode (Akamai)",
        Description = "Akamai Connected Cloud (formerly Linode). Affordable VPS hosting with global regions.",
        SupportedContainerKinds = ["Host", "Network", "Caddy", "FederationGroup"]
    };

    public List<Region> GetRegions() =>
    [
        new() { Id = "us-east", Label = "Newark, NJ", Country = "US" },
        new() { Id = "us-central", Label = "Dallas, TX", Country = "US" },
        new() { Id = "us-west", Label = "Fremont, CA", Country = "US" },
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

    public Dictionary<string, string> GenerateHcl(Topology topology)
    {
        var files = new Dictionary<string, string>();
        var hosts = CollectHosts(topology.Containers, null);
        var resolver = new WireResolver(topology);

        files["main.tf"] = GenerateMain();
        files["secrets.tf"] = GenerateSecrets(hosts, resolver, topology);
        files["variables.tf"] = GenerateVariables(topology);
        files["instances.tf"] = GenerateInstances(topology, hosts);
        files["firewall.tf"] = GenerateFirewall(topology, hosts);
        files["provisioning.tf"] = GenerateProvisioning(hosts, resolver, topology);
        files["volumes.tf"] = GenerateVolumes(hosts);
        files["nodebalancers.tf"] = GenerateNodeBalancers(hosts);
        files["outputs.tf"] = GenerateOutputs(hosts);

        return files;
    }

    // --- Tree-walking helpers ---

    internal record HostEntry(Container Host, Container? FedGroup);

    internal static List<HostEntry> CollectHosts(List<Container> containers, Container? fedGroup)
    {
        var result = new List<HostEntry>();
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.Host)
            {
                result.Add(new HostEntry(container, fedGroup));
                result.AddRange(CollectHosts(container.Children, fedGroup));
            }
            else if (container.Kind == ContainerKind.FederationGroup)
            {
                result.AddRange(CollectHosts(container.Children, container));
            }
            else if (container.Kind == ContainerKind.Network)
            {
                result.AddRange(CollectHosts(container.Children, fedGroup));
            }
        }
        return result;
    }

    internal static List<Image> CollectImages(Container container)
    {
        var images = new List<Image>(container.Images);
        foreach (var child in container.Children)
        {
            if (child.Kind != ContainerKind.FederationGroup)
                images.AddRange(CollectImages(child));
        }
        return images;
    }

    internal static List<Container> CollectCaddyContainers(Container container)
    {
        var caddies = new List<Container>();
        foreach (var child in container.Children)
        {
            if (child.Kind == ContainerKind.Caddy)
                caddies.Add(child);
            else if (child.Kind != ContainerKind.FederationGroup)
                caddies.AddRange(CollectCaddyContainers(child));
        }
        return caddies;
    }

    private static bool IsVariableRef(string value) =>
        value.StartsWith('$') && value.Length > 1 && !value.Contains(' ');

    private static (int? Literal, string? VarRef) ParseReplicas(Image image)
    {
        var replicas = image.Config.GetValueOrDefault("replicas", "1");
        if (IsVariableRef(replicas))
            return (null, replicas[1..]);
        return (int.TryParse(replicas, out var n) ? n : 1, null);
    }

    internal static (int? Literal, string? VarRef) ParseHostReplicas(Container host)
    {
        var replicas = host.Config.GetValueOrDefault("replicas", "1");
        if (IsVariableRef(replicas))
            return (null, replicas[1..]);
        return (int.TryParse(replicas, out var n) ? n : 1, null);
    }

    internal static bool IsReplicatedHost(HostEntry entry)
    {
        if (entry.FedGroup != null) return true;
        var (literal, varRef) = ParseHostReplicas(entry.Host);
        return varRef != null || (literal.HasValue && literal.Value > 1);
    }

    /// <summary>
    /// Get the Terraform count expression for a replicated host.
    /// FedGroup hosts use the federation instance_count variable.
    /// Host-replicated hosts use a literal or variable reference.
    /// Returns null for non-replicated hosts.
    /// </summary>
    internal static string? GetHostCountExpression(HostEntry entry)
    {
        if (entry.FedGroup != null)
            return $"var.{SanitizeName(entry.FedGroup.Name)}_instance_count";

        var (literal, varRef) = ParseHostReplicas(entry.Host);
        if (varRef != null)
            return $"var.{SanitizeName(varRef)}";
        if (literal.HasValue && literal.Value > 1)
        {
            // If min/max are set, use a variable (for runtime override) instead of literal
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");
            if (hasMinMax)
                return $"var.{SanitizeName(entry.Host.Name)}_replicas";
            return literal.Value.ToString();
        }
        return null;
    }

    // --- Compute plan auto-selection ---

    internal static int CalculateHostRam(Container host)
    {
        var totalRam = 0;
        var images = CollectImages(host);
        foreach (var image in images)
        {
            if (ImageOperationalMetadata.Images.TryGetValue(image.Kind, out var meta))
                totalRam += meta.MinRamMb;
            else
                totalRam += 256;
        }
        // Add Caddy overhead if present
        var caddies = CollectCaddyContainers(host);
        if (caddies.Count > 0)
            totalRam += ImageOperationalMetadata.Caddy.MinRamMb;

        return totalRam;
    }

    internal string SelectPlan(int requiredRamMb)
    {
        var plans = GetPlans().OrderBy(p => p.PriceMonthly).ToList();
        foreach (var plan in plans)
        {
            if (plan.MemoryMb >= requiredRamMb)
                return plan.Id;
        }
        return plans.Last().Id; // largest available
    }

    // --- Secret helpers ---

    internal record SecretEntry(string ResourceName, string Description);

    internal static List<SecretEntry> CollectSecrets(HostEntry entry, WireResolver resolver)
    {
        if (entry.FedGroup != null) return []; // Hub manages federation secrets

        var secrets = new List<SecretEntry>();
        var hostName = SanitizeName(entry.Host.Name);
        var images = CollectImages(entry.Host);

        foreach (var image in images)
        {
            var imgName = SanitizeName(image.Name);
            switch (image.Kind)
            {
                case ImageKind.PostgreSQL:
                    secrets.Add(new($"{hostName}_{imgName}_password", $"PostgreSQL password for {image.Name}"));
                    break;
                case ImageKind.Redis:
                    secrets.Add(new($"{hostName}_{imgName}_password", $"Redis password for {image.Name}"));
                    break;
                case ImageKind.MinIO:
                    secrets.Add(new($"{hostName}_{imgName}_access_key", $"MinIO access key for {image.Name}"));
                    secrets.Add(new($"{hostName}_{imgName}_secret_key", $"MinIO secret key for {image.Name}"));
                    break;
                case ImageKind.LiveKit:
                    secrets.Add(new($"{hostName}_{imgName}_api_key", $"LiveKit API key for {image.Name}"));
                    secrets.Add(new($"{hostName}_{imgName}_api_secret", $"LiveKit API secret for {image.Name}"));
                    break;
            }
        }
        return secrets;
    }

    /// <summary>
    /// Derive DB name from the consumer wired to this PG image.
    /// HubServer → xcord_hub, FederationServer → xcord, otherwise → app
    /// </summary>
    internal static string DeriveDbName(Image pgImage, WireResolver resolver)
    {
        var incoming = resolver.ResolveIncoming(pgImage.Id, "postgres");
        foreach (var (node, _) in incoming)
        {
            if (node is Image consumerImage)
            {
                return consumerImage.Kind switch
                {
                    ImageKind.HubServer => "xcord_hub",
                    ImageKind.FederationServer => "xcord",
                    _ => "app"
                };
            }
        }
        return "app";
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

    private static string GenerateSecrets(List<HostEntry> hosts, WireResolver resolver, Topology topology)
    {
        var secrets = new HclBuilder();
        foreach (var entry in hosts)
        {
            var allSecrets = CollectSecrets(entry, resolver);
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

    private static string GenerateVariables(Topology topology)
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

        // FederationGroup instance_count variables
        CollectFedGroupVariables(topology.Containers, vars);

        // Host replica $VAR variables
        var hosts = CollectHosts(topology.Containers, null);
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
                var varName = $"{SanitizeName(container.Name)}_instance_count";
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

    private static void CollectHostReplicaVariables(List<HostEntry> hosts, HclBuilder vars)
    {
        foreach (var entry in hosts)
        {
            if (entry.FedGroup != null) continue; // FedGroup hosts use the fedgroup variable

            var (literal, varRef) = ParseHostReplicas(entry.Host);
            var hasMinMax = entry.Host.Config.ContainsKey("minReplicas") || entry.Host.Config.ContainsKey("maxReplicas");

            // $VAR always needs a variable; literal replicas only need one when min/max are set
            var needsVariable = varRef != null || (literal.HasValue && literal.Value > 1 && hasMinMax);
            if (!needsVariable) continue;

            var varName = varRef != null ? SanitizeName(varRef) : $"{SanitizeName(entry.Host.Name)}_replicas";
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

    private string GenerateInstances(Topology topology, List<HostEntry> hosts)
    {
        var instances = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = SanitizeName(entry.Host.Name);
            var ramRequired = CalculateHostRam(entry.Host);
            var plan = SelectPlan(ramRequired);

            instances.Block($"resource \"linode_instance\" \"{resourceName}\"", b =>
            {
                var countExpr = GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                b.Attribute("label", IsReplicatedHost(entry)
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
        return instances.ToString();
    }

    private static string GenerateFirewall(Topology topology, List<HostEntry> hosts)
    {
        var firewall = new HclBuilder();

        // Check if any non-federation host contains LiveKit (includes replicated hosts)
        var hasLiveKit = hosts.Any(e =>
            e.FedGroup == null && CollectImages(e.Host).Any(i => i.Kind == ImageKind.LiveKit));

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

            // Build linodes list with splat for counted resources
            var refs = new List<string>();
            foreach (var entry in hosts)
            {
                var resourceName = SanitizeName(entry.Host.Name);
                if (IsReplicatedHost(entry))
                    refs.Add($"linode_instance.{resourceName}[*].id");
                else
                    refs.Add($"[linode_instance.{resourceName}.id]");
            }

            if (refs.Any(r => r.Contains("[*]")))
            {
                var concatArgs = string.Join(", ", refs);
                b.RawAttribute("linodes", $"concat({concatArgs})");
            }
            else
            {
                var idRefs = hosts.Select(e => $"linode_instance.{SanitizeName(e.Host.Name)}.id");
                b.RawAttribute("linodes", $"[{string.Join(", ", idRefs)}]");
            }
        });
        return firewall.ToString();
    }

    private string GenerateProvisioning(List<HostEntry> hosts, WireResolver resolver, Topology topology)
    {
        var provisioning = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = SanitizeName(entry.Host.Name);
            var hostName = SanitizeName(entry.Host.Name);
            var images = CollectImages(entry.Host);
            var caddies = CollectCaddyContainers(entry.Host);
            var isFederation = entry.FedGroup != null;
            var isReplicated = IsReplicatedHost(entry);

            // Skip hosts with no images and no caddy (nothing to provision)
            if (images.Count == 0 && caddies.Count == 0) continue;

            provisioning.Block($"resource \"null_resource\" \"provision_{resourceName}\"", b =>
            {
                // Dependencies
                var depsList = new List<string> { $"linode_instance.{resourceName}" };

                var countExpr = GetHostCountExpression(entry);
                if (countExpr != null)
                    b.RawAttribute("count", countExpr);

                // Collect secret dependencies for static hosts (not federation)
                if (!isFederation)
                {
                    var secrets = CollectSecrets(entry, resolver);
                    foreach (var secret in secrets)
                        depsList.Add($"random_password.{secret.ResourceName}");
                }

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

                    // Docker installation
                    b.Line("  \"curl -fsSL https://get.docker.com | sh\",");
                    b.Line("  \"systemctl enable docker\",");
                    b.Line("  \"systemctl start docker\",");

                    if (!isFederation)
                    {
                        // Create bridge network
                        b.Line("  \"docker network create xcord-bridge\",");

                        // Docker run per image
                        foreach (var image in images)
                        {
                            var dockerImage = image.DockerImage ?? GetDefaultDockerImage(image.Kind);
                            var containerName = SanitizeName(image.Name);
                            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);

                            var flags = new List<string>
                            {
                                "-d",
                                $"--name {containerName}",
                                "--network xcord-bridge",
                                "--restart unless-stopped"
                            };

                            // Environment variables from wires
                            var envVars = BuildEnvVars(image, entry, resolver);
                            foreach (var (key, value) in envVars)
                                flags.Add($"-e {key}={value}");

                            // Volume mount
                            var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
                            if (!string.IsNullOrEmpty(volumeSize) && meta?.MountPath != null)
                                flags.Add($"-v {containerName}_data:{meta.MountPath}");

                            // Port mapping — only for cross-host accessible services, LiveKit, Caddy
                            if (image.Kind == ImageKind.LiveKit && meta != null)
                            {
                                foreach (var port in meta.Ports)
                                    flags.Add($"-p {port}:{port}");
                            }

                            // Command override
                            var cmdOverride = ResolveCommandOverride(image, entry, resolver);
                            var cmd = cmdOverride != null ? $" {cmdOverride}" : "";

                            var flagStr = string.Join(" ", flags);
                            b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                        }

                        // Caddy containers
                        foreach (var caddy in caddies)
                        {
                            var caddyfile = GenerateCaddyfile(caddy, resolver);
                            var caddyName = SanitizeName(caddy.Name);

                            // Write Caddyfile to host
                            b.Line($"  \"mkdir -p /opt/caddy\",");
                            // Escape the caddyfile for shell heredoc
                            var escapedCaddyfile = caddyfile.Replace("\"", "\\\"").Replace("$", "\\$");
                            var caddyfileLines = escapedCaddyfile.Split('\n');
                            b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");

                            b.Line($"  \"docker run -d --name {caddyName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                        }
                        // Backup cron jobs for volume-bearing images
                        var backupCommands = GenerateBackupCommands(images, entry.Host);
                        foreach (var cmd in backupCommands)
                            b.Line($"  \"{cmd}\",");
                    }
                    // Federation hosts: Docker install only — hub provisions at runtime

                    pb.Line("]");
                });
            });
            provisioning.Line();
        }
        return provisioning.ToString();
    }

    /// <summary>
    /// Build environment variables for an image based on its wire connections and secret references.
    /// </summary>
    internal static List<(string Key, string Value)> BuildEnvVars(
        Image image, HostEntry entry, WireResolver resolver)
    {
        var envVars = new List<(string, string)>();
        var hostName = SanitizeName(entry.Host.Name);

        switch (image.Kind)
        {
            case ImageKind.PostgreSQL:
            {
                var secretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_password.result}}";
                var dbName = DeriveDbName(image, resolver);
                envVars.Add(("POSTGRES_PASSWORD", secretRef));
                envVars.Add(("POSTGRES_DB", dbName));
                envVars.Add(("POSTGRES_USER", "postgres"));
                break;
            }
            case ImageKind.Redis:
                // Redis uses command override, not env vars — no env needed
                break;
            case ImageKind.MinIO:
            {
                var accessKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_access_key.result}}";
                var secretKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_secret_key.result}}";
                envVars.Add(("MINIO_ROOT_USER", accessKeyRef));
                envVars.Add(("MINIO_ROOT_PASSWORD", secretKeyRef));
                break;
            }
            case ImageKind.HubServer:
            {
                // Resolve PG connection via wire
                var pgTarget = resolver.ResolveWiredImage(image.Id, "pg_connection");
                if (pgTarget != null)
                {
                    var pgContainer = SanitizeName(pgTarget.Name);
                    var pgHost = resolver.FindHostFor(pgTarget.Id);
                    var pgHostName = pgHost != null ? SanitizeName(pgHost.Name) : hostName;
                    var dbName = DeriveDbName(pgTarget, resolver);
                    var pgSecretRef = $"${{random_password.{pgHostName}_{pgContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__DefaultConnection",
                        $"Host={pgContainer};Port=5432;Database={dbName};Username=postgres;Password={pgSecretRef}"));
                }

                // Resolve Redis connection via wire
                var redisTarget = resolver.ResolveWiredImage(image.Id, "redis_connection");
                if (redisTarget != null)
                {
                    var redisContainer = SanitizeName(redisTarget.Name);
                    var redisHost = resolver.FindHostFor(redisTarget.Id);
                    var redisHostName = redisHost != null ? SanitizeName(redisHost.Name) : hostName;
                    var redisSecretRef = $"${{random_password.{redisHostName}_{redisContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__Redis",
                        $"{redisContainer}:6379,password={redisSecretRef}"));
                }
                break;
            }
            case ImageKind.FederationServer:
            {
                // Resolve PG connection via wire
                var pgTarget = resolver.ResolveWiredImage(image.Id, "pg_connection");
                if (pgTarget != null)
                {
                    var pgContainer = SanitizeName(pgTarget.Name);
                    var pgHost = resolver.FindHostFor(pgTarget.Id);
                    var pgHostName = pgHost != null ? SanitizeName(pgHost.Name) : hostName;
                    var dbName = DeriveDbName(pgTarget, resolver);
                    var pgSecretRef = $"${{random_password.{pgHostName}_{pgContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__DefaultConnection",
                        $"Host={pgContainer};Port=5432;Database={dbName};Username=postgres;Password={pgSecretRef}"));
                }

                // Resolve Redis connection via wire
                var redisTarget = resolver.ResolveWiredImage(image.Id, "redis_connection");
                if (redisTarget != null)
                {
                    var redisContainer = SanitizeName(redisTarget.Name);
                    var redisHost = resolver.FindHostFor(redisTarget.Id);
                    var redisHostName = redisHost != null ? SanitizeName(redisHost.Name) : hostName;
                    var redisSecretRef = $"${{random_password.{redisHostName}_{redisContainer}_password.result}}";
                    envVars.Add(("ConnectionStrings__Redis",
                        $"{redisContainer}:6379,password={redisSecretRef}"));
                }

                // Resolve MinIO connection via wire
                var minioTarget = resolver.ResolveWiredImage(image.Id, "minio_connection");
                if (minioTarget != null)
                {
                    var minioContainer = SanitizeName(minioTarget.Name);
                    var minioHost = resolver.FindHostFor(minioTarget.Id);
                    var minioHostName = minioHost != null ? SanitizeName(minioHost.Name) : hostName;
                    var accessRef = $"${{random_password.{minioHostName}_{minioContainer}_access_key.result}}";
                    var secretRef = $"${{random_password.{minioHostName}_{minioContainer}_secret_key.result}}";
                    envVars.Add(("MinIO__Endpoint", $"{minioContainer}:9000"));
                    envVars.Add(("MinIO__AccessKey", accessRef));
                    envVars.Add(("MinIO__SecretKey", secretRef));
                }
                break;
            }
            case ImageKind.LiveKit:
            {
                var apiKeyRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_api_key.result}}";
                var apiSecretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_api_secret.result}}";
                envVars.Add(("LIVEKIT_KEYS", $"{apiKeyRef}: {apiSecretRef}"));
                break;
            }
        }

        return envVars;
    }

    internal static string? ResolveCommandOverride(Image image, HostEntry entry, WireResolver resolver)
    {
        if (image.Kind == ImageKind.Redis)
        {
            var hostName = SanitizeName(entry.Host.Name);
            var secretRef = $"${{random_password.{hostName}_{SanitizeName(image.Name)}_password.result}}";
            return $"redis-server --requirepass {secretRef}";
        }

        if (image.Kind == ImageKind.MinIO)
            return "server /data --console-address :9001";

        return null;
    }

    internal static List<string> GenerateBackupCommands(List<Image> images, Container host)
    {
        var commands = new List<string>();
        var scheduleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hourly"] = "0 * * * *",
            ["daily"] = "0 2 * * *",
            ["weekly"] = "0 2 * * 0"
        };

        foreach (var image in images)
        {
            var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
            if (string.IsNullOrEmpty(volumeSize)) continue;

            // Resolve frequency: image config → host config → skip
            var frequency = image.Config.GetValueOrDefault("backupFrequency", "");
            if (string.IsNullOrEmpty(frequency))
                frequency = host.Config.GetValueOrDefault("backupFrequency", "");
            if (string.IsNullOrEmpty(frequency)) continue;

            if (!scheduleMap.TryGetValue(frequency, out var schedule)) continue;

            // Resolve retention: image config → host config → default 7
            var retentionStr = image.Config.GetValueOrDefault("backupRetention", "");
            if (string.IsNullOrEmpty(retentionStr))
                retentionStr = host.Config.GetValueOrDefault("backupRetention", "");
            var retention = int.TryParse(retentionStr, out var r) ? r : 7;

            var containerName = SanitizeName(image.Name);
            var backupDir = $"/opt/backups/{containerName}";

            // Build backup command based on image kind
            var backupCmd = image.Kind switch
            {
                ImageKind.PostgreSQL =>
                    $"docker exec {containerName} pg_dumpall -U postgres | gzip > {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).sql.gz",
                ImageKind.Redis =>
                    $"docker exec {containerName} redis-cli BGSAVE && sleep 2 && docker cp {containerName}:/data/dump.rdb {backupDir}/{containerName}_$(date +%Y%m%d_%H%M%S).rdb",
                ImageKind.MinIO =>
                    $"docker run --rm --network xcord-bridge -v {backupDir}:/backup minio/mc mirror http://{containerName}:9000 /backup/{containerName}_$(date +%Y%m%d_%H%M%S)/",
                _ => null
            };

            if (backupCmd == null) continue;

            var scriptContent = $"#!/bin/bash\\n{backupCmd}\\nfind {backupDir} -type f -mtime +{retention} -delete\\nfind {backupDir} -type d -empty -delete";

            commands.Add($"mkdir -p {backupDir}");
            commands.Add($"printf '{scriptContent}\\n' > {backupDir}/backup.sh");
            commands.Add($"chmod +x {backupDir}/backup.sh");
            commands.Add($"(crontab -l 2>/dev/null; echo \\\"{schedule} {backupDir}/backup.sh\\\") | crontab -");
        }

        return commands;
    }

    internal static string GenerateCaddyfile(Container caddy, WireResolver resolver)
    {
        var upstreams = resolver.ResolveCaddyUpstreams(caddy);
        var domain = caddy.Config.GetValueOrDefault("domain", "{$DOMAIN}");

        var lines = new List<string> { $"{domain} {{" };

        foreach (var (image, upstreamPath) in upstreams)
        {
            var containerName = SanitizeName(image.Name);
            var meta = ImageOperationalMetadata.Images.GetValueOrDefault(image.Kind);
            var port = meta?.Ports.FirstOrDefault() ?? 80;
            lines.Add($"  handle_path {upstreamPath} {{");
            lines.Add($"    reverse_proxy {containerName}:{port}");
            lines.Add("  }");
        }

        lines.Add("}");
        return string.Join("\n", lines);
    }

    private static string GenerateVolumes(List<HostEntry> hosts)
    {
        var volumes = new HclBuilder();
        foreach (var entry in hosts)
        {
            var images = CollectImages(entry.Host);
            foreach (var image in images)
            {
                var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
                if (string.IsNullOrEmpty(volumeSize)) continue;

                var size = int.TryParse(volumeSize, out var s) ? s : 25;
                var resourceName = $"{SanitizeName(entry.Host.Name)}_{SanitizeName(image.Name)}";

                var isReplicated = IsReplicatedHost(entry);
                volumes.Block($"resource \"linode_volume\" \"{resourceName}_vol\"", b =>
                {
                    if (isReplicated)
                    {
                        var countExpr = GetHostCountExpression(entry);
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
                        ? $"linode_instance.{SanitizeName(entry.Host.Name)}[count.index].id"
                        : $"linode_instance.{SanitizeName(entry.Host.Name)}.id");
                });
                volumes.Line();
            }
        }
        return volumes.ToString();
    }

    private static string GenerateNodeBalancers(List<HostEntry> hosts)
    {
        var nb = new HclBuilder();
        foreach (var entry in hosts)
        {
            var images = CollectImages(entry.Host);
            foreach (var image in images)
            {
                var (literal, varRef) = ParseReplicas(image);
                var needsLb = varRef != null || (literal.HasValue && literal.Value > 1);
                if (!needsLb) continue;

                var resourceName = $"{SanitizeName(entry.Host.Name)}_{SanitizeName(image.Name)}";

                var isReplicated = IsReplicatedHost(entry);
                nb.Block($"resource \"linode_nodebalancer\" \"{resourceName}_nb\"", b =>
                {
                    if (isReplicated)
                    {
                        var countExpr = GetHostCountExpression(entry);
                        b.RawAttribute("count", countExpr!);
                    }
                    b.Attribute("label", $"{image.Name}-lb");
                    b.RawAttribute("region", "var.region");
                });
                nb.Line();

                nb.Block($"resource \"linode_nodebalancer_config\" \"{resourceName}_nb_config\"", b =>
                {
                    if (isReplicated)
                    {
                        var countExpr = GetHostCountExpression(entry);
                        b.RawAttribute("count", countExpr!);
                        b.RawAttribute("nodebalancer_id", $"linode_nodebalancer.{resourceName}_nb[count.index].id");
                    }
                    else
                    {
                        b.RawAttribute("nodebalancer_id", $"linode_nodebalancer.{resourceName}_nb.id");
                    }
                    b.Attribute("port", 80);
                    b.Attribute("protocol", "http");
                    b.Attribute("algorithm", "roundrobin");
                    b.Attribute("check", "http");
                    b.Attribute("check_path", "/");
                });
                nb.Line();
            }
        }
        return nb.ToString();
    }

    private static string GenerateOutputs(List<HostEntry> hosts)
    {
        var outputs = new HclBuilder();
        foreach (var entry in hosts)
        {
            var resourceName = SanitizeName(entry.Host.Name);
            if (IsReplicatedHost(entry))
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
        return outputs.ToString();
    }

    // --- Utilities ---

    internal static string SanitizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_')
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Aggregate("", (current, c) => current + c);

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
