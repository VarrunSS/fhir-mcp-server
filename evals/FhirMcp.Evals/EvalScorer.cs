using System.Text.RegularExpressions;
using FhirMcp.Agent;

namespace FhirMcp.Evals;

/// <summary>
/// Scores one answer against one case with plain string checks: no fuzzy matching and no model
/// acting as judge, so the same input always gives the same verdict and a failure says exactly
/// which rule broke. It is strict on purpose; if a correct answer fails, widen the case's
/// accepted wordings, never the rules.
/// </summary>
public static partial class EvalScorer
{
    public static CaseResult Score(EvalCase c, AgentAnswer a)
    {
        // Rule 1: a run that ended in a refusal, turn limit or bad JSON has no answer worth scoring,
        // and the other rules would only repeat the same failure in noisier words.
        if (a.Problem is not null)
        {
            return new CaseResult(c.Id, false, [$"agent problem: {a.Problem}"]);
        }

        var failures = new List<string>();

        // Rule 2: did the agent make the right call on whether the record holds the answer?
        if (a.Answerable != c.Answerable)
        {
            failures.Add($"answerable: expected {c.Answerable.ToString().ToLowerInvariant()}, got {a.Answerable.ToString().ToLowerInvariant()}");
        }

        var answer = Normalize(a.Answer);

        // Rule 3: every fact needs at least one of its accepted wordings in the answer.
        foreach (var fact in c.MustContain)
        {
            if (!fact.Any(variant => answer.Contains(Normalize(variant), StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"missing fact: none of [{string.Join(" | ", fact)}] found in the answer");
            }
        }

        // Rule 4: required citations must be present, spelled exactly.
        foreach (var reference in c.MustCite.Where(r => !a.Citations.Contains(r, StringComparer.Ordinal)))
        {
            failures.Add($"missing citation: {reference}");
        }

        // Rule 5: strings that would only appear in an invented or wrong answer.
        foreach (var banned in c.MustNotContain.Where(b => answer.Contains(Normalize(b), StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"forbidden text present: \"{banned}\"");
        }

        // Rule 6: the agent cited something no tool call ever returned.
        if (a.UngroundedCitations.Count > 0)
        {
            failures.Add($"ungrounded citations: {string.Join(", ", a.UngroundedCitations)}");
        }

        return new CaseResult(c.Id, failures.Count == 0, failures);
    }

    /// <summary>Collapses every run of whitespace (newlines and tabs included) into one space and trims.</summary>
    public static string Normalize(string text) => WhitespaceRun().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
