using XcordTopo.Infrastructure.Migration;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class MigrationPlanGeneratorTests
{
    private readonly MigrationPlanGenerator _generator = new();

    private static Topology SimpleSource() => new() { Name = "Simple", Containers = [new Container { Name = "server", Kind = ContainerKind.Host, Width = 300, Height = 200 }] };
    private static Topology RobustTarget() => new() { Name = "Robust", Containers = [new Container { Name = "hub-host", Kind = ContainerKind.Host, Width = 300, Height = 200 }] };

    private static MigrationDiffResult BuildDiffWithRelocatedHubPg()
    {
        return new MigrationDiffResult
        {
            Summary = "test diff",
            HostsAdded = 3,
            ImagesRelocated = 2,
            SplitsDetected = 2,
            ImagesAdded = 1,
            ImageMatches =
            [
                new ImageMatch
                {
                    SourceImageKind = ImageKind.HubServer, TargetImageKind = ImageKind.HubServer,
                    SourceHostName = "server", TargetHostName = "hub-host",
                    Kind = ImageMatchKind.Relocated, TargetIsFederation = false
                },
                new ImageMatch
                {
                    SourceImageKind = ImageKind.PostgreSQL, TargetImageKind = ImageKind.PostgreSQL,
                    SourceHostName = "server", TargetHostName = "hub-host",
                    Kind = ImageMatchKind.Split, TargetIsFederation = false
                },
                new ImageMatch
                {
                    SourceImageKind = ImageKind.PostgreSQL, TargetImageKind = ImageKind.PostgreSQL,
                    SourceHostName = "server", TargetHostName = "fed-host",
                    Kind = ImageMatchKind.Split, TargetIsFederation = true
                },
                new ImageMatch
                {
                    SourceImageKind = ImageKind.Redis, TargetImageKind = ImageKind.Redis,
                    SourceHostName = "server", TargetHostName = "hub-host",
                    Kind = ImageMatchKind.Split, TargetIsFederation = false
                },
                new ImageMatch
                {
                    SourceImageKind = ImageKind.Redis, TargetImageKind = ImageKind.Redis,
                    SourceHostName = "server", TargetHostName = "fed-host",
                    Kind = ImageMatchKind.Split, TargetIsFederation = true
                },
                new ImageMatch
                {
                    TargetImageKind = ImageKind.LiveKit, TargetHostName = "media-host",
                    Kind = ImageMatchKind.Added
                }
            ],
            ContainerMatches =
            [
                new ContainerMatch
                {
                    SourceContainerName = "server",
                    TargetContainerName = "hub-host",
                    Kind = ContainerMatchKind.SplitHost
                },
                new ContainerMatch
                {
                    TargetContainerName = "media-host",
                    Kind = ContainerMatchKind.Added
                },
                new ContainerMatch
                {
                    TargetContainerName = "proxy-host",
                    Kind = ContainerMatchKind.Added
                }
            ]
        };
    }

    [Fact]
    public void Generate_HasAllSixPhaseTypes()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "pg_dump_restore" },
            new() { Id = "hub-redis-migration", Kind = DecisionKind.HubRedisMigration, SelectedOptionKey = "fresh_instance" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "post_cutover" },
            new() { Id = "downtime-tolerance", Kind = DecisionKind.DowntimeTolerance, SelectedOptionKey = "maintenance_window" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var phaseTypes = plan.Phases.Select(p => p.Type).ToList();
        Assert.Contains(MigrationPhaseType.PreCheck, phaseTypes);
        Assert.Contains(MigrationPhaseType.Infrastructure, phaseTypes);
        Assert.Contains(MigrationPhaseType.DataMigration, phaseTypes);
        Assert.Contains(MigrationPhaseType.Provisioning, phaseTypes);
        Assert.Contains(MigrationPhaseType.Cutover, phaseTypes);
        Assert.Contains(MigrationPhaseType.Cleanup, phaseTypes);
    }

    [Fact]
    public void Generate_PhasesAreInCorrectOrder()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "pg_dump_restore" },
            new() { Id = "hub-redis-migration", Kind = DecisionKind.HubRedisMigration, SelectedOptionKey = "rdb_snapshot" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "post_cutover" },
            new() { Id = "downtime-tolerance", Kind = DecisionKind.DowntimeTolerance, SelectedOptionKey = "brief_outage" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var phaseTypes = plan.Phases.Select(p => p.Type).ToList();
        var preCheckIdx = phaseTypes.IndexOf(MigrationPhaseType.PreCheck);
        var infraIdx = phaseTypes.IndexOf(MigrationPhaseType.Infrastructure);
        var dataIdx = phaseTypes.IndexOf(MigrationPhaseType.DataMigration);
        var provIdx = phaseTypes.IndexOf(MigrationPhaseType.Provisioning);
        var cutoverIdx = phaseTypes.IndexOf(MigrationPhaseType.Cutover);
        var cleanupIdx = phaseTypes.IndexOf(MigrationPhaseType.Cleanup);

        Assert.True(preCheckIdx < infraIdx);
        Assert.True(infraIdx < dataIdx);
        Assert.True(dataIdx < provIdx);
        Assert.True(provIdx < cutoverIdx);
        Assert.True(cutoverIdx < cleanupIdx);
    }

    [Fact]
    public void Generate_PgDumpRestore_HasDumpAndRestoreSteps()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "pg_dump_restore" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "manual" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "downtime-tolerance", Kind = DecisionKind.DowntimeTolerance, SelectedOptionKey = "brief_outage" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var dataPhase = plan.Phases.First(p => p.Type == MigrationPhaseType.DataMigration);
        Assert.Contains(dataPhase.Steps, s => s.Type == MigrationStepType.DatabaseDump);
        Assert.Contains(dataPhase.Steps, s => s.Type == MigrationStepType.DatabaseRestore);
    }

    [Fact]
    public void Generate_FreshDb_NoDowntimeDataStep()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "fresh_db" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "manual" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "downtime-tolerance", Kind = DecisionKind.DowntimeTolerance, SelectedOptionKey = "brief_outage" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var dataPhase = plan.Phases.First(p => p.Type == MigrationPhaseType.DataMigration);
        Assert.DoesNotContain(dataPhase.Steps, s => s.Type == MigrationStepType.DatabaseDump);
    }

    [Fact]
    public void Generate_FederationIsAlwaysFresh_InformationalStep()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "pg_dump_restore" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "manual" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "downtime-tolerance", Kind = DecisionKind.DowntimeTolerance, SelectedOptionKey = "brief_outage" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var dataPhase = plan.Phases.First(p => p.Type == MigrationPhaseType.DataMigration);
        Assert.Contains(dataPhase.Steps, s =>
            s.Description.Contains("Federation") &&
            s.Description.Contains("no data migration"));
    }

    [Fact]
    public void Generate_PreCheckPhase_ValidatesBothTopologies()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, []);

        var preCheck = plan.Phases.First(p => p.Type == MigrationPhaseType.PreCheck);
        Assert.Equal(2, preCheck.Steps.Count);
        Assert.All(preCheck.Steps, s => Assert.Equal(MigrationStepType.Validate, s.Type));
    }

    [Fact]
    public void Generate_CleanupPhase_HasDestroyStep()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, []);

        var cleanup = plan.Phases.First(p => p.Type == MigrationPhaseType.Cleanup);
        Assert.Contains(cleanup.Steps, s => s.Type == MigrationStepType.TerraformDestroy);
    }

    [Fact]
    public void Generate_SetsTopologyMetadata()
    {
        var source = SimpleSource();
        var target = RobustTarget();
        var diff = new MigrationDiffResult();

        var plan = _generator.Generate(source, target, diff, []);

        Assert.Equal(source.Id, plan.SourceTopologyId);
        Assert.Equal("Simple", plan.SourceTopologyName);
        Assert.Equal(target.Id, plan.TargetTopologyId);
        Assert.Equal("Robust", plan.TargetTopologyName);
    }

    [Fact]
    public void Generate_EmptyDiff_HasPreCheckAndCutoverOnly()
    {
        var diff = new MigrationDiffResult();
        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, []);

        // With empty diff: PreCheck (2 validate steps) + Cutover (always has health check)
        Assert.Equal(2, plan.Phases.Count);
        Assert.Equal(MigrationPhaseType.PreCheck, plan.Phases[0].Type);
        Assert.Equal(MigrationPhaseType.Cutover, plan.Phases[1].Type);
    }

    [Fact]
    public void Generate_CutoverPhase_HasHealthCheck()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "manual" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var cutover = plan.Phases.First(p => p.Type == MigrationPhaseType.Cutover);
        Assert.Contains(cutover.Steps, s => s.Type == MigrationStepType.HealthCheck);
    }

    [Fact]
    public void Generate_SecretRotation_HasSecretGenerateStep()
    {
        var diff = BuildDiffWithRelocatedHubPg();
        var decisions = new List<MigrationDecision>
        {
            new() { Id = "hub-db-migration", Kind = DecisionKind.HubDatabaseMigration, SelectedOptionKey = "fresh_db" },
            new() { Id = "secret-handling", Kind = DecisionKind.SecretHandling, SelectedOptionKey = "rotate" },
            new() { Id = "dns-cutover", Kind = DecisionKind.DnsCutover, SelectedOptionKey = "manual" }
        };

        var plan = _generator.Generate(SimpleSource(), RobustTarget(), diff, decisions);

        var dataPhase = plan.Phases.First(p => p.Type == MigrationPhaseType.DataMigration);
        Assert.Contains(dataPhase.Steps, s => s.Type == MigrationStepType.SecretGenerate);
    }
}
