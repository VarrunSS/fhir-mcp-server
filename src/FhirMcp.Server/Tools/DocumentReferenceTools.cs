using System.ComponentModel;
using FhirMcp.Server.Fhir;
using FhirMcp.Server.Summaries;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

[McpServerToolType]
public sealed class DocumentReferenceTools(FhirClient fhir, IOptions<FhirOptions> options)
{
    [McpServerTool(Name = "search_document_references", ReadOnly = true)]
    [Description(
        "List a patient's clinical documents (clinic notes, discharge summaries, letters), newest first. Each " +
        "result has a 'reference' such as 'DocumentReference/abc', the document type, date, author and a short " +
        "preview of the text. When 'textTruncated' is true, call read_document_reference for the full text. " +
        "Notes often hold details that are not recorded as structured data.")]
    public Task<string> SearchDocumentReferences(
        [Description("The patient's FHIR id (the part after 'Patient/').")] string patientId,
        [Description("Words from the document type, for example 'discharge summary' or 'clinic note'.")] string? typeText = null,
        [Description("Only documents dated on or after this date (YYYY, YYYY-MM or YYYY-MM-DD).")] string? dateFrom = null,
        [Description("Only documents dated on or before this date (YYYY, YYYY-MM or YYYY-MM-DD).")] string? dateTo = null,
        [Description("Maximum results to return (default 10, maximum 50).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            var query = FhirQuery.ForPatient(patientId)
                .Add("type:text", typeText)
                .AddDateRange(dateFrom, dateTo)
                .SortAndLimit("-date", count, options.Value);

            var bundle = await fhir.SearchAsync("DocumentReference", query, cancellationToken);
            return SearchResult.FromBundle(
                bundle, document => DocumentReferenceSummary.From(document, options.Value.PreviewChars));
        });

    [McpServerTool(Name = "read_document_reference", ReadOnly = true)]
    [Description(
        "Read one clinical document by id, including its full text (cut at a fixed maximum length, " +
        "in which case 'textTruncated' is true).")]
    public Task<string> ReadDocumentReference(
        [Description("The document's FHIR id (the part after 'DocumentReference/').")] string documentReferenceId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(async () =>
        {
            FhirId.Validate(documentReferenceId, nameof(documentReferenceId));
            var document = await fhir.ReadAsync("DocumentReference", documentReferenceId, cancellationToken);
            return SummaryJson.Serialize(DocumentReferenceSummary.From(document, options.Value.MaxDocumentChars));
        });
}
