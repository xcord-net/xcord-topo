using XcordTopo.Infrastructure.Terraform;

namespace XcordTopo.Tests.Unit;

public class HclBuilderTests
{
    [Fact]
    public void Block_GeneratesCorrectStructure()
    {
        var builder = new HclBuilder();
        builder.Block("resource \"test\" \"main\"", b =>
        {
            b.Attribute("name", "hello");
            b.Attribute("count", 3);
            b.Attribute("enabled", true);
        });

        var result = builder.ToString();

        Assert.Contains("resource \"test\" \"main\" {", result);
        Assert.Contains("  name = \"hello\"", result);
        Assert.Contains("  count = 3", result);
        Assert.Contains("  enabled = true", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void NestedBlocks_IncreaseIndent()
    {
        var builder = new HclBuilder();
        builder.Block("outer", b =>
        {
            b.Block("inner", ib =>
            {
                ib.Attribute("key", "value");
            });
        });

        var result = builder.ToString();

        Assert.Contains("  inner {", result);
        Assert.Contains("    key = \"value\"", result);
    }

    [Fact]
    public void ListAttribute_GeneratesCorrectFormat()
    {
        var builder = new HclBuilder();
        builder.ListAttribute("tags", ["a", "b", "c"]);

        var result = builder.ToString();

        Assert.Contains("tags = [\"a\", \"b\", \"c\"]", result);
    }

    [Fact]
    public void RawAttribute_DoesNotQuoteValue()
    {
        var builder = new HclBuilder();
        builder.RawAttribute("token", "var.my_token");

        var result = builder.ToString();

        Assert.Contains("token = var.my_token", result);
    }

    [Fact]
    public void Attribute_ValueWithQuotesAndBackslashes_EscapesThem()
    {
        var builder = new HclBuilder();
        builder.Attribute("name", "foo\"bar\\baz");

        var result = builder.ToString();

        Assert.Contains("name = \"foo\\\"bar\\\\baz\"", result);
    }

    [Fact]
    public void Attribute_ValueWithTemplateSequences_EscapesInterpolation()
    {
        var builder = new HclBuilder();
        builder.Attribute("name", "a${var.x}b%{if}c");

        var result = builder.ToString();

        Assert.Contains("name = \"a$${var.x}b%%{if}c\"", result);
    }

    [Fact]
    public void ListAttribute_ItemsWithUnsafeChars_EscapesEachItem()
    {
        var builder = new HclBuilder();
        builder.ListAttribute("tags", ["safe", "evil\"quote", "tpl${var.x}"]);

        var result = builder.ToString();

        Assert.Contains("tags = [\"safe\", \"evil\\\"quote\", \"tpl$${var.x}\"]", result);
    }

    [Fact]
    public void Quoted_ProducesEscapedQuotedLiteralForRawAttributes()
    {
        var quoted = HclBuilder.Quoted("name\"with${bad}");

        Assert.Equal("\"name\\\"with$${bad}\"", quoted);
    }

    [Fact]
    public void HeredocAttribute_ContentContainingDelimiterLine_DoesNotTerminateEarly()
    {
        var builder = new HclBuilder();
        builder.HeredocAttribute("user_data", "line1\nEOF\nline2");

        var result = builder.ToString();
        var lines = result.Split('\n');
        var headerLine = lines.First(l => l.Contains("user_data = <<-"));
        var delimiter = headerLine[(headerLine.IndexOf("<<-", StringComparison.Ordinal) + 3)..].Trim();

        // A content line equal to the delimiter would terminate the heredoc
        // early, silently truncating everything after it.
        Assert.NotEqual("EOF", delimiter);
        Assert.Contains("line2", result);
        var terminatorIndex = Array.FindLastIndex(lines, l => l.Trim() == delimiter);
        var line2Index = Array.FindIndex(lines, l => l.Trim() == "line2");
        Assert.True(line2Index < terminatorIndex, "content after the colliding line must stay inside the heredoc");
    }
}
