using System.Text.RegularExpressions;

namespace FhirMcp.Server.Fhir;

/// <summary>
/// Guardrail: an id supplied by the model goes into a URL path, so it must be a plain FHIR id.
/// This is what stops "pat-1/_history/1" or "x?_count=999" from changing the request.
/// </summary>
public static partial class FhirId
{
    // The FHIR R4 id datatype: letters, digits, '-' and '.', 1 to 64 characters.
    // \z rather than $ so a trailing newline is not accepted.
    [GeneratedRegex(@"\A[A-Za-z0-9\-\.]{1,64}\z")]
    private static partial Regex Pattern();

    public static string Validate(string id, string parameterName)
    {
        if (id is null || !Pattern().IsMatch(id))
        {
            throw new ArgumentException(
                $"{parameterName} must be a FHIR id: 1 to 64 letters, digits, '-' or '.'.", parameterName);
        }

        return id;
    }
}
