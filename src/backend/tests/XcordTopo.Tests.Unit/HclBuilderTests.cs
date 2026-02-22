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
}
