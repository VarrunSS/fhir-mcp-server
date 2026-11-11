using System.Net.Http.Headers;
using System.Text.Json;

namespace FhirMcp.Server.Fhir;

/// <summary>
/// The only class that talks to the FHIR server. It is read-only by construction:
/// it has a read and a search, both HTTP GET, and nothing else.
/// </summary>
/// <param name="http">An HttpClient whose BaseAddress is the validated FHIR base URL.</param>
public sealed class FhirClient(HttpClient http)
{
    private static readonly MediaTypeWithQualityHeaderValue FhirJson = new("application/fhir+json");

    /// <summary>GET [base]/[resourceType]/[id]</summary>
    public Task<JsonElement> ReadAsync(string resourceType, string id, CancellationToken ct)
    {
        FhirId.Validate(id, nameof(id));
        return GetAsync($"{resourceType}/{id}", ct);
    }

    /// <summary>GET [base]/[resourceType]?[query]. Returns the searchset Bundle.</summary>
    public Task<JsonElement> SearchAsync(
        string resourceType, IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        var queryString = string.Join(
            "&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return GetAsync($"{resourceType}?{queryString}", ct);
    }

    private async Task<JsonElement> GetAsync(string relativeUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Accept.Add(FhirJson);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new FhirRequestException($"The FHIR server could not be reached: {ex.Message}", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The caller did not cancel, so this is HttpClient's own timeout.
            throw new FhirRequestException("The FHIR request timed out.", inner: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            // Redirects are never followed: a redirect could send the request to a host
            // that is not on the allowlist.
            if (status is >= 300 and < 400)
            {
                throw new FhirRequestException(
                    $"The FHIR server answered with a redirect (HTTP {status}), which is not followed.", status);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Only text from a well-formed OperationOutcome is passed on; any other body is dropped.
                var outcome = OperationOutcomeParser.Describe(body);
                var message = outcome is null
                    ? $"The FHIR request failed with HTTP {status}."
                    : $"The FHIR request failed with HTTP {status}: {outcome}";
                throw new FhirRequestException(message, status);
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new FhirRequestException("The FHIR server returned a body that is not valid JSON.", status, ex);
            }
        }
    }
}
