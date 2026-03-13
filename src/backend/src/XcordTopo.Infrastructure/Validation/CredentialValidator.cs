using System.Text.RegularExpressions;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Validation;

public static class CredentialValidator
{
    public static Dictionary<string, string> Validate(
        List<CredentialField> schema,
        Dictionary<string, string> values)
    {
        var errors = new Dictionary<string, string>();

        // Only validate fields that are actually being submitted - this is a partial-update API.
        // Required-field completeness is checked at deploy time, not on each save.
        foreach (var field in schema)
        {
            if (!values.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var rule in field.Validation)
            {
                var error = EvaluateRule(rule, value);
                if (error is not null)
                {
                    errors[field.Key] = error;
                    break;
                }
            }
        }

        return errors;
    }

    private static string? EvaluateRule(ValidationRule rule, string value) =>
        rule.Type switch
        {
            "minLength" when int.TryParse(rule.Value, out var min) && value.Length < min => rule.Message,
            "maxLength" when int.TryParse(rule.Value, out var max) && value.Length > max => rule.Message,
            "pattern" when rule.Value is not null && !Regex.IsMatch(value, rule.Value) => rule.Message,
            _ => null
        };
}
