using System.Globalization;

namespace FhirMcp.Evals;

/// <summary>
/// Command-line options of the runner. Parsing is a pure function so it can be unit tested.
/// <paramref name="CasesPath"/> null means the cases.json next to the built assembly.
/// </summary>
public sealed record RunOptions(string? CasesPath, decimal Threshold, string? Only, string OutPath)
{
    public const string Usage =
        "Usage: dotnet run --project evals/FhirMcp.Evals -- [--cases <path>] [--threshold 0.85] [--only <case id>] [--out eval-results.json]";

    public static RunOptions? TryParse(string[] args, out string? error)
    {
        string? cases = null, only = null, outPath = "eval-results.json";
        var threshold = 0.85m;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || args[i] is not ("--cases" or "--threshold" or "--only" or "--out"))
            {
                error = $"Unknown or incomplete option '{args[i]}'. {Usage}";
                return null;
            }

            var value = args[++i];
            switch (args[i - 1])
            {
                case "--cases": cases = value; break;
                case "--only": only = value; break;
                case "--out": outPath = value; break;
                default:
                    // The threshold is a fraction: 0.85, not 85.
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out threshold)
                        || threshold is < 0m or > 1m)
                    {
                        error = $"--threshold must be a number from 0 to 1, got '{value}'.";
                        return null;
                    }
                    break;
            }
        }

        return new RunOptions(cases, threshold, only, outPath);
    }
}
