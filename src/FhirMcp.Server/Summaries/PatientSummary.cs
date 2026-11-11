using System.Text.Json;
using System.Text.Json.Nodes;

namespace FhirMcp.Server.Summaries;

public static class PatientSummary
{
    public static JsonObject From(JsonElement patient)
    {
        var identifiers = new JsonArray();
        foreach (var identifier in SummaryJson.Items(patient, "identifier"))
        {
            var item = new JsonObject();
            item.Set("system", SummaryJson.Str(identifier, "system"));
            item.Set("value", SummaryJson.Str(identifier, "value"));
            identifiers.Add(item);
        }

        var summary = new JsonObject { ["reference"] = SummaryJson.Reference(patient) };
        summary.Set("name", DisplayName(patient));
        summary.Set("gender", SummaryJson.Str(patient, "gender"));
        summary.Set("birthDate", SummaryJson.Str(patient, "birthDate"));
        summary.Set("identifiers", identifiers);
        return summary;
    }

    // First name entry: its "text" when present, otherwise given names followed by the family name.
    private static string? DisplayName(JsonElement patient)
    {
        var name = SummaryJson.Items(patient, "name").FirstOrDefault();
        if (name.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = SummaryJson.Str(name, "text");
        if (text is not null)
        {
            return text;
        }

        var parts = SummaryJson.Items(name, "given")
            .Where(g => g.ValueKind == JsonValueKind.String)
            .Select(g => g.GetString())
            .Append(SummaryJson.Str(name, "family"))
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
