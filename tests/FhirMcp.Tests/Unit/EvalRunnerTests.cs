using System.Globalization;
using FhirMcp.Evals;

namespace FhirMcp.Tests.Unit;

/// <summary>The runner's pure parts: argument parsing and the score line. No API key needed.</summary>
public class EvalRunnerTests
{
    [Fact]
    public void No_arguments_gives_the_defaults()
    {
        var options = RunOptions.TryParse([], out var error);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Null(options.CasesPath);
        Assert.Equal(0.85m, options.Threshold);
        Assert.Null(options.Only);
        Assert.Equal("eval-results.json", options.OutPath);
    }

    [Fact]
    public void All_options_are_read()
    {
        var options = RunOptions.TryParse(
            ["--cases", "my.json", "--threshold", "0.9", "--only", "ada-01-latest-hba1c", "--out", "r.json"], out var error);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("my.json", options.CasesPath);
        Assert.Equal(0.9m, options.Threshold);
        Assert.Equal("ada-01-latest-hba1c", options.Only);
        Assert.Equal("r.json", options.OutPath);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--threshold")]
    [InlineData("--cases")]
    public void Unknown_option_or_missing_value_is_an_error(string argument)
    {
        var options = RunOptions.TryParse([argument], out var error);

        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-0.1")]
    [InlineData("1.5")]
    public void Threshold_must_be_a_fraction_between_0_and_1(string value)
    {
        var options = RunOptions.TryParse(["--threshold", value], out var error);

        Assert.Null(options);
        Assert.Contains("threshold", error);
    }

    [Fact]
    public void Score_line_has_the_documented_shape()
    {
        Assert.Equal("Score: 22/24 (91.7%), threshold 85.0%", ScoreReport.ScoreLine(22, 24, 0.85m));
    }

    [Fact]
    public void Score_line_ignores_the_current_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Score: 22/24 (91.7%), threshold 85.0%", ScoreReport.ScoreLine(22, 24, 0.85m));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Exactly_at_the_threshold_passes()
    {
        // 17 of 20 is exactly 85%.
        Assert.True(ScoreReport.MeetsThreshold(17, 20, 0.85m));
    }

    [Fact]
    public void Just_below_the_threshold_fails()
    {
        Assert.False(ScoreReport.MeetsThreshold(16, 20, 0.85m));
    }

    [Fact]
    public void An_empty_run_never_passes()
    {
        Assert.False(ScoreReport.MeetsThreshold(0, 0, 0m));
    }

    [Fact]
    public void Case_lines_use_PASS_and_FAIL_with_joined_failures()
    {
        Assert.Equal("PASS  ada-01", ScoreReport.CaseLine(new CaseResult("ada-01", true, [])));
        Assert.Equal("FAIL  ada-02: first; second",
            ScoreReport.CaseLine(new CaseResult("ada-02", false, ["first", "second"])));
    }
}
