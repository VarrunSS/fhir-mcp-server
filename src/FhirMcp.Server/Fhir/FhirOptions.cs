namespace FhirMcp.Server.Fhir;

/// <summary>Settings for the FHIR connection and the size limits applied to tool results.</summary>
public sealed class FhirOptions
{
    public const string SectionName = "Fhir";

    public string BaseUrl { get; set; } = "http://localhost:8080/fhir";

    /// <summary>The only hosts the server will talk to. Checked once at startup.</summary>
    public string[] AllowedHosts { get; set; } = ["localhost", "127.0.0.1", "hapi.fhir.org"];

    public int DefaultCount { get; set; } = 10;
    public int MaxCount { get; set; } = 50;
    public int PreviewChars { get; set; } = 500;
    public int MaxDocumentChars { get; set; } = 20000;
}
