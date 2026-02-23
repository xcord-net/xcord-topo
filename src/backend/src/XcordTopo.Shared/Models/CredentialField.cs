namespace XcordTopo.Models;

public sealed class CredentialField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text"; // "password" | "text" | "select" | "textarea"
    public bool Sensitive { get; set; }
    public bool Required { get; set; } = true;
    public string? Placeholder { get; set; }
    public CredentialFieldHelp? Help { get; set; }
}

public sealed class CredentialFieldHelp
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = [];
    public string? Url { get; set; }
    public string? Permissions { get; set; }
}
