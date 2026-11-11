using System.Text.Json;

namespace FhirMcp.Evals;

/// <summary>
/// One golden question. <paramref name="MustContain"/> is a list of facts; each fact is a list of
/// accepted wordings, and any one of them satisfies it. <paramref name="MustCite"/> holds the exact
/// <c>ResourceType/id</c> references the answer has to cite; <paramref name="MustNotContain"/> holds
/// strings that must not appear. Unanswerable cases leave the first two empty.
/// </summary>
public sealed record EvalCase(
    string Id, string PatientId, string Question, bool Answerable,
    IReadOnlyList<IReadOnlyList<string>> MustContain,
    IReadOnlyList<string> MustCite, IReadOnlyList<string> MustNotContain)
{
    /// <summary>For the human reader of cases.json: what this case is testing. Never scored.</summary>
    public string? Notes { get; init; }

    /// <summary>True for the one case whose patient id deliberately does not exist, so file checks can exempt it.</summary>
    public bool UnknownPatient { get; init; }
}

/// <summary>The verdict for one case: passed when there are no failures.</summary>
public sealed record CaseResult(string Id, bool Passed, IReadOnlyList<string> Failures);

/// <summary>Reads cases.json (camelCase property names).</summary>
public static class EvalCaseFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<EvalCase> Load(string path) =>
        JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} is empty.");
}
