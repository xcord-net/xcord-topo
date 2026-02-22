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

    public HclBuilder Attribute(string name, string value)
    {
        Line($"{name} = \"{value}\"");
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

    public HclBuilder RawAttribute(string name, string expression)
    {
        Line($"{name} = {expression}");
        return this;
    }

    public HclBuilder ListAttribute(string name, IEnumerable<string> values)
    {
        var items = string.Join(", ", values.Select(v => $"\"{v}\""));
        Line($"{name} = [{items}]");
        return this;
    }

    public HclBuilder HeredocAttribute(string name, string content)
    {
        Line($"{name} = <<-EOF");
        foreach (var line in content.Split('\n'))
            _sb.AppendLine($"{new string(' ', (_indent + 1) * 2)}{line}");
        Line("EOF");
        return this;
    }

    public override string ToString() => _sb.ToString();
}
