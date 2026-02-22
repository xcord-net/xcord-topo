using System.Text.Json.Serialization;

namespace XcordTopo.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TerraformCommand
{
    Init,
    Plan,
    Apply,
    Destroy
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TerraformExecutionStatus
{
    Idle,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class TerraformExecution
{
    public TerraformCommand Command { get; set; }
    public TerraformExecutionStatus Status { get; set; } = TerraformExecutionStatus.Idle;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? ExitCode { get; set; }
}

public sealed class TerraformOutputLine
{
    public string Text { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
