using System.Net;
using FhirMcp.Server.Fhir;

namespace FhirMcp.Tests.Unit;

public class FhirClientTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private const string NotFoundOutcome = """
        {"resourceType":"OperationOutcome","issue":[
          {"severity":"error","code":"not-found","diagnostics":"Resource Patient/zzz is not known"}]}
        """;

    [Fact]
    public async Task Read_builds_url_and_sends_one_get()
    {
        var fake = FakeHttpHandler.Json("""{"resourceType":"Patient","id":"pat-ada"}""");
        var client = new FhirClient(fake.CreateClient());

        var patient = await client.ReadAsync("Patient", "pat-ada", None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://localhost:8080/fhir/Patient/pat-ada", request.RequestUri!.AbsoluteUri);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/fhir+json");
        Assert.Equal("pat-ada", patient.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Read_rejects_a_bad_id_without_sending_anything()
    {
        var fake = FakeHttpHandler.Json("{}");
        var client = new FhirClient(fake.CreateClient());

        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync("Patient", "pat-1/_history/1", None));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Search_encodes_query_values()
    {
        var fake = FakeHttpHandler.Json("""{"resourceType":"Bundle","type":"searchset"}""");
        var client = new FhirClient(fake.CreateClient());

        await client.SearchAsync("Patient", [new("name", "O'Brien & co"), new("_count", "10")], None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/fhir/Patient", request.RequestUri!.AbsolutePath);
        // The ampersand inside the value must not become a parameter separator.
        Assert.Equal("?name=O%27Brien%20%26%20co&_count=10", request.RequestUri.Query);
    }

    [Fact]
    public async Task Error_with_operation_outcome_carries_status_and_diagnostics()
    {
        var client = new FhirClient(FakeHttpHandler.Json(NotFoundOutcome, HttpStatusCode.NotFound).CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "zzz", None));

        Assert.Equal(404, error.StatusCode);
        Assert.Contains("404", error.Message);
        Assert.Contains("Resource Patient/zzz is not known", error.Message);
    }

    [Fact]
    public async Task Error_without_outcome_does_not_leak_the_body()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html><body>stack trace here</body></html>"),
        });
        var client = new FhirClient(fake.CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "pat-ada", None));

        Assert.Equal(500, error.StatusCode);
        Assert.Contains("500", error.Message);
        Assert.DoesNotContain("stack trace", error.Message);
    }

    [Fact]
    public async Task Redirect_is_an_error_and_is_not_followed()
    {
        var fake = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://evil.example/fhir/Patient/pat-ada");
            return response;
        });
        var client = new FhirClient(fake.CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "pat-ada", None));

        Assert.Contains("redirect", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Unreachable_server_is_reported_as_such()
    {
        var fake = new FakeHttpHandler(_ => throw new HttpRequestException("Connection refused"));
        var client = new FhirClient(fake.CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "pat-ada", None));

        Assert.Contains("could not be reached", error.Message);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task Timeout_is_reported_as_such()
    {
        // HttpClient signals its own timeout with TaskCanceledException while the caller's token is not cancelled.
        var fake = new FakeHttpHandler(_ => throw new TaskCanceledException("The request timed out"));
        var client = new FhirClient(fake.CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "pat-ada", None));

        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_a_fhir_error()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fake = new FakeHttpHandler(_ => throw new TaskCanceledException());
        var client = new FhirClient(fake.CreateClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync("Patient", "pat-ada", cts.Token));
    }

    [Fact]
    public async Task Body_that_is_not_json_is_an_error()
    {
        var client = new FhirClient(FakeHttpHandler.Json("oops").CreateClient());

        var error = await Assert.ThrowsAsync<FhirRequestException>(() => client.ReadAsync("Patient", "pat-ada", None));

        Assert.Contains("not valid JSON", error.Message);
    }
}
