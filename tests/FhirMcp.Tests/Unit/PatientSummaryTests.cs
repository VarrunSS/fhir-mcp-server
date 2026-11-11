using System.Text.Json;
using FhirMcp.Server.Summaries;

namespace FhirMcp.Tests.Unit;

public class PatientSummaryTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Maps_core_fields()
    {
        var patient = Parse("""
            {
              "resourceType": "Patient",
              "id": "pat-ada",
              "meta": { "versionId": "3", "lastUpdated": "2026-01-01T00:00:00Z" },
              "identifier": [ { "system": "urn:example:mrn", "value": "MRN-0001" } ],
              "name": [ { "use": "official", "family": "Quillfeather", "given": [ "Ada", "Marie" ] } ],
              "telecom": [ { "system": "phone", "value": "555-0100" } ],
              "gender": "female",
              "birthDate": "1961-04-12"
            }
            """);

        var summary = PatientSummary.From(patient);

        Assert.Equal(
            """{"reference":"Patient/pat-ada","name":"Ada Marie Quillfeather","gender":"female","birthDate":"1961-04-12","identifiers":[{"system":"urn:example:mrn","value":"MRN-0001"}]}""",
            SummaryJson.Serialize(summary));
    }

    [Fact]
    public void Sparse_patient_does_not_throw()
    {
        var summary = PatientSummary.From(Parse("""{"resourceType":"Patient","id":"pat-min"}"""));

        Assert.Equal("Patient/pat-min", summary["reference"]!.GetValue<string>());
        Assert.Null(summary["name"]);
        Assert.Null(summary["birthDate"]);
    }

    [Fact]
    public void Prefers_name_text_when_present()
    {
        var summary = PatientSummary.From(Parse(
            """{"resourceType":"Patient","id":"p1","name":[{"text":"Dr Ada Quillfeather","family":"Quillfeather"}]}"""));

        Assert.Equal("Dr Ada Quillfeather", summary["name"]!.GetValue<string>());
    }
}
