namespace FhirMcp.Seed;

/// <summary>
/// Guardrail: the seed tool is the only code in the repo that writes to a FHIR server,
/// so it may only ever write to one running on this machine.
/// </summary>
public static class SeedTarget
{
    /// <summary>
    /// The server project's allowlist also admits the public hapi.fhir.org for reading;
    /// this check is stricter on purpose, so a mistyped URL can never POST to a shared server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host is not a loopback address.</exception>
    public static void EnsureLoopback(Uri baseUri)
    {
        if (!baseUri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"Refusing to seed '{baseUri.Host}': the seed tool only writes to a loopback host (localhost, 127.0.0.1 or [::1]).");
        }
    }
}
