using System.Text.Json;
using Anthropic.Models.Messages;
using ModelContextProtocol.Client;

namespace FhirMcp.Agent;

/// <summary>
/// Converts MCP tool definitions to Claude API tool definitions. Both describe inputs with
/// JSON Schema, so the name, description and the schema's "properties" and "required" are
/// copied across as they are; the MCP server stays the single source of truth for the tools.
/// </summary>
public static class McpToolBridge
{
    public static Tool ToClaudeTool(McpClientTool tool) =>
        ToClaudeTool(tool.Name, tool.Description, tool.JsonSchema);

    /// <summary>The pure part, split out so it can be tested without a running MCP server.</summary>
    public static Tool ToClaudeTool(string name, string? description, JsonElement schema)
    {
        var inputSchema = new InputSchema();

        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                // Clone: the JsonElements must outlive the document they were read from.
                inputSchema = inputSchema with
                {
                    Properties = properties.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                };
            }

            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                inputSchema = inputSchema with
                {
                    Required = required.EnumerateArray().Select(r => r.GetString()!).ToList(),
                };
            }
        }

        return new Tool { Name = name, Description = description, InputSchema = inputSchema };
    }
}
