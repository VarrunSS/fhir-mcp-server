using FhirMcp.Agent;

namespace FhirMcp.Tests.Unit;

public class AgentAnswerParserTests
{
    [Fact]
    public void Valid_json_is_parsed()
    {
        var parsed = AgentAnswerParser.TryParse(
            """{"answer":"HbA1c 7.2 % on 2026-03-10.","answerable":true,"citations":["Observation/obs-ada-hba1c-2026-03"]}""");

        Assert.NotNull(parsed);
        Assert.Equal("HbA1c 7.2 % on 2026-03-10.", parsed.Answer);
        Assert.True(parsed.Answerable);
        Assert.Equal(["Observation/obs-ada-hba1c-2026-03"], parsed.Citations);
    }

    [Fact]
    public void Surrounding_whitespace_is_fine()
    {
        var parsed = AgentAnswerParser.TryParse("\n  \t{ \"answer\" : \"x\" ,\n \"answerable\": false , \"citations\" : [ ] }  \n");

        Assert.NotNull(parsed);
        Assert.False(parsed.Answerable);
        Assert.Empty(parsed.Citations);
    }

    [Theory]
    [InlineData("""{"answerable":true,"citations":[]}""")]
    [InlineData("""{"answer":"x","citations":[]}""")]
    [InlineData("""{"answer":"x","answerable":true}""")]
    public void A_missing_field_is_malformed(string json)
    {
        Assert.Null(AgentAnswerParser.TryParse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("The answer is 7.2")]
    [InlineData("{ \"answer\": ")]
    [InlineData("[1,2]")]
    public void Text_that_is_not_a_json_object_is_malformed(string text)
    {
        Assert.Null(AgentAnswerParser.TryParse(text));
    }

    [Theory]
    [InlineData("""{"answer":"x","answerable":true,"citations":"Observation/a"}""")]
    [InlineData("""{"answer":"x","answerable":true,"citations":[1,2]}""")]
    [InlineData("""{"answer":"x","answerable":"yes","citations":[]}""")]
    [InlineData("""{"answer":5,"answerable":true,"citations":[]}""")]
    public void Wrongly_typed_fields_are_malformed(string json)
    {
        Assert.Null(AgentAnswerParser.TryParse(json));
    }
}
