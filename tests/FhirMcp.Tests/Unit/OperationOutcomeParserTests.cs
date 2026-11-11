using FhirMcp.Server.Fhir;

namespace FhirMcp.Tests.Unit;

public class OperationOutcomeParserTests
{
    [Fact]
    public void Describes_a_single_issue()
    {
        const string body = """
            {"resourceType":"OperationOutcome","issue":[
              {"severity":"error","code":"not-found","diagnostics":"Resource Patient/zzz is not known"}]}
            """;

        Assert.Equal("error not-found: Resource Patient/zzz is not known", OperationOutcomeParser.Describe(body));
    }

    [Fact]
    public void Joins_several_issues()
    {
        const string body = """
            {"resourceType":"OperationOutcome","issue":[
              {"severity":"error","code":"invalid","diagnostics":"Unknown search parameter 'foo'"},
              {"severity":"warning","code":"informational","diagnostics":"Something else"}]}
            """;

        Assert.Equal(
            "error invalid: Unknown search parameter 'foo'; warning informational: Something else",
            OperationOutcomeParser.Describe(body));
    }

    [Fact]
    public void Falls_back_to_details_text_then_to_code_alone()
    {
        const string body = """
            {"resourceType":"OperationOutcome","issue":[
              {"severity":"error","code":"processing","details":{"text":"From details"}},
              {"severity":"error","code":"timeout"}]}
            """;

        Assert.Equal("error processing: From details; error timeout", OperationOutcomeParser.Describe(body));
    }

    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"pat-ada"}""")]
    [InlineData("<html><body>Bad gateway</body></html>")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"resourceType":"OperationOutcome"}""")]
    public void Returns_null_when_there_is_nothing_to_describe(string body)
    {
        Assert.Null(OperationOutcomeParser.Describe(body));
    }
}
