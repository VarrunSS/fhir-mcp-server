using System.Text.Json;
using System.Text.Json.Nodes;
using static FhirMcp.Server.Summaries.SummaryJson;

namespace FhirMcp.Server.Summaries;

public static class MedicationRequestSummary
{
    public static JsonObject From(JsonElement request)
    {
        // medication[x]: either an inline CodeableConcept or a reference with a display name.
        var medication = CodeableText(Obj(request, "medicationCodeableConcept"))
            ?? Str(Obj(request, "medicationReference") ?? default, "display");

        var summary = new JsonObject { ["reference"] = Reference(request) };
        summary.Set("medication", medication);
        summary.Set("status", Str(request, "status"));
        summary.Set("intent", Str(request, "intent"));
        summary.Set("authoredOn", Str(request, "authoredOn"));
        summary.Set("dosage", JoinTexts(request, "dosageInstruction"));
        summary.Set("reason", JoinTexts(request, "reasonCode"));
        summary.Set("statusReason", CodeableText(Obj(request, "statusReason")));
        summary.Set("note", JoinTexts(request, "note"));
        return summary;
    }
}
