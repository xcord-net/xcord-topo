using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Migration;

/// <summary>
/// Generates an ordered migration plan from a diff result and user decisions.
/// </summary>
public sealed class MigrationPlanGenerator
{
    public MigrationPlan Generate(
        Topology source, Topology target,
        MigrationDiffResult diff, List<MigrationDecision> decisions)
    {
        var plan = new MigrationPlan
        {
            SourceTopologyId = source.Id,
            SourceTopologyName = source.Name,
            TargetTopologyId = target.Id,
            TargetTopologyName = target.Name,
            Diff = diff,
            Decisions = decisions,
            Phases =
            [
                GeneratePreCheckPhase(source, target),
                GenerateInfrastructurePhase(diff),
                GenerateDataMigrationPhase(diff, decisions),
                GenerateProvisioningPhase(diff, target),
                GenerateCutoverPhase(diff, decisions),
                GenerateCleanupPhase(diff)
            ]
        };

        // Remove empty phases
        plan.Phases = plan.Phases.Where(p => p.Steps.Count > 0).ToList();

        return plan;
    }

    private static MigrationPhase GeneratePreCheckPhase(Topology source, Topology target)
    {
        var steps = new List<MigrationStep>
        {
            new()
            {
                Order = 1,
                Type = MigrationStepType.Validate,
                Description = $"Validate source topology \"{source.Name}\" is deployed and accessible",
                Script = "# Verify current infrastructure is reachable\n" +
                         "terraform -chdir=source/ plan -detailed-exitcode",
                CausesDowntime = false,
                EstimatedDuration = "1m"
            },
            new()
            {
                Order = 2,
                Type = MigrationStepType.Validate,
                Description = $"Validate target topology \"{target.Name}\" configuration",
                Script = "# Validate target Terraform configuration\n" +
                         "terraform -chdir=target/ validate",
                CausesDowntime = false,
                EstimatedDuration = "30s"
            }
        };

        return new MigrationPhase
        {
            Type = MigrationPhaseType.PreCheck,
            Name = "Pre-flight Checks",
            Description = "Validate both topologies before starting migration",
            Steps = steps
        };
    }

