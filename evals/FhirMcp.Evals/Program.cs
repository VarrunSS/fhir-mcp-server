using System.Text.Json;
using Anthropic;
using FhirMcp.Agent;
using FhirMcp.Evals;

// Order matters: cheap checks first, so a mistake costs nothing. Nothing is started before the key check.
var options = RunOptions.TryParse(args, out var parseError);
if (options is null)
{
    Console.Error.WriteLine(parseError);
    return 2;
}

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set; set it as an environment variable and run again.");
    return 2;
}

var casesPath = options.CasesPath ?? Path.Combine(AppContext.BaseDirectory, "cases.json");
List<EvalCase> cases;
try
{
    cases = EvalCaseFile.Load(casesPath).Where(c => options.Only is null || c.Id == options.Only).ToList();
}
catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
{
    Console.Error.WriteLine($"Could not read cases from {casesPath}: {ex.Message}");
    return 2;
}

if (cases.Count == 0)
{
    Console.Error.WriteLine(options.Only is null ? $"{casesPath} has no cases." : $"No case with id '{options.Only}' in {casesPath}.");
    return 2;
}

var agentOptions = new AgentOptions();
if (Environment.GetEnvironmentVariable("AGENT_MODEL") is { Length: > 0 } model)
{
    agentOptions = agentOptions with { Model = model };
}

// One MCP server and one agent for the whole run; cases run one after another.
await using var mcp = await McpServerLauncher.StartAsync(Environment.GetEnvironmentVariable("FHIR_BASE_URL"));
var agent = new ClinicalAgent(new AnthropicClient(), mcp, agentOptions);

var records = new List<CaseRecord>();
foreach (var evalCase in cases)
{
    AgentAnswer answer;
    CaseResult result;
    try
    {
        answer = await agent.AskAsync(evalCase.Question, evalCase.PatientId, CancellationToken.None);
        result = EvalScorer.Score(evalCase, answer);
    }
    catch (Exception ex)
    {
        // A broken case (network, API error) is a failed case, not the end of the run.
        answer = new AgentAnswer("", false, [], [], [], $"exception: {ex.Message}");
        result = new CaseResult(evalCase.Id, false, [$"exception: {ex.Message}"]);
    }

    Console.WriteLine(ScoreReport.CaseLine(result));
    records.Add(CaseRecord.From(evalCase, result, answer));
}

var passed = records.Count(r => r.Passed);
var meets = ScoreReport.MeetsThreshold(passed, records.Count, options.Threshold);

Console.WriteLine(ScoreReport.ScoreLine(passed, records.Count, options.Threshold));
Console.WriteLine(meets ? "RESULT: PASS" : "RESULT: FAIL");

var report = new RunReport(agentOptions.Model, passed, records.Count, options.Threshold, meets, records);
File.WriteAllText(options.OutPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

return meets ? 0 : 1;
