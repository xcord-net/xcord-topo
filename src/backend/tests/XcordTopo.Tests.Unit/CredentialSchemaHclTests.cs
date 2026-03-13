using System.Text.RegularExpressions;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

/// <summary>
/// Validates that credential schema keys match the HCL variable declarations they target.
/// A mismatch means the tfvars file will contain a key that Terraform doesn't recognize,
/// causing "undeclared variable" warnings and silently ignoring the user's input.
/// </summary>
public class CredentialSchemaHclTests
{
    private static readonly Regex VariableNameRegex = new(@"variable\s+""(\w+)""", RegexOptions.Compiled);

    public static IEnumerable<object[]> AllProviders =>
        new List<object[]>
        {
            new object[] { new AwsProvider() },
            new object[] { new LinodeProvider() },
        };

    [Theory]
    [MemberData(nameof(AllProviders))]
    public void CredentialSchemaKeys_MustMatchHclVariables(ICloudProvider provider)
    {
        // Generate HCL with a minimal topology so all variable blocks are emitted
        var topology = new Topology { Name = "schema-test", Provider = provider.Key };
        topology.Containers.Add(new Container
        {
            Name = "host", Kind = ContainerKind.Host, Width = 300, Height = 200
        });

        var files = provider.GenerateHcl(topology);
        var variables = files["variables.tf"];

        // Extract all declared variable names from HCL
        var declaredVars = VariableNameRegex.Matches(variables)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        // Every credential schema key must have a matching HCL variable
        var schema = provider.GetCredentialSchema();
        foreach (var field in schema)
        {
            Assert.True(declaredVars.Contains(field.Key),
                $"Provider '{provider.Key}': credential schema key '{field.Key}' " +
                $"has no matching variable \"{field.Key}\" in variables.tf. " +
                $"Declared variables: {string.Join(", ", declaredVars.Order())}");
        }
    }
}
