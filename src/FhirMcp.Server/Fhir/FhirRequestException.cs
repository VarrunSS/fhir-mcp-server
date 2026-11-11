namespace FhirMcp.Server.Fhir;

/// <summary>
/// A FHIR request that did not produce a usable resource. The message is written to be
/// shown to the model, so it must be safe to expose: no raw response bodies, no stack traces.
/// </summary>
public sealed class FhirRequestException(string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The HTTP status, or null when no response arrived (unreachable, timed out).</summary>
    public int? StatusCode { get; } = statusCode;
}
