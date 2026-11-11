using System.Text.Json;
using System.Text.Json.Nodes;

namespace FhirMcp.Server.Summaries;

/// <summary>
/// The envelope every search tool returns:
/// { "total": int|null, "returned": int, "hasMore": bool, "results": [ summaries ] }.
/// "hasMore" tells the model the list was cut by the size limit, so it can narrow the search.
/// </summary>
public static class SearchResult
{
    public static string FromBundle(JsonElement bundle, Func<JsonElement, JsonObject> summarize)
    {
        var results = new JsonArray();
        foreach (var entry in SummaryJson.Items(bundle, "entry"))
        {
            // A searchset can carry an OperationOutcome entry with warnings; it is not a match.
            if (entry.TryGetProperty("resource", out var resource)
                && SummaryJson.Str(resource, "resourceType") != "OperationOutcome")
            {
                results.Add(summarize(resource));
            }
        }

        int? total = bundle.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt32(out var t)
            ? t
            : null;

        // FHIR paging: the server includes a "next" link when more pages exist.
        var hasMore = SummaryJson.Items(bundle, "link").Any(link => SummaryJson.Str(link, "relation") == "next");

        var envelope = new JsonObject
        {
            ["total"] = total,
            ["returned"] = results.Count,
            ["hasMore"] = hasMore,
            ["results"] = results,
        };
        return SummaryJson.Serialize(envelope);
    }
}