    private static MigrationPhase GenerateInfrastructurePhase(MigrationDiffResult diff)
    {
        var steps = new List<MigrationStep>();
        var order = 1;

        // New hosts
        var addedHosts = diff.ContainerMatches
            .Where(c => c.Kind == ContainerMatchKind.Added)
            .ToList();

        if (addedHosts.Count > 0)
        {
            var hostNames = string.Join(", ", addedHosts.Select(h => h.TargetContainerName));
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.TerraformApply,
                Description = $"Provision {addedHosts.Count} new host(s): {hostNames}",
                Script = "# Apply target Terraform to create new infrastructure\n" +
                         "terraform -chdir=target/ apply -auto-approve",
                CausesDowntime = false,
                EstimatedDuration = $"{addedHosts.Count * 3}m"
            });
        }

        // New volumes for relocated images with volumeSize config
        var relocatedWithVolumes = diff.ImageMatches
            .Where(m => m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split &&
                        !m.TargetIsFederation)
            .ToList();

        if (relocatedWithVolumes.Count > 0)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.TerraformApply,
                Description = "Create volumes for relocated services on new hosts",
                Script = "# Volumes are created as part of the Terraform apply above\n" +
                         "# Verify volumes are attached and mounted",
                CausesDowntime = false,
                EstimatedDuration = "2m"
            });
        }

        // Secrets for new infrastructure
        var newInfra = diff.ImageMatches
            .Where(m => m.Kind == ImageMatchKind.Added &&
                        m.TargetImageKind is ImageKind.PostgreSQL or ImageKind.Redis or ImageKind.MinIO)
            .ToList();

        if (newInfra.Count > 0)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.SecretGenerate,
                Description = $"Generate secrets for {newInfra.Count} new infrastructure service(s)",
                Script = "# Terraform random_password resources handle secret generation\n" +
                         "# Secrets are injected via Docker secrets",
                CausesDowntime = false,
                EstimatedDuration = "30s"
            });
        }

        return new MigrationPhase
        {
            Type = MigrationPhaseType.Infrastructure,
            Name = "Infrastructure Provisioning",
            Description = "Create new hosts, volumes, and network resources",
            Steps = steps
        };
    }

    private static MigrationPhase GenerateDataMigrationPhase(
        MigrationDiffResult diff, List<MigrationDecision> decisions)
    {
        var steps = new List<MigrationStep>();
        var order = 1;

        var decisionMap = decisions.ToDictionary(d => d.Id);

        // Hub PostgreSQL migration
        var hubPgRelocated = diff.ImageMatches.Any(m =>
            m.SourceImageKind == ImageKind.PostgreSQL &&
            !m.TargetIsFederation &&
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split);

        if (hubPgRelocated && decisionMap.TryGetValue("hub-db-migration", out var dbDecision))
        {
            switch (dbDecision.SelectedOptionKey)
            {
                case "pg_dump_restore":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.DatabaseDump,
                        Description = "Dump hub PostgreSQL database",
                        Script = "# Stop hub application to ensure consistency\n" +
                                 "docker stop hub-server\n\n" +
                                 "# Dump the database\n" +
                                 "pg_dump -h $SOURCE_PG_HOST -U xcord -d xcord_hub -F c -f /tmp/hub_backup.dump",
                        CausesDowntime = true,
                        EstimatedDuration = "5m"
                    });
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.DatabaseRestore,
                        Description = "Restore hub database on new host",
                        Script = "# Transfer dump to new host\n" +
                                 "scp /tmp/hub_backup.dump $TARGET_PG_HOST:/tmp/\n\n" +
                                 "# Restore on target\n" +
                                 "pg_restore -h $TARGET_PG_HOST -U xcord -d xcord_hub -c /tmp/hub_backup.dump",
                        CausesDowntime = true,
                        EstimatedDuration = "5m"
                    });
                    break;

                case "streaming_replication":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.DatabaseRestore,
                        Description = "Set up streaming replication from source to target PG",
                        Script = "# Configure target as a standby replica\n" +
                                 "# 1. Take base backup from source\n" +
                                 "pg_basebackup -h $SOURCE_PG_HOST -U replication -D /var/lib/postgresql/data -Fp -Xs -P\n\n" +
                                 "# 2. Configure recovery.conf on target\n" +
                                 "# primary_conninfo = 'host=$SOURCE_PG_HOST user=replication'\n\n" +
                                 "# 3. Start target PostgreSQL in standby mode\n" +
                                 "# 4. When caught up, promote:\n" +
                                 "pg_ctl promote -D /var/lib/postgresql/data",
                        CausesDowntime = false,
                        EstimatedDuration = "15m"
                    });
                    break;

                case "fresh_db":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.Manual,
                        Description = "Fresh hub database — no data migration (hub state will be lost)",
                        Script = "# No migration needed — target PG starts with empty database\n" +
                                 "# WARNING: All hub state (users, instances, billing) will be lost",
                        CausesDowntime = false,
                        EstimatedDuration = "0s"
                    });
                    break;
            }
        }

        // Hub Redis migration
        var hubRedisRelocated = diff.ImageMatches.Any(m =>
            m.SourceImageKind == ImageKind.Redis &&
            !m.TargetIsFederation &&
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split);

        if (hubRedisRelocated && decisionMap.TryGetValue("hub-redis-migration", out var redisDecision))
        {
            switch (redisDecision.SelectedOptionKey)
            {
                case "rdb_snapshot":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.RedisSnapshot,
                        Description = "Create RDB snapshot of hub Redis",
                        Script = "# Trigger RDB save\n" +
                                 "redis-cli -h $SOURCE_REDIS_HOST BGSAVE\n\n" +
                                 "# Wait for completion\n" +
                                 "redis-cli -h $SOURCE_REDIS_HOST LASTSAVE",
                        CausesDowntime = false,
                        EstimatedDuration = "1m"
                    });
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.RedisRestore,
                        Description = "Restore RDB snapshot on new Redis host",
                        Script = "# Transfer RDB file\n" +
                                 "scp $SOURCE_REDIS_HOST:/data/dump.rdb $TARGET_REDIS_HOST:/data/\n\n" +
                                 "# Restart target Redis to load the dump\n" +
                                 "docker restart target-redis",
                        CausesDowntime = false,
                        EstimatedDuration = "2m"
                    });
                    break;

                case "fresh_instance":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.Manual,
                        Description = "Fresh hub Redis — no data migration (sessions will be lost, users must re-login)",
                        Script = "# No migration needed — target Redis starts empty\n" +
                                 "# Users will need to re-authenticate",
                        CausesDowntime = false,
                        EstimatedDuration = "0s"
                    });
                    break;
            }
        }

        // Federation is always fresh — add informational step
        var hasFedSplits = diff.ImageMatches.Any(m =>
            m.TargetIsFederation &&
            m.Kind is ImageMatchKind.Split or ImageMatchKind.Relocated);

        if (hasFedSplits)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.Manual,
                Description = "Federation instances — no data migration needed (hub provisions fresh instances at runtime)",
                Script = "# Federation PG/Redis/MinIO are always fresh\n" +
                         "# The hub will provision new federation instances after cutover\n" +
                         "# No data needs to be migrated for federation-tier services",
                CausesDowntime = false,
                EstimatedDuration = "0s"
            });
        }

        // Secret handling
        if (decisionMap.TryGetValue("secret-handling", out var secretDecision))
        {
            switch (secretDecision.SelectedOptionKey)
            {
                case "rotate":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.SecretGenerate,
                        Description = "Generate new secrets for all relocated infrastructure services",
                        Script = "# Terraform random_password resources will generate new credentials\n" +
                                 "# Application configs will be updated with new connection strings",
                        CausesDowntime = false,
                        EstimatedDuration = "1m"
                    });
                    break;

                case "preserve":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.SecretImport,
                        Description = "Import existing secrets from source Terraform state",
                        Script = "# Import secrets from source tfstate\n" +
                                 "terraform -chdir=target/ import random_password.pg_password <source-state-id>\n" +
                                 "terraform -chdir=target/ import random_password.redis_password <source-state-id>",
                        CausesDowntime = false,
                        EstimatedDuration = "2m"
                    });
                    break;
            }
        }

        return new MigrationPhase
        {
            Type = MigrationPhaseType.DataMigration,
            Name = "Data Migration",
            Description = "Migrate databases, caches, and secrets to new infrastructure",
            Steps = steps
        };
    }

    private static MigrationPhase GenerateProvisioningPhase(MigrationDiffResult diff, Topology target)
    {
        var steps = new List<MigrationStep>();
        var order = 1;

        // Docker install on new hosts
        var newHosts = diff.ContainerMatches
            .Where(c => c.Kind is ContainerMatchKind.Added or ContainerMatchKind.SplitHost)
            .Select(c => c.TargetContainerName)
            .Distinct()
            .ToList();

        if (newHosts.Count > 0)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.DockerInstall,
                Description = $"Install Docker on new hosts: {string.Join(", ", newHosts)}",
                Script = "# Provisioning script installs Docker and dependencies\n" +
                         "# This is handled by Terraform provisioner blocks in provisioning.tf",
                CausesDowntime = false,
                EstimatedDuration = $"{newHosts.Count * 2}m"
            });
        }

        // Start services on new hosts
        var addedImages = diff.ImageMatches
            .Where(m => m.Kind is ImageMatchKind.Added or ImageMatchKind.Relocated)
            .Where(m => !m.TargetIsFederation)
            .GroupBy(m => m.TargetHostName)
            .ToList();

        foreach (var hostGroup in addedImages)
        {
            var imageNames = string.Join(", ", hostGroup.Select(m => m.TargetImageName));
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.DockerRun,
                Description = $"Start services on {hostGroup.Key}: {imageNames}",
                Script = $"# Docker compose or run commands for {hostGroup.Key}\n" +
                         "# Handled by Terraform provisioner scripts",
                CausesDowntime = false,
                EstimatedDuration = "2m"
            });
        }

        return new MigrationPhase
        {
            Type = MigrationPhaseType.Provisioning,
            Name = "Service Provisioning",
            Description = "Install Docker and start services on new hosts",
            Steps = steps
        };
    }

    private static MigrationPhase GenerateCutoverPhase(
        MigrationDiffResult diff, List<MigrationDecision> decisions)
    {
        var steps = new List<MigrationStep>();
        var order = 1;

        var decisionMap = decisions.ToDictionary(d => d.Id);

        // DNS cutover
        if (decisionMap.TryGetValue("dns-cutover", out var dnsDecision))
        {
            switch (dnsDecision.SelectedOptionKey)
            {
                case "pre_point":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.DnsUpdate,
                        Description = "Update DNS A records to point to new proxy host",
                        Script = "# Update DNS records to new proxy host IP\n" +
                                 "# Both old and new hosts should be running during this transition\n" +
                                 "# Wait for DNS propagation (TTL-dependent)",
                        CausesDowntime = false,
                        EstimatedDuration = "5m"
                    });
                    break;

                case "post_cutover":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.DnsUpdate,
                        Description = "Update DNS records to new host IPs",
                        Script = "# Update A records after services are verified\n" +
                                 "# Brief gap during DNS propagation",
                        CausesDowntime = true,
                        EstimatedDuration = "10m"
                    });
                    break;

                case "manual":
                    steps.Add(new MigrationStep
                    {
                        Order = order++,
                        Type = MigrationStepType.Manual,
                        Description = "Manually update DNS records to point to new hosts",
                        Script = "# Update the following DNS records:\n" +
                                 "# A record: your-domain.com → <new-proxy-host-ip>\n" +
                                 "# Verify with: dig +short your-domain.com",
                        CausesDowntime = true,
                        EstimatedDuration = "varies"
                    });
                    break;
            }
        }

        // Caddy reload
        var hasCaddyChanges = diff.ImageMatches.Any(m =>
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Added &&
            (m.SourceImageKind is ImageKind.HubServer or ImageKind.FederationServer ||
             m.TargetImageKind is ImageKind.HubServer or ImageKind.FederationServer));

        if (hasCaddyChanges)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.CaddyReload,
                Description = "Reload Caddy configuration with new upstream addresses",
                Script = "# Caddyfile is regenerated by Terraform to reflect new service locations\n" +
                         "docker exec caddy caddy reload --config /etc/caddy/Caddyfile",
                CausesDowntime = false,
                EstimatedDuration = "30s"
            });
        }

        // Health checks
        steps.Add(new MigrationStep
        {
            Order = order++,
            Type = MigrationStepType.HealthCheck,
            Description = "Verify all services are healthy on new infrastructure",
            Script = "# Check health endpoints\n" +
                     "curl -sf https://your-domain.com/health || echo 'Hub health check failed'\n\n" +
                     "# Verify database connectivity\n" +
                     "pg_isready -h $TARGET_PG_HOST -U xcord\n\n" +
                     "# Verify Redis connectivity\n" +
                     "redis-cli -h $TARGET_REDIS_HOST ping",
            CausesDowntime = false,
            EstimatedDuration = "2m"
        });

        return new MigrationPhase
        {
            Type = MigrationPhaseType.Cutover,
            Name = "Cutover",
            Description = "Switch traffic to new infrastructure and verify health",
            Steps = steps
        };
    }

    private static MigrationPhase GenerateCleanupPhase(MigrationDiffResult diff)
    {
        var steps = new List<MigrationStep>();
        var order = 1;

        var removedHosts = diff.ContainerMatches
            .Where(c => c.Kind == ContainerMatchKind.Removed)
            .ToList();

        // Also count source hosts that were split (the original host can be decommissioned)
        var splitSourceHosts = diff.ContainerMatches
            .Where(c => c.Kind == ContainerMatchKind.SplitHost)
            .Select(c => c.SourceContainerId)
            .Distinct()
            .ToList();

        var hostsToDestroy = removedHosts.Count + splitSourceHosts.Count;

        if (hostsToDestroy > 0)
        {
            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.Manual,
                Description = "Verify migration is successful before destroying old infrastructure",
                Script = "# IMPORTANT: Verify everything works before proceeding!\n" +
                         "# Run smoke tests against the new infrastructure\n" +
                         "# Check logs for errors\n" +
                         "# Monitor for at least 1 hour before cleanup",
                CausesDowntime = false,
                EstimatedDuration = "1h"
            });

            steps.Add(new MigrationStep
            {
                Order = order++,
                Type = MigrationStepType.TerraformDestroy,
                Description = $"Destroy old infrastructure ({hostsToDestroy} host(s))",
                Script = "# Destroy old hosts after confirming migration success\n" +
                         "terraform -chdir=source/ destroy -auto-approve\n\n" +
                         "# Or selectively destroy specific resources:\n" +
                         "# terraform -chdir=source/ destroy -target=linode_instance.host",
                CausesDowntime = false,
                EstimatedDuration = "5m"
            });
        }

        return new MigrationPhase
        {
            Type = MigrationPhaseType.Cleanup,
            Name = "Cleanup",
            Description = "Decommission old infrastructure after successful migration",
            Steps = steps
        };
    }
}
