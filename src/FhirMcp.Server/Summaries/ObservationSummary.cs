using System.Text.Json;
using System.Text.Json.Nodes;
using static FhirMcp.Server.Summaries.SummaryJson;

namespace FhirMcp.Server.Summaries;

public static class ObservationSummary
{
    public static JsonObject From(JsonElement observation)
    {
        var code = Obj(observation, "code");

        // Panels such as blood pressure carry their numbers in components, not in a top-level value.
        var components = new JsonArray();
        foreach (var component in Items(observation, "component"))
        {
            var item = new JsonObject();
            item.Set("code", CodeableText(Obj(component, "code")));
            item.Set("value", Value(component));
            components.Add(item);
        }

        var summary = new JsonObject { ["reference"] = Reference(observation) };
        summary.Set("code", CodeableText(code));
        summary.Set("codings", Codings(code));
        summary.Set("category", CodeableCode(Items(observation, "category").FirstOrDefault()));
        summary.Set("status", Str(observation, "status"));
        summary.Set("effectiveDateTime",
            Str(observation, "effectiveDateTime") ?? Str(Obj(observation, "effectivePeriod") ?? default, "start"));
        summary.Set("value", Value(observation));
        summary.Set("components", components);
        summary.Set("interpretation", JoinTexts(observation, "interpretation"));
        summary.Set("referenceRange", ReferenceRange(observation));
        summary.Set("note", JoinTexts(observation, "note"));
        return summary;
    }

    // FHIR's value[x]: the value can be one of several types, each under its own property name.
    private static string? Value(JsonElement element)
    {
        if (Quantity(Obj(element, "valueQuantity")) is { } quantity)
        {
            return quantity;
        }

        if (Str(element, "valueString") is { } text)
        {
            return text;
        }

        if (CodeableText(Obj(element, "valueCodeableConcept")) is { } concept)
        {
            return concept;
        }

        foreach (var property in new[] { "valueBoolean", "valueInteger" })
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return Str(element, "valueDateTime");
    }

    private static string? ReferenceRange(JsonElement observation)
    {
        var range = Items(observation, "referenceRange").FirstOrDefault();
        if (range.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Str(range, "text") is { } text)
        {
            return text;
        }

        var low = Obj(range, "low");
        var high = Obj(range, "high");
        var unit = Str(high ?? default, "unit") ?? Str(low ?? default, "unit");
        var lowValue = Number(low);
        var highValue = Number(high);

        var numbers = (lowValue, highValue) switch
        {
            (not null, not null) => $"{lowValue} - {highValue}",
            (not null, null) => $">= {lowValue}",
            (null, not null) => $"<= {highValue}",
            _ => null,
        };
        return numbers is null ? null : unit is null ? numbers : $"{numbers} {unit}";
    }

    private static string? Number(JsonElement? quantity) =>
        quantity is { } q && q.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;
}
