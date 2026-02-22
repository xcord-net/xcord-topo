using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Migration;

/// <summary>
/// Flattened image with its host context for matching.
/// </summary>
public sealed record FlatImage(
    Image Image,
    Container Host,
    Container? FederationGroup,
    string HostPath
);

/// <summary>
/// 2-pass topology matcher: images first (semantic by Kind), then containers derived from image matches.
/// </summary>
public sealed class TopologyMatcher
{
    /// <summary>
    /// Compute the full diff between source and target topologies.
    /// </summary>
    public MigrationDiffResult Match(Topology source, Topology target)
    {
        var sourceImages = FlattenImages(source);
        var targetImages = FlattenImages(target);

        var sourceResolver = new WireResolver(source);
        var targetResolver = new WireResolver(target);

        // Pass 1: Image matching
        var imageMatches = MatchImages(sourceImages, targetImages, sourceResolver, targetResolver);

        // Pass 2: Container matching derived from image matches
        var containerMatches = DeriveContainerMatches(source, target, imageMatches);

        // Generate decisions based on matches
        var decisions = GenerateDecisions(imageMatches, sourceImages, targetImages);

        // Build summary
        var hostsAdded = containerMatches.Count(c => c.Kind == ContainerMatchKind.Added);
        var hostsRemoved = containerMatches.Count(c => c.Kind == ContainerMatchKind.Removed);
        var imagesRelocated = imageMatches.Count(m => m.Kind == ImageMatchKind.Relocated);
        var imagesAdded = imageMatches.Count(m => m.Kind == ImageMatchKind.Added);
        var imagesRemoved = imageMatches.Count(m => m.Kind == ImageMatchKind.Removed);
        var splitsDetected = imageMatches.Count(m => m.Kind == ImageMatchKind.Split);

        var summaryParts = new List<string>();
        if (hostsAdded > 0) summaryParts.Add($"{hostsAdded} host(s) added");
        if (hostsRemoved > 0) summaryParts.Add($"{hostsRemoved} host(s) removed");
        if (imagesRelocated > 0) summaryParts.Add($"{imagesRelocated} image(s) relocated");
        if (splitsDetected > 0) summaryParts.Add($"{splitsDetected} split(s) detected");
        if (imagesAdded > 0) summaryParts.Add($"{imagesAdded} new image(s)");
        if (imagesRemoved > 0) summaryParts.Add($"{imagesRemoved} image(s) removed");

        return new MigrationDiffResult
        {
            Summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "No changes detected",
            HostsAdded = hostsAdded,
            HostsRemoved = hostsRemoved,
            ImagesRelocated = imagesRelocated,
            ImagesAdded = imagesAdded,
            ImagesRemoved = imagesRemoved,
            SplitsDetected = splitsDetected,
            ImageMatches = imageMatches,
            ContainerMatches = containerMatches,
            Decisions = decisions
        };
    }

    /// <summary>
    /// Flatten all images from a topology with their host context.
    /// Recursively walks containers, tracking the nearest Host ancestor and any FederationGroup ancestor.
    /// </summary>
    public static List<FlatImage> FlattenImages(Topology topology)
    {
        var result = new List<FlatImage>();
        FlattenContainers(topology.Containers, null, null, "", result);
        return result;
    }

    private static void FlattenContainers(
        List<Container> containers, Container? hostAncestor, Container? fedGroupAncestor,
        string pathPrefix, List<FlatImage> result)
    {
        foreach (var container in containers)
        {
            var currentHost = container.Kind == ContainerKind.Host ? container : hostAncestor;
            var currentFedGroup = container.Kind == ContainerKind.FederationGroup ? container : fedGroupAncestor;
            var currentPath = string.IsNullOrEmpty(pathPrefix) ? container.Name : $"{pathPrefix}/{container.Name}";

            foreach (var image in container.Images)
            {
                if (currentHost != null)
                {
                    result.Add(new FlatImage(image, currentHost, currentFedGroup, currentPath));
                }
            }

            FlattenContainers(container.Children, currentHost, currentFedGroup, currentPath, result);
        }
    }

