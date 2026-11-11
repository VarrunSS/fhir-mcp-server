using FhirMcp.Agent;
using FhirMcp.Evals;

namespace FhirMcp.Tests.Unit;

public class EvalScorerTests
{
    private static readonly EvalCase LatestHba1c = new(
        "ada-01-latest-hba1c", "pat-ada", "What is the latest HbA1c?", true,
        MustContain: [["7.2"], ["2026-03-18", "18 March 2026"]],
        MustCite: ["Observation/obs-ada-hba1c-2026-03"],
        MustNotContain: []);

    private static readonly EvalCase NotInRecord = new(
        "ada-07-allergies", "pat-ada", "Does the patient have any allergies?", false,
        MustContain: [], MustCite: [], MustNotContain: ["NKDA"]);

    private static AgentAnswer Answer(
        string text, bool answerable = true, string[]? citations = null,
        string[]? ungrounded = null, string? problem = null) =>
        new(text, answerable, citations ?? [], ungrounded ?? [], [], problem);

    private static AgentAnswer CorrectHba1c() => Answer(
        "The latest HbA1c is 7.2 % (2026-03-18).", citations: ["Observation/obs-ada-hba1c-2026-03"]);

    [Fact]
    public void Correct_answer_passes()
    {
        var result = EvalScorer.Score(LatestHba1c, CorrectHba1c());

        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
        Assert.Equal("ada-01-latest-hba1c", result.Id);
    }

    [Fact]
    public void Problem_fails_the_case()
    {
        var result = EvalScorer.Score(LatestHba1c, Answer("The model declined.", answerable: false, problem: "refusal"));

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, f => f.Contains("refusal"));
    }

    [Fact]
    public void Answerable_mismatch_fails_in_both_directions()
    {
        var saidNo = EvalScorer.Score(LatestHba1c,
            Answer("7.2 on 2026-03-18", answerable: false, citations: ["Observation/obs-ada-hba1c-2026-03"]));
        var saidYes = EvalScorer.Score(NotInRecord, Answer("No allergies are recorded.", answerable: true));

        Assert.Contains(saidNo.Failures, f => f.Contains("answerable"));
        Assert.Contains(saidYes.Failures, f => f.Contains("answerable"));
    }

    [Fact]
    public void Missing_fact_fails_and_names_the_fact()
    {
        var result = EvalScorer.Score(LatestHba1c,
            Answer("The latest HbA1c is 7.2 %.", citations: ["Observation/obs-ada-hba1c-2026-03"]));

        var failure = Assert.Single(result.Failures);
        Assert.Contains("2026-03-18", failure);
    }

    [Fact]
    public void Any_one_variant_satisfies_a_fact()
    {
        var result = EvalScorer.Score(LatestHba1c,
            Answer("HbA1c was 7.2% on 18 March 2026.", citations: ["Observation/obs-ada-hba1c-2026-03"]));

        Assert.True(result.Passed);
    }

    [Fact]
    public void Matching_ignores_case_and_collapses_whitespace()
    {
        var evalCase = LatestHba1c with { MustContain = [["blood  pressure 138"]] };
        var result = EvalScorer.Score(evalCase,
            Answer("BLOOD\n  PRESSURE\t138 over 84", citations: ["Observation/obs-ada-hba1c-2026-03"]));

        Assert.True(result.Passed);
    }

    [Fact]
    public void Missing_citation_fails_and_names_the_reference()
    {
        var result = EvalScorer.Score(LatestHba1c, Answer("7.2 % on 2026-03-18", citations: []));

        var failure = Assert.Single(result.Failures);
        Assert.Contains("Observation/obs-ada-hba1c-2026-03", failure);
    }

    [Fact]
    public void Citation_must_match_exactly()
    {
        var result = EvalScorer.Score(LatestHba1c,
            Answer("7.2 % on 2026-03-18", citations: ["observation/obs-ada-hba1c-2026-03"]));

        Assert.False(result.Passed);
    }

    [Fact]
    public void Forbidden_text_fails()
    {
        var result = EvalScorer.Score(NotInRecord,
            Answer("The record says nkda.", answerable: false));

        var failure = Assert.Single(result.Failures);
        Assert.Contains("NKDA", failure);
    }

    [Fact]
    public void Ungrounded_citation_fails()
    {
        var result = EvalScorer.Score(LatestHba1c, Answer(
            "7.2 % on 2026-03-18",
            citations: ["Observation/obs-ada-hba1c-2026-03", "Observation/obs-made-up"],
            ungrounded: ["Observation/obs-made-up"]));

        var failure = Assert.Single(result.Failures);
        Assert.Contains("Observation/obs-made-up", failure);
    }

    [Fact]
    public void Unanswerable_case_passes_with_empty_citations()
    {
        var result = EvalScorer.Score(NotInRecord,
            Answer("The record contains no allergy information.", answerable: false));

        Assert.True(result.Passed);
    }

    [Fact]
    public void Every_failed_rule_adds_its_own_failure()
    {
        var result = EvalScorer.Score(LatestHba1c, Answer("Nothing useful.", answerable: false));

        // answerable mismatch + two missing facts + one missing citation
        Assert.Equal(4, result.Failures.Count);
    }
}
