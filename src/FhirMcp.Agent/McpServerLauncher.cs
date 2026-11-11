using ModelContextProtocol.Client;

namespace FhirMcp.Agent;

/// <summary>
/// Starts the real MCP server as a child process and connects over stdio, as Claude Code would.
/// The agent project references the server project only so the server's build output sits next
/// to the agent's. The eval runner reuses this to talk to the same server.
/// </summary>
public static class McpServerLauncher
{
    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "FhirMcp.Server.dll");

    public static Task<McpClient> StartAsync(string? fhirBaseUrl = null)
    {
        var options = new StdioClientTransportOptions
        {
            Name = "fhir-mcp-server",
            Command = "dotnet",
            Arguments = [ServerDll],
        };

        // The server picks its FHIR base URL from its own config unless overridden here.
        if (fhirBaseUrl is not null)
        {
            options.EnvironmentVariables = new Dictionary<string, string?> { ["FHIR_BASE_URL"] = fhirBaseUrl };
        }

        return McpClient.CreateAsync(new StdioClientTransport(options));
    }
}
