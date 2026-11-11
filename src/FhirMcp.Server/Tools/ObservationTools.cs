using System.ComponentModel;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Summaries;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

[McpServerToolType]
public sealed class ObservationTools(FhirClient fhir, IOptions<FhirOptions> options)
{
    [McpServerTool(Name = "search_observations", ReadOnly = true)]
    [Description(
        "List a patient's observations (laboratory results, vital signs, social history), newest first. " +
        "Each result has a 'reference' such as 'Observation/abc', what was measured, the value with its unit " +
        "and the date. Filter by code or codeText to find one kind of measurement; when 'hasMore' is true, " +
        "narrow the search or raise count.")]
    public Task<string> SearchObservations(
        [Description("The patient's FHIR id (the part after 'Patient/').")] string patientId,
        [Description("A LOINC code for the measurement, for example '4548-4' for hemoglobin A1c.")] string? code = null,
        [Description("Words from the measurement's name, for example 'HbA1c' or 'blood pressure'. Use when the code is not known.")] string? codeText = null,
        [Description("Observation category: laboratory, vital-signs, social-history, imaging, survey, exam, procedure, therapy or activity.")] string? category = null,
        [Description("Only observations on or after this date (YYYY, YYYY-MM or YYYY-MM-DD).")] string? dateFrom = null,
        [Description("Only observations on or before this date (YYYY, YYYY-MM or YYYY-MM-DD).")] string? dateTo = null,
        [Description("Maximum results to return (default 10, maximum 50).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            var query = FhirQuery.ForPatient(patientId)
                .Add("code", code)
                .Add("code:text", codeText)
                .Add("category", category)
                .AddDateRange(dateFrom, dateTo)
                .SortAndLimit("-date", count, options.Value);

            var bundle = await fhir.SearchAsync("Observation", query, cancellationToken);
            return SearchResult.FromBundle(bundle, ObservationSummary.From);
        });

    [McpServerTool(Name = "read_observation", ReadOnly = true)]
    [Description("Read one observation by id. Returns the observation summary with its 'reference'.")]
    public Task<string> ReadObservation(
        [Description("The observation's FHIR id (the part after 'Observation/').")] string observationId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            FhirId.Validate(observationId, nameof(observationId));
            var observation = await fhir.ReadAsync("Observation", observationId, cancellationToken);
            return SummaryJson.Serialize(ObservationSummary.From(observation));
        });
}
