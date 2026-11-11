using ModelContextProtocol.Client;

namespace FhirMcp.Tests.Integration;

/// <summary>
/// Starts the real server as a child process and connects to it over stdio,
/// exactly as Claude Code or the Q&amp;A agent would.
/// </summary>
public static class McpServerProcess
{
    // The test project references the server project, so the server's build output
    // sits next to the test assembly.
    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "FhirMcp.Server.dll");

    public static Task<McpClient> StartAsync(string? fhirBaseUrl = null, Action<string>? onStderrLine = null)
    {
        var options = new StdioClientTransportOptions
        {
            Name = "fhir-mcp-server",
            Command = "dotnet",
            Arguments = [ServerDll],
            StandardErrorLines = onStderrLine,
        };

        if (fhirBaseUrl is not null)
        {
            options.EnvironmentVariables = new Dictionary<string, string?> { ["FHIR_BASE_URL"] = fhirBaseUrl };
        }

        return McpClient.CreateAsync(new StdioClientTransport(options));
    }
}
