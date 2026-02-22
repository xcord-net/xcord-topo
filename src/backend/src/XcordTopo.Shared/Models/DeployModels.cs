namespace XcordTopo.Models;

public sealed class CredentialStatus
{
    public bool HasCredentials { get; set; }
    public List<string> SetVariables { get; set; } = [];
    public Dictionary<string, string> NonSensitiveValues { get; set; } = new();
}

public sealed class DeployedTopology
{
    public Guid TopologyId { get; set; }
    public string TopologyName { get; set; } = string.Empty;
    public bool HasState { get; set; }
    public int ResourceCount { get; set; }
}