    /// <summary>
    /// Pass 1: Match images by (Kind, Name). Detect relocations, splits, additions, removals.
    /// </summary>
    private static List<ImageMatch> MatchImages(
        List<FlatImage> sourceImages, List<FlatImage> targetImages,
        WireResolver sourceResolver, WireResolver targetResolver)
    {
        var matches = new List<ImageMatch>();
        var matchedSourceIds = new HashSet<Guid>();
        var matchedTargetIds = new HashSet<Guid>();

        // Group by Kind for matching
        var sourceByKind = sourceImages.GroupBy(i => i.Image.Kind).ToDictionary(g => g.Key, g => g.ToList());
        var targetByKind = targetImages.GroupBy(i => i.Image.Kind).ToDictionary(g => g.Key, g => g.ToList());

        var allKinds = sourceByKind.Keys.Union(targetByKind.Keys).ToList();

        foreach (var kind in allKinds)
        {
            var sources = sourceByKind.GetValueOrDefault(kind, []);
            var targets = targetByKind.GetValueOrDefault(kind, []);

            if (sources.Count == 0)
            {
                // All targets are Added
                foreach (var t in targets)
                {
                    matchedTargetIds.Add(t.Image.Id);
                    matches.Add(CreateAddedMatch(t));
                }
                continue;
            }

            if (targets.Count == 0)
            {
                // All sources are Removed
                foreach (var s in sources)
                {
                    matchedSourceIds.Add(s.Image.Id);
                    matches.Add(CreateRemovedMatch(s));
                }
                continue;
            }

            if (sources.Count == 1 && targets.Count == 1)
            {
                // 1:1 match
                var s = sources[0];
                var t = targets[0];
                matchedSourceIds.Add(s.Image.Id);
                matchedTargetIds.Add(t.Image.Id);
                matches.Add(CreateOneToOneMatch(s, t));
            }
            else if (sources.Count == 1 && targets.Count > 1)
            {
                // 1:N split — use wire consumer analysis
                var s = sources[0];
                matchedSourceIds.Add(s.Image.Id);

                var splitMatches = ResolveSplit(s, targets, sourceResolver, targetResolver);
                foreach (var m in splitMatches)
                {
                    if (m.TargetImageId.HasValue)
                        matchedTargetIds.Add(m.TargetImageId.Value);
                }
                matches.AddRange(splitMatches);
            }
            else
            {
                // N:M — match by name first, then by position
                MatchByName(sources, targets, matches, matchedSourceIds, matchedTargetIds);
            }
        }

        return matches;
    }

    /// <summary>
    /// For N:M cases, match images by name within the same kind group.
    /// Remaining unmatched sources become Removed, unmatched targets become Added.
    /// </summary>
    private static void MatchByName(
        List<FlatImage> sources, List<FlatImage> targets,
        List<ImageMatch> matches, HashSet<Guid> matchedSourceIds, HashSet<Guid> matchedTargetIds)
    {
        var remainingSources = new List<FlatImage>(sources);
        var remainingTargets = new List<FlatImage>(targets);

        // Try exact name match
        for (var i = remainingSources.Count - 1; i >= 0; i--)
        {
            var s = remainingSources[i];
            var targetIdx = remainingTargets.FindIndex(t => t.Image.Name == s.Image.Name);
            if (targetIdx >= 0)
            {
                var t = remainingTargets[targetIdx];
                matchedSourceIds.Add(s.Image.Id);
                matchedTargetIds.Add(t.Image.Id);
                matches.Add(CreateOneToOneMatch(s, t));
                remainingSources.RemoveAt(i);
                remainingTargets.RemoveAt(targetIdx);
            }
        }

        // Remaining sources are Removed
        foreach (var s in remainingSources)
        {
            matchedSourceIds.Add(s.Image.Id);
            matches.Add(CreateRemovedMatch(s));
        }

        // Remaining targets are Added
        foreach (var t in remainingTargets)
        {
            matchedTargetIds.Add(t.Image.Id);
            matches.Add(CreateAddedMatch(t));
        }
    }

