using System.Text;

namespace XcordTopo.Infrastructure.Terraform;

public sealed class HclBuilder
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public HclBuilder Line(string text = "")
    {
        if (string.IsNullOrEmpty(text))
            _sb.AppendLine();
        else
            _sb.AppendLine($"{new string(' ', _indent * 2)}{text}");
        return this;
    }

    public HclBuilder Block(string header, Action<HclBuilder> body)
    {
        Line($"{header} {{");
        _indent++;
        body(this);
        _indent--;
        Line("}");
        return this;
    }

    /// <summary>
    /// Escapes a value for use inside a quoted HCL string literal: backslashes,
    /// double quotes, and the template sequences ${ / %{ (which Terraform would
    /// otherwise evaluate as interpolation/directives).
    /// </summary>
    public static string EscapeHcl(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("${", "$${")
            .Replace("%{", "%%{");

    /// <summary>
    /// Returns an escaped, double-quoted HCL string literal. Use this when
    /// composing <see cref="RawAttribute"/> expressions that mix user-supplied
    /// text with intentional Terraform interpolation.
    /// </summary>
    public static string Quoted(string value) => $"\"{EscapeHcl(value)}\"";

    public HclBuilder Attribute(string name, string value)
    {
        Line($"{name} = {Quoted(value)}");
        return this;
    }

    public HclBuilder Attribute(string name, int value)
    {
        Line($"{name} = {value}");
        return this;
    }

    public HclBuilder Attribute(string name, bool value)
    {
        Line($"{name} = {(value ? "true" : "false")}");
        return this;
    }

    public HclBuilder MapBlock(string name, Action<HclBuilder> body)
    {
        Line($"{name} = {{");
        _indent++;
        body(this);
        _indent--;
        Line("}");
        return this;
    }

    public HclBuilder RawAttribute(string name, string expression)
    {
        Line($"{name} = {expression}");
        return this;
    }

    public HclBuilder ListAttribute(string name, IEnumerable<string> values)
    {
        var items = string.Join(", ", values.Select(Quoted));
        Line($"{name} = [{items}]");
        return this;
    }

    public HclBuilder HeredocAttribute(string name, string content)
    {
        var lines = content.Split('\n');

        // A content line equal to the delimiter would terminate the heredoc
        // early and silently truncate the rest; pick one that never collides.
        var delimiter = "EOF";
        while (lines.Any(l => l.Trim() == delimiter))
            delimiter = "XCORD_" + delimiter;

        Line($"{name} = <<-{delimiter}");
        foreach (var line in lines)
            _sb.AppendLine($"{new string(' ', (_indent + 1) * 2)}{line}");
        Line(delimiter);
        return this;
    }

    public override string ToString() => _sb.ToString();
}
