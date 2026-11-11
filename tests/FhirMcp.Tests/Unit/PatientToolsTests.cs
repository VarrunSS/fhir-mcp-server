using System.Text.Json;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Tools;
using ModelContextProtocol;

namespace FhirMcp.Tests.Unit;

public class PatientToolsTests
{
    private const string EmptyBundle = """{"resourceType":"Bundle","type":"searchset","total":0}""";

    private static PatientTools Tools(FakeHttpHandler fake) =>
        new(new FhirClient(fake.CreateClient()), Microsoft.Extensions.Options.Options.Create(new FhirOptions()));

    [Fact]
    public async Task Search_sends_only_supplied_parameters()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await Tools(fake).SearchPatients(name: "Quillfeather");

        Assert.Equal("?name=Quillfeather&_count=10", Assert.Single(fake.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task Search_maps_all_parameters_and_caps_count()
    {
        var fake = FakeHttpHandler.Json(EmptyBundle);

        await Tools(fake).SearchPatients(name: "Ada", birthDate: "1961-04-12", identifier: "MRN-0001", count: 999);

        Assert.Equal(
            "?name=Ada&birthdate=1961-04-12&identifier=MRN-0001&_count=50",
            Assert.Single(fake.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task Search_returns_the_result_envelope()
    {
        var fake = FakeHttpHandler.Json("""
            {"resourceType":"Bundle","type":"searchset","total":1,
             "entry":[{"resource":{"resourceType":"Patient","id":"pat-ada","gender":"female"}}]}
            """);

        var json = await Tools(fake).SearchPatients(name: "Ada");

        var result = JsonDocument.Parse(json).RootElement;
        Assert.Equal(1, result.GetProperty("returned").GetInt32());
        Assert.Equal("Patient/pat-ada", result.GetProperty("results")[0].GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Read_returns_a_summary()
    {
        var fake = FakeHttpHandler.Json("""{"resourceType":"Patient","id":"pat-ada","birthDate":"1961-04-12"}""");

        var json = await Tools(fake).ReadPatient("pat-ada");

        Assert.Equal("/fhir/Patient/pat-ada", Assert.Single(fake.Requests).RequestUri!.AbsolutePath);
        Assert.Equal("1961-04-12", JsonDocument.Parse(json).RootElement.GetProperty("birthDate").GetString());
    }

    [Fact]
    public async Task Read_rejects_a_bad_id_as_a_tool_error_without_a_request()
    {
        var fake = FakeHttpHandler.Json("{}");

        var error = await Assert.ThrowsAsync<McpException>(() => Tools(fake).ReadPatient("a/b"));

        Assert.Contains("patientId", error.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Not_found_is_a_tool_error_with_the_outcome_text()
    {
        var fake = FakeHttpHandler.Json(
            """{"resourceType":"OperationOutcome","issue":[{"severity":"error","code":"not-found","diagnostics":"Resource Patient/zzz is not known"}]}""",
            System.Net.HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<McpException>(() => Tools(fake).ReadPatient("zzz"));

        Assert.Contains("404", error.Message);
        Assert.Contains("Resource Patient/zzz is not known", error.Message);
    }
}
