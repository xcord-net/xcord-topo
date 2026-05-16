using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
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
                        // or lives on a DataPool (DataPool images are always accessed from other hosts).
                        // Exception: IsPublicEndpoint images on a Caddy host do NOT publish ports -
                        // Caddy already binds 80/443 and reverse-proxies via the Docker bridge network.
                        var hasCaddy = caddies.Count > 0;
                        var publishPorts = desc?.Ports.Length > 0 &&
                            (((desc?.IsPublicEndpoint ?? false) && !hasCaddy) ||
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
                            var extraFlags = string.Join(" ", flags.Skip(2)); // skip -d and --name

                            if (image.ResolveTypeId() == "Registry")
                            {
                                b.Line($"  {TopologyHelpers.GenerateRegistryRetryBlock(containerName, $"{dockerImage}{cmd}", extraFlags, sudo: false)}");
                            }
                            else
                            {
                                b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");
                                b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                                b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(containerName, sudo: false)}");
                            }
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
                            b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(caddyName, sudo: false)}");
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
                            b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck("caddy_registry", sudo: false)}");
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
                    // Per-pool isolated overlay network - prevents lateral movement between pools.
                    // Instance containers deployed into this pool join xcord-pool-{poolName}, not a
                    // shared network, so instances on different pools cannot reach each other directly.
                    b.Line($"  \"docker network create --driver overlay --attachable xcord-pool-{poolName} 2>/dev/null || true\",");
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
                        // Caddy joins the pool-specific network to proxy to instance containers on the same pool.
                        b.Line($"  \"docker service create --name {caddyName} --mode global --network xcord-pool-{poolName} -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");

                        var rateLimitCmds = TopologyHelpers.GenerateRateLimitCommands(caddy);
                        foreach (var rlCmd in rateLimitCmds)
                            b.Line($"  \"{rlCmd}\",");
                    }
                    if (poolCaddies.Count == 0)
                    {
                        b.Line($"  \"mkdir -p /opt/caddy\",");
                        b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n\\nCADDYEOF\",");
                        b.Line($"  \"docker service rm caddy 2>/dev/null || true\",");
                        // Caddy joins the pool-specific network to proxy to instance containers on the same pool.
                        b.Line($"  \"docker service create --name caddy --mode global --network xcord-pool-{poolName} -p 80:80 -p 443:443 --mount type=bind,source=/opt/caddy/Caddyfile,target=/etc/caddy/Caddyfile --mount type=volume,source=caddy_data,target=/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
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
                        var extraFlagsStandalone = string.Join(" ", flags.Skip(2)); // skip -d and --name

                        if (image.ResolveTypeId() == "Registry")
                        {
                            b.Line($"  {TopologyHelpers.GenerateRegistryRetryBlock(containerName, $"{dockerImage}{cmd}", extraFlagsStandalone, sudo: false)}");
                        }
                        else
                        {
                            b.Line($"  \"docker rm -f {containerName} 2>/dev/null || true\",");
                            b.Line($"  \"docker run {flagStr} {dockerImage}{cmd}\",");
                            b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(containerName, sudo: false)}");
                        }
                    }

                    b.Line($"  \"mkdir -p /opt/caddy\",");
                    var escapedCaddyfile = caddyfile.Replace("\"", "\\\"");
                    var caddyfileLines = escapedCaddyfile.Split('\n');
                    b.Line($"  \"cat > /opt/caddy/Caddyfile << 'CADDYEOF'\\n{string.Join("\\n", caddyfileLines)}\\nCADDYEOF\",");
                    b.Line($"  \"docker rm -f {resourceName} 2>/dev/null || true\",");
                    b.Line($"  \"docker run -d --name {resourceName} --network xcord-bridge --restart unless-stopped -p 80:80 -p 443:443 -v /opt/caddy/Caddyfile:/etc/caddy/Caddyfile -v caddy_data:/data {ImageOperationalMetadata.Caddy.DockerImage}\",");
                    b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(resourceName, sudo: false)}");

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
                    b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(resourceName, sudo: false)}");

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

            provisioning.Block($"resource \"null_resource\" \"deploy_{resourceName}\"", b =>
            {
                var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                if (isReplicated)
                    b.RawAttribute("count", $"var.deploy_apps ? {countExpr ?? "1"} : 0");
                else
                    b.RawAttribute("count", "var.deploy_apps ? 1 : 0");

                // Every host with images gets a provision_* resource in phase 1
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
                            b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(containerName, sudo: false)}");
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
                        b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(containerName, sudo: false)}");
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
                    b.Line($"  {TopologyHelpers.GenerateContainerHealthCheck(resourceName, sudo: false)}");
                    pb.Line("]");
                });
            });
            provisioning.Line();
        }
    }

}
