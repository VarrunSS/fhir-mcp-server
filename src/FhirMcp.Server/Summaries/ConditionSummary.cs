using System.Text.Json;
using System.Text.Json.Nodes;
using static FhirMcp.Server.Summaries.SummaryJson;

namespace FhirMcp.Server.Summaries;

public static class ConditionSummary
{
    public static JsonObject From(JsonElement condition)
    {
        var code = Obj(condition, "code");

        var summary = new JsonObject { ["reference"] = Reference(condition) };
        summary.Set("code", CodeableText(code));
        summary.Set("codings", Codings(code));
        summary.Set("clinicalStatus", CodeableCode(Obj(condition, "clinicalStatus")));
        summary.Set("verificationStatus", CodeableCode(Obj(condition, "verificationStatus")));
        summary.Set("onsetDateTime", Str(condition, "onsetDateTime"));
        summary.Set("abatementDateTime", Str(condition, "abatementDateTime"));
        summary.Set("recordedDate", Str(condition, "recordedDate"));
        summary.Set("note", JoinTexts(condition, "note"));
        return summary;
    }
}
