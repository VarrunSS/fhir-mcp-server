namespace FhirMcp.Server.Fhir;

/// <summary>
/// Guardrail: the server only talks to FHIR hosts on an explicit allowlist.
/// Run once at startup so a bad setting stops the server instead of failing per request.
/// </summary>
public static class FhirBaseUrlValidator
{
    /// <returns>The base URI with exactly one trailing slash, ready to be an HttpClient.BaseAddress.</returns>
    /// <exception cref="InvalidOperationException">The URL breaks a rule; the message says which.</exception>
    public static Uri Validate(FhirOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"FHIR base URL '{options.BaseUrl}' is not an absolute URL.");
        }

        // "https://allowed-host@evil.com/" parses with host evil.com; refuse userinfo outright
        // so the URL can never look like one host while pointing at another.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("FHIR base URL must not contain user information.");
        }

        // Uri.Host is already lower-cased; compare the whole host, never a prefix or suffix.
        var hostAllowed = options.AllowedHosts.Any(
            allowed => string.Equals(allowed, uri.Host, StringComparison.OrdinalIgnoreCase));
        if (!hostAllowed)
        {
            throw new InvalidOperationException(
                $"FHIR host '{uri.Host}' is not in the allowed hosts: {string.Join(", ", options.AllowedHosts)}.");
        }

        var schemeAllowed = uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!schemeAllowed)
        {
            throw new InvalidOperationException(
                $"FHIR base URL must use https (http is allowed for loopback only); got '{uri.Scheme}' for host '{uri.Host}'.");
        }

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        builder.Path = builder.Path.TrimEnd('/') + "/";
        return builder.Uri;
    }
}
