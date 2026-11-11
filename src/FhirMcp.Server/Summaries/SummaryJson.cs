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

    /// <summary>An object-valued property, or null.</summary>
    public static JsonElement? Obj(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>
    /// Human-readable text of a CodeableConcept: its "text", else the first coding's display, else its code.
    /// </summary>
    public static string? CodeableText(JsonElement? concept)
    {
        if (concept is not { } c)
        {
            return null;
        }

        var text = Str(c, "text");
        if (text is not null)
        {
            return text;
        }

        var coding = Items(c, "coding").FirstOrDefault();
        return Str(coding, "display") ?? Str(coding, "code");
    }

    /// <summary>The first coding's code only, for status-like concepts such as clinicalStatus.</summary>
    public static string? CodeableCode(JsonElement? concept) =>
        concept is { } c ? Str(Items(c, "coding").FirstOrDefault(), "code") ?? Str(c, "text") : null;

    /// <summary>[{system, code}] for each coding, so the model can see standard codes such as LOINC.</summary>
    public static JsonArray Codings(JsonElement? concept)
    {
        var codings = new JsonArray();
        if (concept is { } c)
        {
            foreach (var coding in Items(c, "coding"))
            {
                var item = new JsonObject();
                item.Set("system", Str(coding, "system"));
                item.Set("code", Str(coding, "code"));
                codings.Add(item);
            }
        }

        return codings;
    }

    /// <summary>"7.2 %" from a Quantity. The number keeps the digits the server sent.</summary>
    public static string? Quantity(JsonElement? quantity)
    {
        if (quantity is not { } q || !q.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var unit = Str(q, "unit") ?? Str(q, "code");
        return unit is null ? value.GetRawText() : $"{value.GetRawText()} {unit}";
    }

    /// <summary>The texts of an array of {text} or CodeableConcept items, joined with "; ".</summary>
    public static string? JoinTexts(JsonElement element, string property)
    {
        var texts = Items(element, property).Select(item => CodeableText(item)).Where(t => !string.IsNullOrWhiteSpace(t));
        var joined = string.Join("; ", texts);
        return joined.Length == 0 ? null : joined;
    }

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
