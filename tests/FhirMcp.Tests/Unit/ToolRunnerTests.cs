using FhirMcp.Server.Fhir;
using FhirMcp.Server.Tools;
using ModelContextProtocol;

namespace FhirMcp.Tests.Unit;

public class ToolRunnerTests
{
    private static readonly FhirOptions Options = new();

    [Theory]
    [InlineData(null, 10)]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public void ClampCount_applies_default_and_cap(int? requested, int expected)
    {
        Assert.Equal(expected, ToolRunner.ClampCount(requested, Options));
    }

    [Fact]
    public async Task Fhir_errors_become_tool_errors_with_the_same_message()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => ToolRunner.RunAsync(
            () => throw new FhirRequestException("The FHIR request failed with HTTP 404: error not-found: nope", 404)));

        Assert.Equal("The FHIR request failed with HTTP 404: error not-found: nope", error.Message);
    }

    [Fact]
    public async Task Invalid_arguments_become_tool_errors()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => ToolRunner.RunAsync(
            () => throw new ArgumentException("patientId must be a FHIR id")));

        Assert.Contains("patientId must be a FHIR id", error.Message);
    }

    [Fact]
    public async Task Unexpected_exceptions_are_not_converted()
    {
        // The MCP SDK replaces these with a generic message, so internals do not reach the model.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ToolRunner.RunAsync(
            () => throw new InvalidOperationException("internal detail")));
    }

    [Fact]
    public async Task Success_passes_through()
    {
        Assert.Equal("ok", await ToolRunner.RunAsync(() => Task.FromResult("ok")));
    }
}
