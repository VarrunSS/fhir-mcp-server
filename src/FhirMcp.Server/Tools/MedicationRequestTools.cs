using System.ComponentModel;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Summaries;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

[McpServerToolType]
public sealed class MedicationRequestTools(FhirClient fhir, IOptions<FhirOptions> options)
{
    [McpServerTool(Name = "search_medication_requests", ReadOnly = true)]
    [Description(
        "List a patient's medication orders and prescriptions, most recently written first. Each result has a " +
        "'reference' such as 'MedicationRequest/abc', the medication, its status (active, stopped, completed), " +
        "the date written and the dosage instructions. A stopped or completed request is not a current medication.")]
    public Task<string> SearchMedicationRequests(
        [Description("The patient's FHIR id (the part after 'Patient/').")] string patientId,
        [Description("Filter by status: active, on-hold, cancelled, completed, stopped, draft, entered-in-error or unknown.")] string? status = null,
        [Description("Maximum results to return (default 10, maximum 50).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            var query = FhirQuery.ForPatient(patientId)
                .Add("status", status)
                .SortAndLimit("-authoredon", count, options.Value);

            var bundle = await fhir.SearchAsync("MedicationRequest", query, cancellationToken);
            return SearchResult.FromBundle(bundle, MedicationRequestSummary.From);
        });

    [McpServerTool(Name = "read_medication_request", ReadOnly = true)]
    [Description("Read one medication request by id. Returns its summary with its 'reference'.")]
    public Task<string> ReadMedicationRequest(
        [Description("The medication request's FHIR id (the part after 'MedicationRequest/').")] string medicationRequestId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            FhirId.Validate(medicationRequestId, nameof(medicationRequestId));
            var request = await fhir.ReadAsync("MedicationRequest", medicationRequestId, cancellationToken);
            return SummaryJson.Serialize(MedicationRequestSummary.From(request));
        });
}
