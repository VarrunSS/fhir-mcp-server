using FhirMcp.Server.Fhir;
using ModelContextProtocol;

namespace FhirMcp.Server.Tools;

/// <summary>Behaviour every tool shares: error conversion and the result size limit.</summary>
public static class ToolRunner
{
    /// <summary>
    /// Runs a tool body. The MCP SDK shows the model an exception's message only when it is an
    /// McpException; anything else becomes a generic "an error occurred". So the two error kinds
    /// whose messages are written for the model (FHIR errors, bad arguments) are converted here,
    /// and unexpected exceptions are left alone so their details stay private.
    /// </summary>
    public static async Task<string> RunAsync(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (FhirRequestException ex)
        {
            throw new McpException(ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    /// <summary>Guardrail: a search never returns more than MaxCount results, whatever the model asks for.</summary>
    public static int ClampCount(int? count, FhirOptions options) =>
        Math.Clamp(count ?? options.DefaultCount, 1, options.MaxCount);
}
