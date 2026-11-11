using ModelContextProtocol.Client;

namespace FhirMcp.Tests.Integration;

/// <summary>
/// Shared by every test in a class: checks that the local HAPI server answers, then starts
/// one real MCP server process pointed at it. Starting a process per test would be slow.
/// </summary>
public sealed class HapiFixture : IAsyncLifetime
{
    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("FHIR_BASE_URL") is { Length: > 0 } url ? url : "http://localhost:8080/fhir";

    public McpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await EnsureHapiIsReachableAsync();
        Client = await McpServerProcess.StartAsync(BaseUrl);
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }
    }

    private async Task EnsureHapiIsReachableAsync()
    {
        var message = $"HAPI is not reachable at {BaseUrl}. Run: docker compose up -d, then the seed tool.";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync($"{BaseUrl.TrimEnd('/')}/metadata");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(message);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(message, ex);
        }
    }
}
