using System.ComponentModel;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Summaries;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

[McpServerToolType]
public sealed class PatientTools(FhirClient fhir, IOptions<FhirOptions> options)
{
    [McpServerTool(Name = "search_patients", ReadOnly = true)]
    [Description(
        "Search for patients by name, birth date or identifier. Returns patient summaries, each with a " +
        "'reference' such as 'Patient/abc'; use the id after the slash as patientId in the other tools. " +
        "Supply at least one criterion to narrow the search.")]
    public Task<string> SearchPatients(
        [Description("Part of the patient's given or family name.")] string? name = null,
        [Description("Date of birth as YYYY-MM-DD.")] string? birthDate = null,
        [Description("A patient identifier value such as a medical record number.")] string? identifier = null,
        [Description("Maximum results to return (default 10, maximum 50).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            var query = new List<KeyValuePair<string, string>>();
            AddIfPresent(query, "name", name);
            AddIfPresent(query, "birthdate", birthDate);
            AddIfPresent(query, "identifier", identifier);
            query.Add(new("_count", ToolRunner.ClampCount(count, options.Value).ToString()));

            var bundle = await fhir.SearchAsync("Patient", query, cancellationToken);
            return SearchResult.FromBundle(bundle, PatientSummary.From);
        });

    [McpServerTool(Name = "read_patient", ReadOnly = true)]
    [Description("Read one patient's demographics by id. Returns a patient summary with its 'reference'.")]
    public Task<string> ReadPatient(
        [Description("The patient's FHIR id, for example 'pat-ada' (the part after 'Patient/').")] string patientId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            FhirId.Validate(patientId, nameof(patientId));
            var patient = await fhir.ReadAsync("Patient", patientId, cancellationToken);
            return SummaryJson.Serialize(PatientSummary.From(patient));
        });

    private static void AddIfPresent(List<KeyValuePair<string, string>> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add(new(name, value.Trim()));
        }
    }
}
