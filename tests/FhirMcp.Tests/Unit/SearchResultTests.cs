using System.Text.Json;
using FhirMcp.Server.Summaries;

namespace FhirMcp.Tests.Unit;

public class SearchResultTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Wraps_summaries_with_counts()
    {
        var bundle = Parse("""
            {
              "resourceType": "Bundle", "type": "searchset", "total": 2,
              "link": [ { "relation": "self", "url": "http://localhost:8080/fhir/Patient?name=q" } ],
              "entry": [
                { "resource": { "resourceType": "Patient", "id": "a" } },
                { "resource": { "resourceType": "Patient", "id": "b" } }
              ]
            }
            """);

        var json = SearchResult.FromBundle(bundle, PatientSummary.From);

        var result = Parse(json);
        Assert.Equal(2, result.GetProperty("total").GetInt32());
        Assert.Equal(2, result.GetProperty("returned").GetInt32());
        Assert.False(result.GetProperty("hasMore").GetBoolean());
        Assert.Equal("Patient/b", result.GetProperty("results")[1].GetProperty("reference").GetString());
    }

    [Fact]
    public void Next_link_means_there_is_more()
    {
        var bundle = Parse("""
            {
              "resourceType": "Bundle", "type": "searchset", "total": 40,
              "link": [ { "relation": "next", "url": "http://localhost:8080/fhir?_getpages=abc" } ],
              "entry": [ { "resource": { "resourceType": "Patient", "id": "a" } } ]
            }
            """);

        var result = Parse(SearchResult.FromBundle(bundle, PatientSummary.From));

        Assert.True(result.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, result.GetProperty("returned").GetInt32());
    }

    [Fact]
    public void Empty_bundle_is_an_empty_result_not_an_error()
    {
        var result = Parse(SearchResult.FromBundle(
            Parse("""{"resourceType":"Bundle","type":"searchset","total":0}"""), PatientSummary.From));

        Assert.Equal(0, result.GetProperty("total").GetInt32());
        Assert.Equal(0, result.GetProperty("returned").GetInt32());
        Assert.Equal(0, result.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Missing_total_is_null_and_outcome_entries_are_skipped()
    {
        var bundle = Parse("""
            {
              "resourceType": "Bundle", "type": "searchset",
              "entry": [
                { "resource": { "resourceType": "Patient", "id": "a" } },
                { "resource": { "resourceType": "OperationOutcome", "issue": [] }, "search": { "mode": "outcome" } }
              ]
            }
            """);

        var result = Parse(SearchResult.FromBundle(bundle, PatientSummary.From));

        Assert.Equal(JsonValueKind.Null, result.GetProperty("total").ValueKind);
        Assert.Equal(1, result.GetProperty("returned").GetInt32());
    }
}