    /// <summary>
    /// Resolve a 1:N split using wire consumer analysis.
    /// Source image had N consumers; each consumer maps to a different target image.
    /// </summary>
    private static List<ImageMatch> ResolveSplit(
        FlatImage source, List<FlatImage> targets,
        WireResolver sourceResolver, WireResolver targetResolver)
    {
        var matches = new List<ImageMatch>();
        var matchedTargets = new HashSet<Guid>();

        // Find all consumers of the source image (images that wire INTO the source)
        var sourceConsumers = FindConsumers(source.Image, sourceResolver);

        foreach (var consumer in sourceConsumers)
        {
            // Find where this consumer ended up in the target topology by Kind+Name
            var consumerKind = consumer.Kind;

            // For each target, check if a consumer of the same kind is wired to it
            foreach (var t in targets)
            {
                if (matchedTargets.Contains(t.Image.Id)) continue;

                var targetConsumers = FindConsumers(t.Image, targetResolver);
                if (targetConsumers.Any(tc => tc.Kind == consumerKind))
                {
                    matchedTargets.Add(t.Image.Id);
                    matches.Add(new ImageMatch
                    {
                        SourceImageId = source.Image.Id,
                        SourceImageName = source.Image.Name,
                        SourceImageKind = source.Image.Kind,
                        SourceHostId = source.Host.Id,
                        SourceHostName = source.Host.Name,
                        TargetImageId = t.Image.Id,
                        TargetImageName = t.Image.Name,
                        TargetImageKind = t.Image.Kind,
                        TargetHostId = t.Host.Id,
                        TargetHostName = t.Host.Name,
                        Kind = ImageMatchKind.Split,
                        SplitConsumerId = consumer.Id,
                        TargetIsFederation = t.FederationGroup != null
                    });
                    break;
                }
            }
        }

        // Any target not matched by consumer analysis — match by position or mark as Added
        foreach (var t in targets)
        {
            if (matchedTargets.Contains(t.Image.Id)) continue;
            matchedTargets.Add(t.Image.Id);
            matches.Add(new ImageMatch
            {
                SourceImageId = source.Image.Id,
                SourceImageName = source.Image.Name,
                SourceImageKind = source.Image.Kind,
                SourceHostId = source.Host.Id,
                SourceHostName = source.Host.Name,
                TargetImageId = t.Image.Id,
                TargetImageName = t.Image.Name,
                TargetImageKind = t.Image.Kind,
                TargetHostId = t.Host.Id,
                TargetHostName = t.Host.Name,
                Kind = ImageMatchKind.Split,
                TargetIsFederation = t.FederationGroup != null
            });
        }

        return matches;
    }

    /// <summary>
    /// Find all images that wire INTO a given image (its consumers).
    /// These are images whose output ports connect to the given image's input ports.
    /// </summary>
    private static List<Image> FindConsumers(Image image, WireResolver resolver)
    {
        var consumers = new List<Image>();
        foreach (var port in image.Ports.Where(p => p.Direction is PortDirection.In or PortDirection.InOut))
        {
            var incoming = resolver.ResolveIncoming(image.Id, port.Name);
            foreach (var (node, _) in incoming)
            {
                if (node is Image img && img.Id != image.Id)
                    consumers.Add(img);
            }
        }
        return consumers;
    }

    private static ImageMatch CreateOneToOneMatch(FlatImage source, FlatImage target)
    {
        var kind = ImageMatchKind.Unchanged;
        if (source.Host.Id != target.Host.Id)
            kind = ImageMatchKind.Relocated;
        else if (HasConfigDiff(source.Image, target.Image))
            kind = ImageMatchKind.Modified;

        return new ImageMatch
        {
            SourceImageId = source.Image.Id,
            SourceImageName = source.Image.Name,
            SourceImageKind = source.Image.Kind,
            SourceHostId = source.Host.Id,
            SourceHostName = source.Host.Name,
            TargetImageId = target.Image.Id,
            TargetImageName = target.Image.Name,
            TargetImageKind = target.Image.Kind,
            TargetHostId = target.Host.Id,
            TargetHostName = target.Host.Name,
            Kind = kind,
            TargetIsFederation = target.FederationGroup != null
        };
    }

