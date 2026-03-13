using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Storage;

namespace XcordTopo.Tests.Unit;

public sealed class FileCredentialStoreTests : IDisposable
{
    private static readonly Guid TestTopologyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly string _tempDir;
    private readonly FileCredentialStore _store;

    public FileCredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"xcord-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var options = Options.Create(new DataOptions { BasePath = _tempDir });
        _store = new FileCredentialStore(options, NullLogger<FileCredentialStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task GetStatusAsync_NoFile_ReturnsEmpty()
    {
        var status = await _store.GetStatusAsync(TestTopologyId, "linode");

        Assert.False(status.HasCredentials);
        Assert.Empty(status.SetVariables);
        Assert.Empty(status.NonSensitiveValues);
    }

    [Fact]
    public async Task SaveAsync_CreatesFile_GetStatusReturnsValues()
    {
        var variables = new Dictionary<string, string>
        {
            ["linode_token"] = "abc123secret",
            ["linode_region"] = "us-east",
            ["domain"] = "example.com"
        };

        await _store.SaveAsync(TestTopologyId, "linode", variables);
        var status = await _store.GetStatusAsync(TestTopologyId, "linode");

        Assert.True(status.HasCredentials);
        Assert.Equal(3, status.SetVariables.Count);
        Assert.Contains("linode_token", status.SetVariables);
        Assert.Contains("linode_region", status.SetVariables);
        Assert.Contains("domain", status.SetVariables);

        // Token is sensitive — should NOT appear in non-sensitive values
        Assert.DoesNotContain("linode_token", status.NonSensitiveValues.Keys);

        // Region and domain are NOT sensitive — should appear
        Assert.Equal("us-east", status.NonSensitiveValues["linode_region"]);
        Assert.Equal("example.com", status.NonSensitiveValues["domain"]);
    }

    [Fact]
    public async Task SaveAsync_MergesWithExisting()
    {
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["linode_region"] = "us-east",
            ["domain"] = "old.com"
        });

        // Update domain, add new key
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["domain"] = "new.com",
            ["instance_count"] = "3"
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");
        Assert.Equal(3, status.SetVariables.Count);
        Assert.Equal("new.com", status.NonSensitiveValues["domain"]);
        Assert.Equal("us-east", status.NonSensitiveValues["linode_region"]);
    }

    [Fact]
    public async Task SaveAsync_EmptyValueRemovesKey()
    {
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["linode_region"] = "us-east",
            ["domain"] = "example.com"
        });

        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["linode_region"] = ""
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");
        Assert.Single(status.SetVariables);
        Assert.Equal("domain", status.SetVariables[0]);
    }

    [Fact]
    public async Task ParseTfVars_HandlesQuotedAndUnquoted()
    {
        var filePath = Path.Combine(_tempDir, "credentials", "test.tfvars");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath,
            """
            quoted_var = "hello world"
            unquoted_var = plain
            # This is a comment

            spaced_var   =   "  spaces  "
            """);

        var result = await FileCredentialStore.ParseTfVarsAsync(filePath);

        Assert.Equal("hello world", result["quoted_var"]);
        Assert.Equal("plain", result["unquoted_var"]);
        Assert.Equal("  spaces  ", result["spaced_var"]);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task SaveAsync_EscapesSpecialCharacters()
    {
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["value_with_quotes"] = "say \"hello\"",
            ["value_with_newline"] = "line1\nline2"
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");
        Assert.True(status.HasCredentials);
        Assert.Equal(2, status.SetVariables.Count);
    }

    [Fact]
    public async Task GetStatusAsync_MigratesLegacyRegionKey_Linode()
    {
        // Simulate old tfvars with bare "region" key
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["linode_token"] = "tok_123",
            ["region"] = "us-lax"
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");

        // Old "region" should be migrated to "linode_region"
        Assert.Contains("linode_region", status.SetVariables);
        Assert.DoesNotContain("region", status.SetVariables);
        Assert.Equal("us-lax", status.NonSensitiveValues["linode_region"]);
    }

    [Fact]
    public async Task GetStatusAsync_MigratesLegacyRegionKey_Aws()
    {
        await _store.SaveAsync(TestTopologyId, "aws", new Dictionary<string, string>
        {
            ["aws_access_key_id"] = "AKIA...",
            ["region"] = "us-west-1"
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "aws");

        Assert.Contains("aws_region", status.SetVariables);
        Assert.DoesNotContain("region", status.SetVariables);
        Assert.Equal("us-west-1", status.NonSensitiveValues["aws_region"]);
    }

    [Fact]
    public async Task GetStatusAsync_DoesNotOverwriteNewKey_WhenBothExist()
    {
        // If someone already has both old and new keys, keep the new one
        var filePath = Path.Combine(_tempDir, "deployments", TestTopologyId.ToString(), "credentials", "linode.tfvars");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "region = \"us-east\"\nlinode_region = \"us-lax\"\n");

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");

        Assert.Equal("us-lax", status.NonSensitiveValues["linode_region"]);
    }

    [Fact]
    public async Task GetStatusAsync_RemovesObsoleteSshKeys()
    {
        await _store.SaveAsync(TestTopologyId, "linode", new Dictionary<string, string>
        {
            ["linode_token"] = "tok_123",
            ["ssh_public_key"] = "ssh-rsa AAAA",
            ["ssh_private_key"] = "-----BEGIN-----"
        });

        var status = await _store.GetStatusAsync(TestTopologyId, "linode");

        Assert.DoesNotContain("ssh_public_key", status.SetVariables);
        Assert.DoesNotContain("ssh_private_key", status.SetVariables);
        Assert.Single(status.SetVariables); // only linode_token remains
    }

    [Theory]
    [InlineData("linode_token", true)]
    [InlineData("api_secret", true)]
    [InlineData("db_password", true)]
    [InlineData("access_key", true)]
    [InlineData("region", false)]
    [InlineData("domain", false)]
    [InlineData("ssh_public_key", true)]
    [InlineData("instance_count", false)]
    public void IsSensitive_CorrectlyIdentifies(string key, bool expected)
    {
        Assert.Equal(expected, FileCredentialStore.IsSensitive(key));
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("line1\nline2", "line1\\nline2")]
    public void EscapeTfValue_CorrectlyEscapes(string input, string expected)
    {
        Assert.Equal(expected, FileCredentialStore.EscapeTfValue(input));
    }
}
