using System.ComponentModel;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Summaries;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

[McpServerToolType]
public sealed class ConditionTools(FhirClient fhir, IOptions<FhirOptions> options)
{
    [McpServerTool(Name = "search_conditions", ReadOnly = true)]
    [Description(
        "List a patient's conditions (diagnoses and problems), most recently recorded first. Each result has a " +
        "'reference' such as 'Condition/abc', the condition name, its clinical status and its onset date.")]
    public Task<string> SearchConditions(
        [Description("The patient's FHIR id (the part after 'Patient/').")] string patientId,
        [Description("Filter by clinical status: active, recurrence, relapse, inactive, remission or resolved.")] string? clinicalStatus = null,
        [Description("Maximum results to return (default 10, maximum 50).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            var query = FhirQuery.ForPatient(patientId)
                .Add("clinical-status", clinicalStatus)
                .SortAndLimit("-recorded-date", count, options.Value);

            var bundle = await fhir.SearchAsync("Condition", query, cancellationToken);
            return SearchResult.FromBundle(bundle, ConditionSummary.From);
        });

    [McpServerTool(Name = "read_condition", ReadOnly = true)]
    [Description("Read one condition by id. Returns the condition summary with its 'reference'.")]
    public Task<string> ReadCondition(
        [Description("The condition's FHIR id (the part after 'Condition/').")] string conditionId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            FhirId.Validate(conditionId, nameof(conditionId));
            var condition = await fhir.ReadAsync("Condition", conditionId, cancellationToken);
            return SummaryJson.Serialize(ConditionSummary.From(condition));
        });
}
