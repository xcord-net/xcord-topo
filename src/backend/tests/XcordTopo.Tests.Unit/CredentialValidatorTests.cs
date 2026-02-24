using XcordTopo.Infrastructure.Validation;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public sealed class CredentialValidatorTests
{
    [Fact]
    public void RequiredFieldMissing_ReturnsError()
    {
        var schema = new List<CredentialField>
        {
            new() { Key = "token", Label = "Token", Required = true }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string>(), []);

        Assert.Contains("token", errors.Keys);
        Assert.Equal("Token is required", errors["token"]);
    }

    [Fact]
    public void RequiredFieldEmpty_ReturnsError()
    {
        var schema = new List<CredentialField>
        {
            new() { Key = "token", Label = "Token", Required = true }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["token"] = "  " }, []);

        Assert.Contains("token", errors.Keys);
    }

    [Fact]
    public void RequiredSensitiveFieldAlreadySaved_NoError()
    {
        var schema = new List<CredentialField>
        {
            new() { Key = "token", Label = "Token", Required = true, Sensitive = true }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string>(), ["token"]);

        Assert.Empty(errors);
    }

    [Fact]
    public void MinLengthViolation_ReturnsError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "token", Label = "Token", Required = true,
                Validation = [new() { Type = "minLength", Value = "10", Message = "Too short" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["token"] = "abc" }, []);

        Assert.Contains("token", errors.Keys);
        Assert.Equal("Too short", errors["token"]);
    }

    [Fact]
    public void MinLengthSatisfied_NoError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "token", Label = "Token", Required = true,
                Validation = [new() { Type = "minLength", Value = "3", Message = "Too short" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["token"] = "abc" }, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void MaxLengthViolation_ReturnsError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "name", Label = "Name", Required = true,
                Validation = [new() { Type = "maxLength", Value = "5", Message = "Too long" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["name"] = "toolong" }, []);

        Assert.Contains("name", errors.Keys);
        Assert.Equal("Too long", errors["name"]);
    }

    [Fact]
    public void PatternViolation_ReturnsError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "domain", Label = "Domain", Required = true,
                Validation = [new() { Type = "pattern", Value = @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$", Message = "Invalid domain" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["domain"] = "not a domain!" }, []);

        Assert.Contains("domain", errors.Keys);
        Assert.Equal("Invalid domain", errors["domain"]);
    }

    [Fact]
    public void ValidDomain_NoError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "domain", Label = "Domain", Required = true,
                Validation = [new() { Type = "pattern", Value = @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$", Message = "Invalid domain" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["domain"] = "example.com" }, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidAwsAccessKeyId_NoError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "key", Label = "Key", Required = true,
                Validation = [new() { Type = "pattern", Value = @"^AKIA[A-Z0-9]{16}$", Message = "Invalid key" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["key"] = "AKIAIOSFODNN7EXAMPLE" }, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidSshKey_NoError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "ssh", Label = "SSH", Required = false,
                Validation = [new() { Type = "pattern", Value = @"^ssh-(rsa|ed25519|ecdsa)\s+[A-Za-z0-9+/=]+", Message = "Invalid SSH key" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["ssh"] = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExample" }, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void OptionalFieldEmpty_NoError()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "ssh", Label = "SSH", Required = false,
                Validation = [new() { Type = "pattern", Value = @"^ssh-", Message = "Invalid" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string>(), []);

        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownRuleType_Ignored()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "x", Label = "X", Required = true,
                Validation = [new() { Type = "unknownRule", Value = "42", Message = "Should not appear" }]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["x"] = "any" }, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void FirstRuleFailureWins()
    {
        var schema = new List<CredentialField>
        {
            new()
            {
                Key = "x", Label = "X", Required = true,
                Validation =
                [
                    new() { Type = "minLength", Value = "5", Message = "First error" },
                    new() { Type = "minLength", Value = "3", Message = "Second error" }
                ]
            }
        };

        var errors = CredentialValidator.Validate(schema, new Dictionary<string, string> { ["x"] = "ab" }, []);

        Assert.Equal("First error", errors["x"]);
    }
}
