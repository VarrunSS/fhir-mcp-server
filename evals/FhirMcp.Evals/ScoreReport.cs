using System.Globalization;

namespace FhirMcp.Evals;

/// <summary>The text the runner prints and the pass/fail decision, kept pure so they can be unit tested.</summary>
public static class ScoreReport
{
    public static string CaseLine(CaseResult result) =>
        result.Passed ? $"PASS  {result.Id}" : $"FAIL  {result.Id}: {string.Join("; ", result.Failures)}";

    public static string ScoreLine(int passed, int total, decimal threshold)
    {
        var percent = total == 0 ? 0m : 100m * passed / total;
        return string.Create(CultureInfo.InvariantCulture,
            $"Score: {passed}/{total} ({percent:F1}%), threshold {threshold * 100m:F1}%");
    }

    /// <summary>At or above the threshold passes. Decimal arithmetic keeps "exactly 85%" exactly 85%. An empty run never passes.</summary>
    public static bool MeetsThreshold(int passed, int total, decimal threshold) =>
        total > 0 && passed >= threshold * total;
}
