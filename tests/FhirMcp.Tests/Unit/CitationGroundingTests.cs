using FhirMcp.Agent;

namespace FhirMcp.Tests.Unit;

public class CitationGroundingTests
{
    private const string OneCondition = """{"reference":"Condition/cond-ada-t2dm","display":"Type 2 diabetes"}""";

    private const string SearchEnvelope = """
        {"total":2,"returned":2,"hasMore":false,"results":[
          {"reference":"Observation/obs-1","value":7.2},
          {"reference":"Observation/obs-2","value":8.1}]}
        """;

    [Fact]
    public void Reference_in_a_tool_result_is_retrieved()
    {
        var retrieved = CitationGrounding.RetrievedReferences([OneCondition]);

        Assert.Contains("Condition/cond-ada-t2dm", retrieved);
    }

    [Fact]
    public void Citation_never_returned_by_a_tool_is_ungrounded()
    {
        var ungrounded = CitationGrounding.FindUngrounded(
            ["Condition/cond-ada-t2dm", "Observation/obs-never-fetched"], [OneCondition]);

        Assert.Equal(["Observation/obs-never-fetched"], ungrounded);
    }

    [Fact]
    public void Every_result_in_a_search_envelope_is_counted()
    {
        var retrieved = CitationGrounding.RetrievedReferences([SearchEnvelope]);

        Assert.Equal(new[] { "Observation/obs-1", "Observation/obs-2" }, retrieved.Order());
    }

    [Fact]
    public void Non_json_text_is_ignored_without_throwing()
    {
        var retrieved = CitationGrounding.RetrievedReferences(["HTTP 404 from FHIR server", "{ not json", "", OneCondition]);

        Assert.Equal(["Condition/cond-ada-t2dm"], retrieved);
    }

    [Fact]
    public void Comparison_is_exact_and_case_sensitive()
    {
        var ungrounded = CitationGrounding.FindUngrounded(
            ["condition/cond-ada-t2dm", "Condition/COND-ADA-T2DM", "Condition/cond-ada-t2dm "], [OneCondition]);

        Assert.Equal(3, ungrounded.Count);
    }

    [Fact]
    public void Only_properties_named_reference_count_and_only_string_values()
    {
        const string text = """{"subject":"Patient/pat-ada","reference":"Observation/obs-1","nested":{"reference":42}}""";

        var retrieved = CitationGrounding.RetrievedReferences([text]);

        Assert.Equal(["Observation/obs-1"], retrieved);
    }

    [Fact]
    public void Nothing_cited_means_nothing_ungrounded()
    {
        Assert.Empty(CitationGrounding.FindUngrounded([], [OneCondition]));
    }
}
