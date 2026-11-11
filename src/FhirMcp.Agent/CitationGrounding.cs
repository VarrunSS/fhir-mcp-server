using System.Text.Json;

namespace FhirMcp.Agent;

/// <summary>
/// The citation grounding check. The model cites FHIR references; this proves each one
/// was actually returned by a tool call in the same run, so a made-up or remembered
/// reference is caught by code rather than trusted. Every tool summary carries a
/// "reference" property ("ResourceType/id"), which is what makes this check possible.
/// </summary>
public static class CitationGrounding
{
    /// <summary>Every string value of a "reference" property, at any depth, in any JSON tool result.</summary>
    public static IReadOnlySet<string> RetrievedReferences(IEnumerable<string> toolResultTexts)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in toolResultTexts)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                Collect(doc.RootElement, found);
            }
            catch (JsonException)
            {
                // Error messages and other plain text are not JSON and hold no references.
            }
        }
        return found;
    }

    /// <summary>Citations (exact, case-sensitive) that no tool result contained, in the order given.</summary>
    public static IReadOnlyList<string> FindUngrounded(IEnumerable<string> citations, IEnumerable<string> toolResultTexts)
    {
        var retrieved = RetrievedReferences(toolResultTexts);
        return citations.Where(c => !retrieved.Contains(c)).ToList();
    }

    private static void Collect(JsonElement element, HashSet<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "reference" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        found.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        Collect(property.Value, found);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, found);
                }
                break;
        }
    }
}
