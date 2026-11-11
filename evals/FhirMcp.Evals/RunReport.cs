using FhirMcp.Agent;

namespace FhirMcp.Evals;

/// <summary>The eval-results.json file: enough to see why every case passed or failed without re-running it.</summary>
public sealed record RunReport(
    string Model, int Passed, int Total, decimal Threshold, bool MeetsThreshold, IReadOnlyList<CaseRecord> Cases);

/// <summary>One case in the results file. Tool call result text is left out to keep the file small.</summary>
public sealed record CaseRecord(
    string Id, string Question, bool Passed, IReadOnlyList<string> Failures, string Answer,
    IReadOnlyList<string> Citations, IReadOnlyList<string> UngroundedCitations, string? Problem,
    IReadOnlyList<ToolCallSummary> ToolCalls)
{
    public static CaseRecord From(EvalCase evalCase, CaseResult result, AgentAnswer answer) => new(
        evalCase.Id, evalCase.Question, result.Passed, result.Failures, answer.Answer,
        answer.Citations, answer.UngroundedCitations, answer.Problem,
        answer.ToolCalls.Select(t => new ToolCallSummary(t.Name, t.ArgumentsJson, t.IsError)).ToList());
}

public sealed record ToolCallSummary(string Name, string Arguments, bool IsError);
