using System.Text;
using System.Text.Json;
using FhirMcp.Server.Summaries;

namespace FhirMcp.Tests.Unit;

/// <summary>Condition, Observation and MedicationRequest summaries.</summary>
public class ClinicalSummaryTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Condition_maps_core_fields()
    {
        var condition = Parse("""
            {
              "resourceType": "Condition", "id": "cond-ada-t2dm",
              "clinicalStatus": { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/condition-clinical", "code": "active" } ] },
              "verificationStatus": { "coding": [ { "code": "confirmed" } ] },
              "code": { "coding": [ { "system": "http://snomed.info/sct", "code": "44054006", "display": "Diabetes mellitus type 2" } ], "text": "Type 2 diabetes mellitus" },
              "subject": { "reference": "Patient/pat-ada" },
              "onsetDateTime": "2019-06-03",
              "recordedDate": "2019-06-10",
              "note": [ { "text": "Diagnosed after routine screening." } ]
            }
            """);

        Assert.Equal(
            """{"reference":"Condition/cond-ada-t2dm","code":"Type 2 diabetes mellitus","codings":[{"system":"http://snomed.info/sct","code":"44054006"}],"clinicalStatus":"active","verificationStatus":"confirmed","onsetDateTime":"2019-06-03","recordedDate":"2019-06-10","note":"Diagnosed after routine screening."}""",
            SummaryJson.Serialize(ConditionSummary.From(condition)));
    }

    [Fact]
    public void Condition_uses_coding_display_when_there_is_no_text_and_keeps_abatement()
    {
        var summary = ConditionSummary.From(Parse("""
            {"resourceType":"Condition","id":"c1",
             "code":{"coding":[{"system":"s","code":"1","display":"Pneumonia"}]},
             "abatementDateTime":"2024-02-01"}
            """));

        Assert.Equal("Pneumonia", summary["code"]!.GetValue<string>());
        Assert.Equal("2024-02-01", summary["abatementDateTime"]!.GetValue<string>());
    }

    [Fact]
    public void Observation_with_quantity()
    {
        var observation = Parse("""
            {
              "resourceType": "Observation", "id": "obs-ada-hba1c-2026-03", "status": "final",
              "category": [ { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/observation-category", "code": "laboratory" } ] } ],
              "code": { "coding": [ { "system": "http://loinc.org", "code": "4548-4", "display": "Hemoglobin A1c" } ], "text": "HbA1c" },
              "effectiveDateTime": "2026-03-14",
              "valueQuantity": { "value": 7.2, "unit": "%", "system": "http://unitsofmeasure.org", "code": "%" },
              "interpretation": [ { "coding": [ { "code": "H", "display": "High" } ] } ],
              "referenceRange": [ { "low": { "value": 4.0, "unit": "%" }, "high": { "value": 5.6, "unit": "%" } } ]
            }
            """);

        Assert.Equal(
            """{"reference":"Observation/obs-ada-hba1c-2026-03","code":"HbA1c","codings":[{"system":"http://loinc.org","code":"4548-4"}],"category":"laboratory","status":"final","effectiveDateTime":"2026-03-14","value":"7.2 %","interpretation":"High","referenceRange":"4.0 - 5.6 %"}""",
            SummaryJson.Serialize(ObservationSummary.From(observation)));
    }

    [Fact]
    public void Observation_with_components_for_blood_pressure()
    {
        var summary = ObservationSummary.From(Parse("""
            {
              "resourceType": "Observation", "id": "obs-bp", "status": "final",
              "code": { "text": "Blood pressure" },
              "effectiveDateTime": "2026-02-01",
              "component": [
                { "code": { "text": "Systolic blood pressure" }, "valueQuantity": { "value": 148, "unit": "mmHg" } },
                { "code": { "text": "Diastolic blood pressure" }, "valueQuantity": { "value": 92, "unit": "mmHg" } }
              ]
            }
            """));

        Assert.Null(summary["value"]);
        var components = summary["components"]!.AsArray();
        Assert.Equal(2, components.Count);
        Assert.Equal("Systolic blood pressure", components[0]!["code"]!.GetValue<string>());
        Assert.Equal("148 mmHg", components[0]!["value"]!.GetValue<string>());
        Assert.Equal("92 mmHg", components[1]!["value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"valueString":"Negative"}""", "Negative")]
    [InlineData("""{"valueCodeableConcept":{"text":"Never smoker"}}""", "Never smoker")]
    [InlineData("""{"valueBoolean":true}""", "true")]
    [InlineData("""{"valueInteger":3}""", "3")]
    [InlineData("""{"valueQuantity":{"value":5}}""", "5")]
    public void Observation_value_kinds(string valueJson, string expected)
    {
        var json = """{"resourceType":"Observation","id":"o1",""" + valueJson[1..];

        Assert.Equal(expected, ObservationSummary.From(Parse(json))["value"]!.GetValue<string>());
    }

    [Fact]
    public void MedicationRequest_maps_core_fields()
    {
        var request = Parse("""
            {
              "resourceType": "MedicationRequest", "id": "med-ada-metformin",
              "status": "active", "intent": "order",
              "medicationCodeableConcept": { "coding": [ { "system": "http://www.nlm.nih.gov/research/umls/rxnorm", "code": "860975" } ], "text": "Metformin 500 mg tablet" },
              "subject": { "reference": "Patient/pat-ada" },
              "authoredOn": "2019-06-10",
              "reasonCode": [ { "text": "Type 2 diabetes mellitus" } ],
              "dosageInstruction": [ { "text": "500 mg twice daily with meals" } ]
            }
            """);

        Assert.Equal(
            """{"reference":"MedicationRequest/med-ada-metformin","medication":"Metformin 500 mg tablet","status":"active","intent":"order","authoredOn":"2019-06-10","dosage":"500 mg twice daily with meals","reason":"Type 2 diabetes mellitus"}""",
            SummaryJson.Serialize(MedicationRequestSummary.From(request)));
    }

    [Fact]
    public void MedicationRequest_with_a_medication_reference_uses_its_display()
    {
        var summary = MedicationRequestSummary.From(Parse(
            """{"resourceType":"MedicationRequest","id":"m1","medicationReference":{"reference":"Medication/x","display":"Lisinopril 10 mg"}}"""));

        Assert.Equal("Lisinopril 10 mg", summary["medication"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Condition")]
    [InlineData("Observation")]
    [InlineData("MedicationRequest")]
    [InlineData("DocumentReference")]
    public void Sparse_resources_do_not_throw(string resourceType)
    {
        var resource = Parse($$"""{"resourceType":"{{resourceType}}","id":"min"}""");

        var summary = resourceType switch
        {
            "Condition" => ConditionSummary.From(resource),
            "Observation" => ObservationSummary.From(resource),
            "MedicationRequest" => MedicationRequestSummary.From(resource),
            _ => DocumentReferenceSummary.From(resource, 500),
        };

        Assert.Equal($"{resourceType}/min", summary["reference"]!.GetValue<string>());
    }
}

public class DocumentReferenceSummaryTests
{
    private static JsonElement Document(string? contentType, string? data) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            resourceType = "DocumentReference",
            id = "doc-ada-clinic-2026-03",
            status = "current",
            type = new { text = "Clinic note" },
            date = "2026-03-14T10:30:00Z",
            author = new[] { new { display = "Dr Imaginary Example" } },
            description = "Diabetes review",
            content = new[] { new { attachment = new { contentType, data } } },
        })).RootElement;

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Decodes_inline_text()
    {
        var summary = DocumentReferenceSummary.From(Document("text/plain", Base64("Patient reports feeling well.")), 500);

        Assert.Equal(
            """{"reference":"DocumentReference/doc-ada-clinic-2026-03","type":"Clinic note","status":"current","date":"2026-03-14T10:30:00Z","author":"Dr Imaginary Example","description":"Diabetes review","contentType":"text/plain","text":"Patient reports feeling well.","textTruncated":false}""",
            SummaryJson.Serialize(summary));
    }

    [Fact]
    public void Cuts_long_text_and_says_so()
    {
        var summary = DocumentReferenceSummary.From(Document("text/plain; charset=utf-8", Base64(new string('a', 600))), 500);

        Assert.Equal(new string('a', 500) + " [truncated]", summary["text"]!.GetValue<string>());
        Assert.True(summary["textTruncated"]!.GetValue<bool>());
    }

    [Fact]
    public void Text_at_the_limit_is_not_cut()
    {
        var summary = DocumentReferenceSummary.From(Document("text/plain", Base64(new string('a', 500))), 500);

        Assert.Equal(500, summary["text"]!.GetValue<string>().Length);
        Assert.False(summary["textTruncated"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("text/plain", null)]               // no inline data
    [InlineData("text/plain", "not base64 !!!")]   // corrupt data
    [InlineData("application/pdf", "JVBERi0xLjQ=")] // not text
    [InlineData(null, "aGVsbG8=")]                 // unknown type
    public void Unreadable_content_is_reported_not_thrown(string? contentType, string? data)
    {
        var summary = DocumentReferenceSummary.From(Document(contentType, data), 500);

        Assert.Null(summary["text"]);
        Assert.Equal("No inline text is available for this document.", summary["textNote"]!.GetValue<string>());
        Assert.Equal("Clinic note", summary["type"]!.GetValue<string>());
    }
}
