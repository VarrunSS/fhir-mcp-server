using System.Text;
using System.Text.Json;
using FhirMcp.Evals;

namespace FhirMcp.Tests.Unit;

/// <summary>
/// Checks cases.json against the synthetic bundles in data/. Needs no API key and no HAPI:
/// a case that names a resource or a value that is not in the data can never pass, so it is
/// caught here, long before an eval run costs anything.
/// </summary>
public class EvalCasesFileTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly IReadOnlyList<EvalCase> Cases = EvalCaseFile.Load(
        Path.Combine(RepoRoot, "evals", "FhirMcp.Evals", "cases.json"));

    /// <summary>Every resource in data/*.json by "ResourceType/id": its owning patient reference and its searchable text.</summary>
    private static readonly Dictionary<string, (string Patient, string Text)> Resources = LoadResources();

    [Fact]
    public void File_parses_and_has_24_cases()
    {
        Assert.Equal(24, Cases.Count);
    }

    [Fact]
    public void Case_ids_are_unique_and_non_empty()
    {
        Assert.All(Cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        Assert.Equal(Cases.Count, Cases.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Every_case_has_a_question()
    {
        Assert.All(Cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Question), c.Id));
    }

    [Fact]
    public void At_least_six_cases_are_unanswerable()
    {
        Assert.True(Cases.Count(c => !c.Answerable) >= 6);
    }

    [Fact]
    public void Exactly_one_case_uses_an_unknown_patient_and_it_is_marked()
    {
        var unknown = Assert.Single(Cases, c => c.PatientId == "pat-does-not-exist");

        Assert.True(unknown.UnknownPatient);
        Assert.False(unknown.Answerable);
        Assert.Single(Cases, c => c.UnknownPatient);
    }

    [Fact]
    public void Every_patient_id_exists_in_the_data_unless_marked_unknown()
    {
        foreach (var c in Cases.Where(c => !c.UnknownPatient))
        {
            Assert.True(Resources.ContainsKey($"Patient/{c.PatientId}"), $"{c.Id}: no Patient/{c.PatientId} in data/");
        }
    }

    [Fact]
    public void Every_mustCite_reference_exists_and_belongs_to_the_case_patient()
    {
        foreach (var c in Cases)
        {
            foreach (var reference in c.MustCite)
            {
                Assert.True(Resources.TryGetValue(reference, out var resource), $"{c.Id}: {reference} is not in data/");
                Assert.True(resource.Patient == $"Patient/{c.PatientId}",
                    $"{c.Id}: {reference} belongs to {resource.Patient}, not Patient/{c.PatientId}");
            }
        }
    }

    [Fact]
    public void Answerable_cases_need_a_fact_and_a_citation()
    {
        foreach (var c in Cases.Where(c => c.Answerable))
        {
            Assert.True(c.MustContain.Count >= 1, $"{c.Id}: no mustContain");
            Assert.True(c.MustCite.Count >= 1, $"{c.Id}: no mustCite");
        }
    }

    [Fact]
    public void Unanswerable_cases_expect_no_facts_and_no_citations()
    {
        foreach (var c in Cases.Where(c => !c.Answerable))
        {
            Assert.Empty(c.MustContain);
            Assert.Empty(c.MustCite);
        }
    }

    [Fact]
    public void No_variant_is_empty_or_a_single_character()
    {
        foreach (var c in Cases)
        {
            foreach (var fact in c.MustContain)
            {
                Assert.NotEmpty(fact);
                Assert.All(fact, v => Assert.True(EvalScorer.Normalize(v).Length >= 2, $"{c.Id}: variant '{v}' is too short"));
            }
            Assert.All(c.MustNotContain, v => Assert.True(EvalScorer.Normalize(v).Length >= 2, $"{c.Id}: banned '{v}' is too short"));
        }
    }

    [Fact]
    public void At_least_six_cases_cite_a_document_reference()
    {
        Assert.True(Cases.Count(c => c.MustCite.Any(r => r.StartsWith("DocumentReference/", StringComparison.Ordinal))) >= 6);
    }

    /// <summary>
    /// Makes the cases honest: each expected fact must really occur in the resources the case
    /// cites (their JSON, plus the decoded note text for a DocumentReference). A case whose
    /// expected value is not in the record could never be passed by a correct answer.
    /// </summary>
    [Fact]
    public void Every_expected_fact_occurs_in_the_cited_resources()
    {
        foreach (var c in Cases.Where(c => c.Answerable))
        {
            var citedText = EvalScorer.Normalize(string.Join("\n", c.MustCite.Select(r => Resources[r].Text)));
            foreach (var fact in c.MustContain)
            {
                Assert.True(
                    fact.Any(variant => citedText.Contains(EvalScorer.Normalize(variant), StringComparison.OrdinalIgnoreCase)),
                    $"{c.Id}: none of [{string.Join(" | ", fact)}] occurs in the cited resources");
            }
        }
    }

    /// <summary>A forbidden string must not be an ordinary part of the record, or a correct answer could trip it.</summary>
    [Fact]
    public void Forbidden_strings_are_not_in_the_cited_resources()
    {
        foreach (var c in Cases.Where(c => c.Answerable && c.MustNotContain.Count > 0))
        {
            var citedText = EvalScorer.Normalize(string.Join("\n", c.MustCite.Select(r => Resources[r].Text)));
            foreach (var banned in c.MustNotContain)
            {
                Assert.DoesNotContain(EvalScorer.Normalize(banned), citedText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static Dictionary<string, (string Patient, string Text)> LoadResources()
    {
        var resources = new Dictionary<string, (string, string)>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "data"), "patient-*.json"))
        {
            using var bundle = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var entry in bundle.RootElement.GetProperty("entry").EnumerateArray())
            {
                var resource = entry.GetProperty("resource");
                var type = resource.GetProperty("resourceType").GetString()!;
                var id = resource.GetProperty("id").GetString()!;
                var reference = $"{type}/{id}";

                var patient = type == "Patient"
                    ? reference
                    : resource.GetProperty("subject").GetProperty("reference").GetString()!;

                var text = resource.GetRawText();
                if (type == "DocumentReference")
                {
                    var data = resource.GetProperty("content")[0].GetProperty("attachment").GetProperty("data").GetString()!;
                    text += "\n" + Encoding.UTF8.GetString(Convert.FromBase64String(data));
                }

                resources.Add(reference, (patient, text));
            }
        }
        return resources;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FhirMcp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("FhirMcp.slnx not found above the test output folder.");
    }
}
