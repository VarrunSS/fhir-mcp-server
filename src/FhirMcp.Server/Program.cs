using System.Net.Http.Headers;
using FhirMcp.Server.Fhir;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// The content root is the folder the server was built into, not the current directory,
// so appsettings.json is found whichever folder the MCP client starts the process from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

var fhirOptions = builder.Configuration.GetSection(FhirOptions.SectionName).Get<FhirOptions>() ?? new FhirOptions();

// FHIR_BASE_URL is the short form for MCP client configs; it wins over appsettings.json.
var baseUrlOverride = Environment.GetEnvironmentVariable("FHIR_BASE_URL");
if (!string.IsNullOrWhiteSpace(baseUrlOverride))
{
    fhirOptions.BaseUrl = baseUrlOverride;
}

// Guardrail: check the host allowlist before anything else starts. A bad setting stops the server.
Uri baseAddress;
try
{
    baseAddress = FhirBaseUrlValidator.Validate(fhirOptions);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"fhir-mcp-server cannot start: {ex.Message}");
    return 1;
}

builder.Services.AddSingleton(Options.Create(fhirOptions));
builder.Services.AddSingleton(_ =>
{
    // Redirects are not followed: a redirect could lead to a host that is not on the allowlist.
    var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
    var http = new HttpClient(handler)
    {
        BaseAddress = baseAddress,
        Timeout = TimeSpan.FromSeconds(30),
    };
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("fhir-mcp-server", "1.0"));
    return new FhirClient(http);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
