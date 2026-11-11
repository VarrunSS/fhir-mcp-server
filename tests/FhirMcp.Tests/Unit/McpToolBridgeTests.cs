using System.Text.Json;
using FhirMcp.Agent;

namespace FhirMcp.Tests.Unit;

public class McpToolBridgeTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "patientId": { "type": "string", "description": "The patient's FHIR id." },
            "count": { "type": "integer" }
          },
          "required": ["patientId"]
        }
        """).RootElement;

    [Fact]
    public void Name_and_description_are_copied()
    {
        var tool = McpToolBridge.ToClaudeTool("search_conditions", "Find conditions.", Schema);

        Assert.Equal("search_conditions", tool.Name);
        Assert.Equal("Find conditions.", tool.Description);
    }

    [Fact]
    public void Properties_and_required_are_copied()
    {
        var tool = McpToolBridge.ToClaudeTool("search_conditions", "Find conditions.", Schema);

        Assert.Equal(["count", "patientId"], tool.InputSchema.Properties!.Keys.Order());
        Assert.Equal("string", tool.InputSchema.Properties["patientId"].GetProperty("type").GetString());
        Assert.Equal(["patientId"], tool.InputSchema.Required!);
    }

    [Fact]
    public void Schema_without_properties_or_required_gives_an_empty_input_schema()
    {
        var empty = JsonDocument.Parse("""{"type":"object"}""").RootElement;

        var tool = McpToolBridge.ToClaudeTool("ping", null, empty);

        Assert.Null(tool.Description);
        Assert.True(tool.InputSchema.Properties is null || tool.InputSchema.Properties.Count == 0);
        Assert.True(tool.InputSchema.Required is null || tool.InputSchema.Required.Count == 0);
    }
}