    private static ImageMatch CreateAddedMatch(FlatImage target) => new()
    {
        TargetImageId = target.Image.Id,
        TargetImageName = target.Image.Name,
        TargetImageKind = target.Image.Kind,
        TargetHostId = target.Host.Id,
        TargetHostName = target.Host.Name,
        Kind = ImageMatchKind.Added,
        TargetIsFederation = target.FederationGroup != null
    };

    private static ImageMatch CreateRemovedMatch(FlatImage source) => new()
    {
        SourceImageId = source.Image.Id,
        SourceImageName = source.Image.Name,
        SourceImageKind = source.Image.Kind,
        SourceHostId = source.Host.Id,
        SourceHostName = source.Host.Name,
        Kind = ImageMatchKind.Removed
    };

    private static bool HasConfigDiff(Image a, Image b)
    {
        if (a.Config.Count != b.Config.Count) return true;
        foreach (var (key, value) in a.Config)
        {
            if (!b.Config.TryGetValue(key, out var otherValue) || value != otherValue)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Pass 2: Derive container matches from image matches.
    /// A container is Matched if it contains at least one matched image pair.
    /// </summary>
    private static List<ContainerMatch> DeriveContainerMatches(
        Topology source, Topology target, List<ImageMatch> imageMatches)
    {
        var matches = new List<ContainerMatch>();

        var sourceHosts = CollectHosts(source);
        var targetHosts = CollectHosts(target);

        // Track which source/target hosts are touched by image matches
        var sourceHostToTargetHosts = new Dictionary<Guid, HashSet<Guid>>();
        var targetHostToSourceHosts = new Dictionary<Guid, HashSet<Guid>>();
        var hostMatchedImageIds = new Dictionary<(Guid, Guid), List<Guid>>();

        foreach (var im in imageMatches)
        {
            if (im.SourceHostId.HasValue && im.TargetHostId.HasValue)
            {
                var sId = im.SourceHostId.Value;
                var tId = im.TargetHostId.Value;

                if (!sourceHostToTargetHosts.ContainsKey(sId))
                    sourceHostToTargetHosts[sId] = [];
                sourceHostToTargetHosts[sId].Add(tId);

                if (!targetHostToSourceHosts.ContainsKey(tId))
                    targetHostToSourceHosts[tId] = [];
                targetHostToSourceHosts[tId].Add(sId);

                var key = (sId, tId);
                if (!hostMatchedImageIds.ContainsKey(key))
                    hostMatchedImageIds[key] = [];
                if (im.TargetImageId.HasValue)
                    hostMatchedImageIds[key].Add(im.TargetImageId.Value);
            }
        }

        var processedSourceHosts = new HashSet<Guid>();
        var processedTargetHosts = new HashSet<Guid>();

        // Source hosts that map to multiple target hosts → SplitHost
        foreach (var (sourceHostId, targetHostIds) in sourceHostToTargetHosts)
        {
            var sourceHost = sourceHosts.FirstOrDefault(h => h.Id == sourceHostId);
            if (sourceHost == null) continue;

            processedSourceHosts.Add(sourceHostId);

            if (targetHostIds.Count > 1)
            {
                // Split host: one source distributed across multiple targets
                foreach (var targetHostId in targetHostIds)
                {
                    processedTargetHosts.Add(targetHostId);
                    var targetHost = targetHosts.FirstOrDefault(h => h.Id == targetHostId);
                    matches.Add(new ContainerMatch
                    {
                        SourceContainerId = sourceHostId,
                        SourceContainerName = sourceHost.Name,
                        TargetContainerId = targetHostId,
                        TargetContainerName = targetHost?.Name,
                        Kind = ContainerMatchKind.SplitHost,
                        MatchedImageIds = hostMatchedImageIds.GetValueOrDefault((sourceHostId, targetHostId), [])
                    });
                }
            }
            else
            {
                var targetHostId = targetHostIds.First();
                processedTargetHosts.Add(targetHostId);
                var targetHost = targetHosts.FirstOrDefault(h => h.Id == targetHostId);
                matches.Add(new ContainerMatch
                {
                    SourceContainerId = sourceHostId,
                    SourceContainerName = sourceHost.Name,
                    TargetContainerId = targetHostId,
                    TargetContainerName = targetHost?.Name,
                    Kind = ContainerMatchKind.Matched,
                    MatchedImageIds = hostMatchedImageIds.GetValueOrDefault((sourceHostId, targetHostId), [])
                });
            }
        }

        // Source hosts with no target counterpart → Removed
        foreach (var host in sourceHosts)
        {
            if (!processedSourceHosts.Contains(host.Id))
            {
                matches.Add(new ContainerMatch
                {
                    SourceContainerId = host.Id,
                    SourceContainerName = host.Name,
                    Kind = ContainerMatchKind.Removed
                });
            }
        }

        // Target hosts with no source counterpart → Added
        foreach (var host in targetHosts)
        {
            if (!processedTargetHosts.Contains(host.Id))
            {
                matches.Add(new ContainerMatch
                {
                    TargetContainerId = host.Id,
                    TargetContainerName = host.Name,
                    Kind = ContainerMatchKind.Added
                });
            }
        }

        return matches;
    }

    /// <summary>
    /// Collect all Host-kind containers from a topology (recursively).
    /// </summary>
    private static List<Container> CollectHosts(Topology topology)
    {
        var hosts = new List<Container>();
        CollectHostsRecursive(topology.Containers, hosts);
        return hosts;
    }

    private static void CollectHostsRecursive(List<Container> containers, List<Container> hosts)
    {
        foreach (var container in containers)
        {
            if (container.Kind == ContainerKind.Host)
                hosts.Add(container);
            CollectHostsRecursive(container.Children, hosts);
        }
    }

    /// <summary>
    /// Generate user decisions based on detected matches.
    /// </summary>
    private static List<MigrationDecision> GenerateDecisions(
        List<ImageMatch> imageMatches, List<FlatImage> sourceImages, List<FlatImage> targetImages)
    {
        var decisions = new List<MigrationDecision>();

        // Check for hub database migration (PG relocated or split, non-federation target)
        var hubPgMatches = imageMatches.Where(m =>
            m.SourceImageKind == ImageKind.PostgreSQL &&
            !m.TargetIsFederation &&
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split).ToList();

        if (hubPgMatches.Count > 0)
        {
            decisions.Add(new MigrationDecision
            {
                Id = "hub-db-migration",
                Kind = DecisionKind.HubDatabaseMigration,
                Title = "Hub Database Migration Strategy",
                Description = "The hub's PostgreSQL database is moving to a new host. How should data be migrated?",
                Required = true,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "pg_dump_restore",
                        Label = "pg_dump + psql restore",
                        Description = "Brief downtime while dumping and restoring. Simple and reliable."
                    },
                    new DecisionOption
                    {
                        Key = "streaming_replication",
                        Label = "Streaming replication then promote",
                        Description = "Near-zero downtime. Set up replication, then promote the new server."
                    },
                    new DecisionOption
                    {
                        Key = "fresh_db",
                        Label = "Fresh database",
                        Description = "Start with an empty database. All hub state (users, instances) will be lost."
                    }
                ]
            });
        }

        // Check for hub Redis migration
        var hubRedisMatches = imageMatches.Where(m =>
            m.SourceImageKind == ImageKind.Redis &&
            !m.TargetIsFederation &&
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split).ToList();

        if (hubRedisMatches.Count > 0)
        {
            decisions.Add(new MigrationDecision
            {
                Id = "hub-redis-migration",
                Kind = DecisionKind.HubRedisMigration,
                Title = "Hub Redis Migration Strategy",
                Description = "The hub's Redis instance is moving. How should data be migrated?",
                Required = false,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "rdb_snapshot",
                        Label = "RDB snapshot transfer",
                        Description = "Copy RDB file to the new host. Brief interruption."
                    },
                    new DecisionOption
                    {
                        Key = "fresh_instance",
                        Label = "Fresh instance",
                        Description = "Start fresh. Sessions will be lost (users must re-login). Usually acceptable."
                    }
                ]
            });
        }

        // Secret handling if any infrastructure moves hosts
        var hasRelocations = imageMatches.Any(m =>
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split &&
            m.SourceImageKind is ImageKind.PostgreSQL or ImageKind.Redis or ImageKind.MinIO);

        if (hasRelocations)
        {
            decisions.Add(new MigrationDecision
            {
                Id = "secret-handling",
                Kind = DecisionKind.SecretHandling,
                Title = "Secret Handling",
                Description = "Infrastructure services are moving to new hosts. How should secrets (passwords, keys) be handled?",
                Required = true,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "rotate",
                        Label = "Rotate all secrets",
                        Description = "Generate new passwords and keys for all services. Most secure."
                    },
                    new DecisionOption
                    {
                        Key = "preserve",
                        Label = "Preserve existing secrets",
                        Description = "Import secrets from current Terraform state. Simpler migration."
                    }
                ]
            });
        }

        // DNS cutover if external-facing services move to different hosts
        var proxyRelocated = imageMatches.Any(m =>
            m.Kind is ImageMatchKind.Relocated or ImageMatchKind.Split &&
            m.SourceHostName != m.TargetHostName &&
            m.SourceImageKind is ImageKind.HubServer or ImageKind.FederationServer);

        if (proxyRelocated)
        {
            decisions.Add(new MigrationDecision
            {
                Id = "dns-cutover",
                Kind = DecisionKind.DnsCutover,
                Title = "DNS Cutover Strategy",
                Description = "Services are moving to new hosts with new IPs. How should DNS be updated?",
                Required = true,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "pre_point",
                        Label = "Pre-point DNS (blue-green)",
                        Description = "Point DNS to new hosts before cutover. Requires both old and new running simultaneously."
                    },
                    new DecisionOption
                    {
                        Key = "post_cutover",
                        Label = "Post-cutover update",
                        Description = "Update DNS records after migration completes. Brief DNS propagation delay."
                    },
                    new DecisionOption
                    {
                        Key = "manual",
                        Label = "Manual DNS update",
                        Description = "Provide instructions but don't automate DNS changes."
                    }
                ]
            });
        }

        // Downtime tolerance if any data migration is needed
        var hasDataMigration = hubPgMatches.Count > 0 || hubRedisMatches.Count > 0;
        if (hasDataMigration)
        {
            decisions.Add(new MigrationDecision
            {
                Id = "downtime-tolerance",
                Kind = DecisionKind.DowntimeTolerance,
                Title = "Downtime Tolerance",
                Description = "Data migration steps may require downtime. What is your tolerance?",
                Required = true,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "maintenance_window",
                        Label = "Maintenance window",
                        Description = "Schedule a maintenance window. All services stop during migration."
                    },
                    new DecisionOption
                    {
                        Key = "rolling_update",
                        Label = "Rolling update",
                        Description = "Migrate services one at a time. Partial availability during migration."
                    },
                    new DecisionOption
                    {
                        Key = "brief_outage",
                        Label = "Accept brief outage",
                        Description = "Migrate as fast as possible, accepting a brief outage during cutover."
                    }
                ]
            });
        }

        // Variable values — check target topology for $VAR references
        var varReferences = new HashSet<string>();
        foreach (var fi in targetImages)
        {
            foreach (var (_, value) in fi.Image.Config)
            {
                if (value.StartsWith('$'))
                    varReferences.Add(value);
            }
            foreach (var (_, value) in fi.Host.Config)
            {
                if (value.StartsWith('$'))
                    varReferences.Add(value);
            }
        }

        foreach (var varRef in varReferences)
        {
            decisions.Add(new MigrationDecision
            {
                Id = $"var-{varRef.TrimStart('$').ToLowerInvariant()}",
                Kind = DecisionKind.VariableValue,
                Title = $"Variable: {varRef}",
                Description = $"The target topology references {varRef}. Please provide a concrete value.",
                Required = true,
                Options =
                [
                    new DecisionOption
                    {
                        Key = "custom",
                        Label = "Enter value",
                        Description = $"Provide a concrete value for {varRef}"
                    }
                ]
            });
        }

        return decisions;
    }
}
