using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FhirMcp.Server.Summaries;

/// <summary>
/// Small helpers shared by the summarizers. FHIR resources are read as plain JSON,
/// and every read is tolerant: a missing or oddly shaped field gives null, never an exception.
/// </summary>
public static class SummaryJson
{
    // Relaxed escaping keeps characters such as ' and non-ASCII letters readable for the model
    // instead of \uXXXX sequences. The output is tool text, never embedded in HTML.
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(JsonNode node) => node.ToJsonString(Compact);

    /// <summary>"ResourceType/id": the value the agent cites.</summary>
    public static string Reference(JsonElement resource) =>
        $"{Str(resource, "resourceType")}/{Str(resource, "id")}";

    public static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The items of an array property, or nothing when it is missing or not an array.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    /// <summary>Adds the property only when there is a value, so summaries stay small.</summary>
    public static void Set(this JsonObject target, string property, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[property] = value;
        }
    }

    public static void Set(this JsonObject target, string property, JsonArray value)
    {
        if (value.Count > 0)
        {
            target[property] = value;
        }
    }
}
