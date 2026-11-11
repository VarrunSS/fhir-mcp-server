using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FhirMcp.Server.Summaries.SummaryJson;

namespace FhirMcp.Server.Summaries;

public static class DocumentReferenceSummary
{
    public const string TruncationMarker = " [truncated]";
    public const string NoTextNote = "No inline text is available for this document.";

    /// <param name="maxChars">
    /// Guardrail: the most document text to return. Search passes a short preview length,
    /// read passes the full-document maximum.
    /// </param>
    public static JsonObject From(JsonElement document, int maxChars)
    {
        var attachment = Obj(Items(document, "content").FirstOrDefault(), "attachment");
        var contentType = Str(attachment ?? default, "contentType");

        var authors = Items(document, "author").Select(a => Str(a, "display")).Where(a => !string.IsNullOrWhiteSpace(a));

        var summary = new JsonObject { ["reference"] = Reference(document) };
        summary.Set("type", CodeableText(Obj(document, "type")));
        summary.Set("status", Str(document, "status"));
        summary.Set("date", Str(document, "date"));
        summary.Set("author", string.Join("; ", authors));
        summary.Set("description", Str(document, "description"));
        summary.Set("contentType", contentType);

        var text = InlineText(attachment, contentType);
        if (text is null)
        {
            summary["textNote"] = NoTextNote;
        }
        else if (text.Length > maxChars)
        {
            summary["text"] = text[..maxChars] + TruncationMarker;
            summary["textTruncated"] = true;
        }
        else
        {
            summary["text"] = text;
            summary["textTruncated"] = false;
        }

        return summary;
    }

    // Only text/* attachments with inline base64 data are readable. PDFs, images, attachments
    // that are only a URL, and corrupt data all give null: the server never fetches attachment URLs.
    private static string? InlineText(JsonElement? attachment, string? contentType)
    {
        if (attachment is not { } a
            || contentType is null
            || !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || Str(a, "data") is not { } data)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
