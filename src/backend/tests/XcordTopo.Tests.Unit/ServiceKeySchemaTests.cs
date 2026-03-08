using XcordTopo.Infrastructure.Providers;

namespace XcordTopo.Tests.Unit;

public sealed class ServiceKeySchemaTests
{
    [Fact]
    public void GetSchema_ReturnsAllExpectedKeys()
    {
        var fields = ServiceKeySchema.GetSchema();
        var keys = fields.Select(f => f.Key).ToList();

        Assert.Contains("stripe_publishable_key", keys);
        Assert.Contains("stripe_secret_key", keys);
        Assert.Contains("smtp_host", keys);
        Assert.Contains("smtp_port", keys);
        Assert.Contains("smtp_username", keys);
        Assert.Contains("smtp_password", keys);
        Assert.Contains("smtp_from_address", keys);
        Assert.Contains("smtp_from_name", keys);
        Assert.Contains("tenor_api_key", keys);
    }

    [Fact]
    public void StripeFields_AreOptional()
    {
        var fields = ServiceKeySchema.GetSchema();
        Assert.False(fields.First(f => f.Key == "stripe_publishable_key").Required);
        Assert.False(fields.First(f => f.Key == "stripe_secret_key").Required);
    }

    [Fact]
    public void SmtpFields_RequiredExceptPortAndFromName()
    {
        var fields = ServiceKeySchema.GetSchema();
        Assert.True(fields.First(f => f.Key == "smtp_host").Required);
        Assert.False(fields.First(f => f.Key == "smtp_port").Required);
        Assert.True(fields.First(f => f.Key == "smtp_username").Required);
        Assert.True(fields.First(f => f.Key == "smtp_password").Required);
        Assert.True(fields.First(f => f.Key == "smtp_from_address").Required);
        Assert.False(fields.First(f => f.Key == "smtp_from_name").Required);
    }

    [Fact]
    public void TenorField_IsOptional()
    {
        var fields = ServiceKeySchema.GetSchema();
        Assert.False(fields.First(f => f.Key == "tenor_api_key").Required);
    }

    [Fact]
    public void SensitiveFields_AreMarkedSensitive()
    {
        var fields = ServiceKeySchema.GetSchema();
        Assert.True(fields.First(f => f.Key == "stripe_secret_key").Sensitive);
        Assert.True(fields.First(f => f.Key == "stripe_publishable_key").Sensitive);
        Assert.True(fields.First(f => f.Key == "smtp_password").Sensitive);
        Assert.True(fields.First(f => f.Key == "tenor_api_key").Sensitive);
    }

    [Fact]
    public void AllFields_HaveHelp()
    {
        var fields = ServiceKeySchema.GetSchema();
        foreach (var field in fields)
        {
            Assert.NotNull(field.Help);
            Assert.False(string.IsNullOrWhiteSpace(field.Help!.Summary), $"{field.Key} missing help summary");
            Assert.NotEmpty(field.Help.Steps);
        }
    }

    [Theory]
    [InlineData("stripe_publishable_key", "pk_live_abc123")]
    [InlineData("stripe_publishable_key", "pk_test_abc123")]
    [InlineData("stripe_secret_key", "sk_live_abc123")]
    [InlineData("stripe_secret_key", "sk_test_abc123")]
    [InlineData("stripe_secret_key", "rk_live_abc123")]
    public void ValidationPatterns_AcceptValidValues(string key, string value)
    {
        var fields = ServiceKeySchema.GetSchema();
        var field = fields.First(f => f.Key == key);
        var errors = XcordTopo.Infrastructure.Validation.CredentialValidator.Validate(
            [field],
            new Dictionary<string, string> { [key] = value },
            []);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("stripe_publishable_key", "not_a_stripe_key")]
    [InlineData("stripe_secret_key", "not_a_stripe_key")]
    [InlineData("smtp_port", "abc")]
    public void ValidationPatterns_RejectInvalidValues(string key, string value)
    {
        var fields = ServiceKeySchema.GetSchema();
        var field = fields.First(f => f.Key == key);
        var errors = XcordTopo.Infrastructure.Validation.CredentialValidator.Validate(
            [field],
            new Dictionary<string, string> { [key] = value },
            []);
        Assert.NotEmpty(errors);
    }
}
