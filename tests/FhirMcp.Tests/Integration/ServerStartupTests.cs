using System.Collections.Concurrent;

namespace FhirMcp.Tests.Integration;

/// <summary>
/// Runs the built server as a real process. Needs no FHIR server: listing tools
/// and refusing to start never send a FHIR request.
/// </summary>
[Trait("Category", "Integration")]
public class ServerStartupTests
{
    [Fact]
    public async Task Lists_patient_tools_as_read_only()
    {
        await using var client = await McpServerProcess.StartAsync();

        var tools = await client.ListToolsAsync();

        foreach (var name in new[] { "search_patients", "read_patient" })
        {
            var tool = Assert.Single(tools, t => t.Name == name);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, $"{name} must be marked read-only");
        }
    }

    [Fact]
    public async Task Exposes_exactly_the_ten_tools_in_the_design()
    {
        await using var client = await McpServerProcess.StartAsync();

        var names = (await client.ListToolsAsync()).Select(t => t.Name).Order().ToArray();

        // A new tool, or a write tool, cannot appear without this list being changed on purpose.
        Assert.Equal(
            [
                "read_condition", "read_document_reference", "read_medication_request", "read_observation",
                "read_patient", "search_conditions", "search_document_references", "search_medication_requests",
                "search_observations", "search_patients",
            ],
            names);
    }

    [Fact]
    public async Task Every_tool_is_read_only_and_every_parameter_is_described()
    {
        await using var client = await McpServerProcess.StartAsync();

        var tools = await client.ListToolsAsync();

        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, $"{tool.Name} must be marked read-only");

            foreach (var parameter in tool.JsonSchema.GetProperty("properties").EnumerateObject())
            {
                Assert.True(
                    parameter.Value.TryGetProperty("description", out var description)
                    && !string.IsNullOrWhiteSpace(description.GetString()),
                    $"{tool.Name}.{parameter.Name} needs a description");
            }
        }
    }

    [Fact]
    public async Task Refuses_to_start_for_a_host_that_is_not_allowed()
    {
        var stderr = new ConcurrentQueue<string>();

        await Assert.ThrowsAnyAsync<Exception>(
            () => McpServerProcess.StartAsync("https://evil.example/fhir", stderr.Enqueue));

        Assert.Contains(stderr, line => line.Contains("not in the allowed hosts"));
    }
}
