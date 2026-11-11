using System.Text.Json;

namespace FhirMcp.Server.Fhir;

/// <summary>
/// FHIR servers describe errors with an OperationOutcome resource. This turns one into
/// a single line of text that a model (or a person) can act on.
/// </summary>
public static class OperationOutcomeParser
{
    /// <returns>"severity code: text" per issue joined with "; ", or null when the body is not an OperationOutcome with issues.</returns>
    public static string? Describe(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("resourceType", out var type)
                || type.GetString() != "OperationOutcome"
                || !root.TryGetProperty("issue", out var issues)
                || issues.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var lines = issues.EnumerateArray().Select(DescribeIssue).ToList();
            return lines.Count == 0 ? null : string.Join("; ", lines);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeIssue(JsonElement issue)
    {
        var label = $"{Text(issue, "severity")} {Text(issue, "code")}".Trim();

        var text = Text(issue, "diagnostics");
        if (text is null && issue.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            text = Text(details, "text");
        }

        return text is null ? label : $"{label}: {text}";
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
