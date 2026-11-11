using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FhirMcp.Tests.Integration;

/// <summary>
/// Every tool, called through a real MCP client against the seeded local HAPI server.
/// The ids and values asserted here come from data/*.json; change one and change the other.
/// </summary>
[Trait("Category", "Integration")]
public class ToolsAgainstHapiTests(HapiFixture hapi) : IClassFixture<HapiFixture>
{
    private async Task<CallToolResult> CallAsync(string tool, params (string Name, object? Value)[] arguments) =>
        await hapi.Client.CallToolAsync(
            tool,
            arguments.ToDictionary(a => a.Name, a => a.Value));

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private async Task<JsonElement> JsonAsync(string tool, params (string Name, object? Value)[] arguments)
    {
        var result = await CallAsync(tool, arguments);
        Assert.True(result.IsError != true, TextOf(result));
        return JsonDocument.Parse(TextOf(result)).RootElement.Clone();
    }

    private static string[] References(JsonElement searchResult) =>
        searchResult.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("reference").GetString()!).ToArray();

    [Fact]
    public async Task Search_patients_by_name()
    {
        var result = await JsonAsync("search_patients", ("name", "Quillfeather"));

        Assert.Equal(["Patient/pat-ada"], References(result));
        Assert.Equal("Ada Quillfeather", result.GetProperty("results")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Read_patient_returns_demographics_and_identifier()
    {
        var patient = await JsonAsync("read_patient", ("patientId", "pat-bram"));

        Assert.Equal("Patient/pat-bram", patient.GetProperty("reference").GetString());
        Assert.Equal("Bram Thistlewood", patient.GetProperty("name").GetString());
        Assert.Equal("male", patient.GetProperty("gender").GetString());
        Assert.Equal("1978-09-30", patient.GetProperty("birthDate").GetString());
        Assert.Equal("MRN-2002", patient.GetProperty("identifiers")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Search_and_read_condition()
    {
        var all = await JsonAsync("search_conditions", ("patientId", "pat-ada"));
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        Assert.Contains("Condition/cond-ada-t2dm", References(all));

        var resolved = await JsonAsync("search_conditions", ("patientId", "pat-ada"), ("clinicalStatus", "resolved"));
        Assert.Equal(["Condition/cond-ada-uti"], References(resolved));
        Assert.Equal("2024-02-23", resolved.GetProperty("results")[0].GetProperty("abatementDateTime").GetString());

        var condition = await JsonAsync("read_condition", ("conditionId", "cond-ada-t2dm"));
        Assert.Equal("Condition/cond-ada-t2dm", condition.GetProperty("reference").GetString());
        Assert.Equal("Type 2 diabetes mellitus", condition.GetProperty("code").GetString());
        Assert.Equal("active", condition.GetProperty("clinicalStatus").GetString());
        Assert.Equal("44054006", condition.GetProperty("codings")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_and_read_observation()
    {
        // The same lab repeated on three dates comes back newest first.
        var hba1c = await JsonAsync("search_observations", ("patientId", "pat-ada"), ("code", "4548-4"));
        Assert.Equal(
            ["Observation/obs-ada-hba1c-2026-03", "Observation/obs-ada-hba1c-2025-09", "Observation/obs-ada-hba1c-2025-03"],
            References(hba1c));
        Assert.Equal(
            ["7.2 %", "7.6 %", "8.1 %"],
            hba1c.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("value").GetString()!).ToArray());

        // The code:text parameter finds the same observations by name, not by code.
        var byText = await JsonAsync("search_observations", ("patientId", "pat-ada"), ("codeText", "HbA1c"));
        Assert.Equal(3, byText.GetProperty("total").GetInt32());

        var byCategory = await JsonAsync("search_observations", ("patientId", "pat-bram"), ("category", "social-history"));
        Assert.Equal(["Observation/obs-bram-smoking-2026-05-06"], References(byCategory));

        var bp = await JsonAsync("read_observation", ("observationId", "obs-ada-bp-2026-03"));
        Assert.Equal("Observation/obs-ada-bp-2026-03", bp.GetProperty("reference").GetString());
        var components = bp.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal("Systolic blood pressure", components[0].GetProperty("code").GetString());
        Assert.Equal("138 mmHg", components[0].GetProperty("value").GetString());
        Assert.Equal("84 mmHg", components[1].GetProperty("value").GetString());
        Assert.Equal("High", bp.GetProperty("interpretation").GetString());

        var egfr = await JsonAsync("read_observation", ("observationId", "obs-chidi-egfr-2026-08"));
        Assert.Equal("44 mL/min/1.73m2", egfr.GetProperty("value").GetString());
        Assert.Equal(">= 60 mL/min/1.73m2", egfr.GetProperty("referenceRange").GetString());
    }

    [Fact]
    public async Task Observations_date_range_filters()
    {
        var inRange = await JsonAsync(
            "search_observations", ("patientId", "pat-chidi"), ("code", "6301-6"),
            ("dateFrom", "2026-07-01"), ("dateTo", "2026-08-31"));

        // June's INR is outside the range; the two inside it come back newest first.
        Assert.Equal(
            ["Observation/obs-chidi-inr-2026-08", "Observation/obs-chidi-inr-2026-07"],
            References(inRange));
        Assert.Equal(2, inRange.GetProperty("total").GetInt32());

        var oneMonth = await JsonAsync(
            "search_observations", ("patientId", "pat-ada"), ("code", "4548-4"),
            ("dateFrom", "2025-06"), ("dateTo", "2025-12"));
        Assert.Equal(["Observation/obs-ada-hba1c-2025-09"], References(oneMonth));
    }

    [Fact]
    public async Task Count_limit_reports_more()
    {
        var result = await JsonAsync("search_observations", ("patientId", "pat-ada"), ("count", 2));

        Assert.Equal(2, result.GetProperty("returned").GetInt32());
        Assert.True(result.GetProperty("hasMore").GetBoolean());
        Assert.True(result.GetProperty("total").GetInt32() > 2);
    }

    [Fact]
    public async Task Search_and_read_medication_request()
    {
        var stopped = await JsonAsync("search_medication_requests", ("patientId", "pat-ada"), ("status", "stopped"));
        Assert.Equal(["MedicationRequest/med-ada-glipizide"], References(stopped));
        Assert.Equal("Adverse effect", stopped.GetProperty("results")[0].GetProperty("statusReason").GetString());

        var active = await JsonAsync("search_medication_requests", ("patientId", "pat-chidi"), ("status", "active"));
        Assert.Equal(3, active.GetProperty("total").GetInt32());
        Assert.DoesNotContain("MedicationRequest/med-chidi-naproxen", References(active));

        var warfarin = await JsonAsync("read_medication_request", ("medicationRequestId", "med-chidi-warfarin"));
        Assert.Equal("MedicationRequest/med-chidi-warfarin", warfarin.GetProperty("reference").GetString());
        Assert.Equal("Warfarin 3 mg tablet", warfarin.GetProperty("medication").GetString());
        Assert.Contains("3 mg", warfarin.GetProperty("dosage").GetString());
        Assert.Equal("Atrial fibrillation, stroke prevention", warfarin.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Search_and_read_document_reference()
    {
        var all = await JsonAsync("search_document_references", ("patientId", "pat-ada"));
        Assert.Equal(
            [
                "DocumentReference/doc-ada-clinic-2026-03",
                "DocumentReference/doc-ada-phone-2025-06",
                "DocumentReference/doc-ada-discharge-2024-02",
            ],
            References(all));

        // The clinic note is longer than the 500-character preview, so search cuts it short.
        var clinic = all.GetProperty("results")[0];
        Assert.True(clinic.GetProperty("textTruncated").GetBoolean());
        Assert.EndsWith("[truncated]", clinic.GetProperty("text").GetString());

        var discharge = await JsonAsync("search_document_references", ("patientId", "pat-ada"), ("typeText", "discharge"));
        Assert.Equal(["DocumentReference/doc-ada-discharge-2024-02"], References(discharge));

        var read = await JsonAsync("read_document_reference", ("documentReferenceId", "doc-ada-discharge-2024-02"));
        Assert.Equal("DocumentReference/doc-ada-discharge-2024-02", read.GetProperty("reference").GetString());
        Assert.Equal("Discharge summary", read.GetProperty("type").GetString());
        Assert.Equal("Dr Ignatius Fernsby", read.GetProperty("author").GetString());
    }

    [Fact]
    public async Task Document_text_is_decoded()
    {
        var read = await JsonAsync("read_document_reference", ("documentReferenceId", "doc-ada-clinic-2026-03"));

        var text = read.GetProperty("text").GetString()!;
        Assert.False(read.GetProperty("textTruncated").GetBoolean());
        Assert.StartsWith("CLINIC NOTE - Diabetes and cardiovascular review", text);
        Assert.Contains("Ms Quillfeather attends for her six-monthly review.", text);
        // A fact that is only in the note, not in any structured resource.
        Assert.Contains("Repeat HbA1c on 2026-09-15", text);
    }

    [Fact]
    public async Task Unknown_patient_search_is_empty_not_error()
    {
        var result = await CallAsync("search_conditions", ("patientId", "no-such-patient"));

        Assert.True(result.IsError != true, TextOf(result));
        var json = JsonDocument.Parse(TextOf(result)).RootElement;
        Assert.Equal(0, json.GetProperty("total").GetInt32());
        Assert.Equal(0, json.GetProperty("returned").GetInt32());
    }

    [Fact]
    public async Task Read_unknown_id_is_a_tool_error()
    {
        var result = await CallAsync("read_patient", ("patientId", "does-not-exist"));

        Assert.True(result.IsError);
        Assert.Contains("404", TextOf(result));
    }
}
