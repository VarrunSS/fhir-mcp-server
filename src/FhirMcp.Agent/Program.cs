using Anthropic;
using Anthropic.Exceptions;
using FhirMcp.Agent;

const string Usage = "Usage: dotnet run --project src/FhirMcp.Agent -- [--patient <id>] \"<question>\"";

// Parse arguments: an optional --patient <id> and exactly one question.
string? patientId = null;
string? question = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--patient" && i + 1 < args.Length && patientId is null)
    {
        patientId = args[++i];
    }
    else if (!args[i].StartsWith("--", StringComparison.Ordinal) && question is null)
    {
        question = args[i];
    }
    else
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

if (string.IsNullOrWhiteSpace(question))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

// Checked before the MCP server starts, so a missing key costs nothing.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set; set it as an environment variable and run again.");
    return 2;
}

var options = new AgentOptions();
if (Environment.GetEnvironmentVariable("AGENT_MODEL") is { Length: > 0 } model)
{
    options = options with { Model = model };
}

try
{
    await using var mcp = await McpServerLauncher.StartAsync(Environment.GetEnvironmentVariable("FHIR_BASE_URL"));
    var agent = new ClinicalAgent(new AnthropicClient(), mcp, options);
    var result = await agent.AskAsync(question, patientId, CancellationToken.None);

    Console.WriteLine(result.Answer);
    Console.WriteLine();
    if (result.Problem is not null)
    {
        Console.WriteLine($"Problem: {result.Problem}");
    }
    else
    {
        Console.WriteLine($"Answerable: {(result.Answerable ? "yes" : "no")}");
        Console.WriteLine("Citations:");
        foreach (var citation in result.Citations)
        {
            Console.WriteLine($"  {citation}");
        }
        if (result.UngroundedCitations.Count > 0)
        {
            Console.WriteLine("WARNING: cited but never retrieved:");
            foreach (var citation in result.UngroundedCitations)
            {
                Console.WriteLine($"  {citation}");
            }
        }
    }

    Console.WriteLine("Tool calls:");
    foreach (var call in result.ToolCalls)
    {
        Console.WriteLine($"  {call.Name} {call.ArgumentsJson}{(call.IsError ? " ERROR" : "")}");
    }

    return result.Problem is null ? 0 : 1;
}
// Most specific first; the base AnthropicApiException catches any other API status.
catch (AnthropicUnauthorizedException)
{
    Console.Error.WriteLine("The Anthropic API rejected the API key (401). Check ANTHROPIC_API_KEY.");
}
catch (AnthropicNotFoundException)
{
    Console.Error.WriteLine($"The Anthropic API says model '{options.Model}' was not found (404). Check AGENT_MODEL.");
}
catch (AnthropicRateLimitException)
{
    Console.Error.WriteLine("The Anthropic API rate limit was hit (429). Wait a moment and try again.");
}
catch (Anthropic5xxException)
{
    Console.Error.WriteLine("The Anthropic API had a server error (5xx). Try again shortly.");
}
catch (AnthropicIOException)
{
    Console.Error.WriteLine("Could not reach the Anthropic API (network error).");
}
catch (AnthropicApiException ex)
{
    Console.Error.WriteLine($"The Anthropic API returned an error: {ex.Message}");
}
return 1;
