using System.Text.Json;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace FhirMcp.Tests.Unit;

/// <summary>The patient-scoped tools: which FHIR query each one sends, and what it refuses.</summary>
public class ClinicalToolsTests
{
    private const string EmptyBundle = """{"resourceType":"Bundle","type":"searchset","total":0}""";

    private static readonly IOptions<FhirOptions> Opts = Options.Create(new FhirOptions());

    private static FhirClient Fhir(FakeHttpHandler fake) => new(fake.CreateClient());

    private static string Query(FakeHttpHandler fake) => Uri.UnescapeDataString(Assert.Single(fake.Requests).RequestUri!.PathAndQuery);

    [Fact]
    public async Task Conditions_search()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await new ConditionTools(Fhir(fake), Opts).SearchConditions("pat-ada", clinicalStatus: "active");

        Assert.Equal("/fhir/Condition?patient=pat-ada&clinical-status=active&_sort=-recorded-date&_count=10", Query(fake));
    }

    [Fact]
    public async Task Observations_search_with_all_filters()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await new ObservationTools(Fhir(fake), Opts).SearchObservations(
            "pat-ada", code: "4548-4", codeText: "HbA1c", category: "laboratory",
            dateFrom: "2026-01", dateTo: "2026-06-30", count: 5);

        Assert.Equal(
            "/fhir/Observation?patient=pat-ada&code=4548-4&code:text=HbA1c&category=laboratory&date=ge2026-01&date=le2026-06-30&_sort=-date&_count=5",
            Query(fake));
    }

    [Fact]
    public async Task Observations_search_minimal()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await new ObservationTools(Fhir(fake), Opts).SearchObservations("pat-ada");

        Assert.Equal("/fhir/Observation?patient=pat-ada&_sort=-date&_count=10", Query(fake));
    }

    [Fact]
    public async Task Medication_requests_search()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await new MedicationRequestTools(Fhir(fake), Opts).SearchMedicationRequests("pat-ada", status: "active");

        Assert.Equal("/fhir/MedicationRequest?patient=pat-ada&status=active&_sort=-authoredon&_count=10", Query(fake));
    }

    [Fact]
    public async Task Document_references_search()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await new DocumentReferenceTools(Fhir(fake), Opts).SearchDocumentReferences(
            "pat-ada", typeText: "Discharge summary", dateFrom: "2025");

        Assert.Equal(
            "/fhir/DocumentReference?patient=pat-ada&type:text=Discharge summary&date=ge2025&_sort=-date&_count=10",
            Query(fake));
    }

    [Fact]
    public async Task Reads_use_the_resource_path()
    {
        var cases = new (string Path, Func<FakeHttpHandler, Task<string>> Call)[]
        {
            ("/fhir/Condition/c-1", f => new ConditionTools(Fhir(f), Opts).ReadCondition("c-1")),
            ("/fhir/Observation/o-1", f => new ObservationTools(Fhir(f), Opts).ReadObservation("o-1")),
            ("/fhir/MedicationRequest/m-1", f => new MedicationRequestTools(Fhir(f), Opts).ReadMedicationRequest("m-1")),
            ("/fhir/DocumentReference/d-1", f => new DocumentReferenceTools(Fhir(f), Opts).ReadDocumentReference("d-1")),
        };

        foreach (var (path, call) in cases)
        {
            var type = path.Split('/')[2];
            var fake = FakeHttpHandler.Json($$"""{"resourceType":"{{type}}","id":"{{path.Split('/')[3]}}"}""");

            var json = await call(fake);

            Assert.Equal(path, Query(fake));
            Assert.Equal(path[6..], JsonDocument.Parse(json).RootElement.GetProperty("reference").GetString());
        }
    }

    [Fact]
    public async Task Document_search_previews_text_and_read_returns_more()
    {
        var data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string('n', 900)));
        var doc = """{"resourceType":"DocumentReference","id":"d-1","content":[{"attachment":{"contentType":"text/plain","data":"""
            + "\"" + data + "\"}}]}";
        var tools = (FakeHttpHandler f) => new DocumentReferenceTools(Fhir(f), Opts);

        var bundle = """{"resourceType":"Bundle","total":1,"entry":[{"resource":""" + doc + "}]}";
        var searchJson = await tools(FakeHttpHandler.Json(bundle)).SearchDocumentReferences("pat-ada");
        var readJson = await tools(FakeHttpHandler.Json(doc)).ReadDocumentReference("d-1");

        var preview = JsonDocument.Parse(searchJson).RootElement.GetProperty("results")[0];
        Assert.True(preview.GetProperty("textTruncated").GetBoolean());
        Assert.Equal(500 + " [truncated]".Length, preview.GetProperty("text").GetString()!.Length);

        var full = JsonDocument.Parse(readJson).RootElement;
        Assert.False(full.GetProperty("textTruncated").GetBoolean());
        Assert.Equal(900, full.GetProperty("text").GetString()!.Length);
    }

    [Theory]
    [InlineData("last week")]
    [InlineData("2026-3-1")]
    [InlineData("14/03/2026")]
    [InlineData("2026-03-14T10:00:00Z")]
    [InlineData("ge2026")]
    public async Task Bad_dates_are_tool_errors_without_a_request(string date)
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        var error = await Assert.ThrowsAsync<McpException>(
            () => new ObservationTools(Fhir(fake), Opts).SearchObservations("pat-ada", dateFrom: date));

        Assert.Contains("dateFrom", error.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Bad_patient_ids_are_tool_errors_without_a_request()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);
        var calls = new Func<Task<string>>[]
        {
            () => new ConditionTools(Fhir(fake), Opts).SearchConditions("pat/1"),
            () => new ObservationTools(Fhir(fake), Opts).SearchObservations("pat&_count=999"),
            () => new MedicationRequestTools(Fhir(fake), Opts).SearchMedicationRequests(""),
            () => new DocumentReferenceTools(Fhir(fake), Opts).SearchDocumentReferences("Patient/pat-ada"),
        };

        foreach (var call in calls)
        {
            var error = await Assert.ThrowsAsync<McpException>(call);
            Assert.Contains("patientId", error.Message);
        }

        Assert.Empty(fake.Requests);
    }
}
