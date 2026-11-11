using FhirMcp.Server.Fhir;

namespace FhirMcp.Tests.Unit;

public class FhirBaseUrlValidatorTests
{
    private static Uri Validate(string baseUrl) =>
        FhirBaseUrlValidator.Validate(new FhirOptions { BaseUrl = baseUrl });

    [Fact]
    public void Accepts_https_allowed_host()
    {
        Assert.Equal("https://hapi.fhir.org/baseR4/", Validate("https://hapi.fhir.org/baseR4").AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost:8080/fhir", "http://localhost:8080/fhir/")]
    [InlineData("http://127.0.0.1:8080/fhir", "http://127.0.0.1:8080/fhir/")]
    [InlineData("http://localhost:8080/fhir/", "http://localhost:8080/fhir/")]
    public void Accepts_http_loopback(string baseUrl, string expected)
    {
        Assert.Equal(expected, Validate(baseUrl).AbsoluteUri);
    }

    [Theory]
    [InlineData("http://hapi.fhir.org/baseR4")]             // plain http to a non-loopback host
    [InlineData("https://hapi.fhir.org.evil.com/baseR4")]   // allowed host as a subdomain prefix
    [InlineData("https://evil.com/hapi.fhir.org")]          // allowed host in the path
    [InlineData("https://hapi.fhir.org@evil.com/baseR4")]   // allowed host as userinfo
    [InlineData("https://user:pw@hapi.fhir.org/baseR4")]    // credentials in the URL
    [InlineData("ftp://localhost/fhir")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Rejects(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => Validate(baseUrl));
    }

    [Fact]
    public void Host_match_is_case_insensitive()
    {
        Assert.Equal("hapi.fhir.org", Validate("https://HAPI.FHIR.ORG/baseR4").Host);
    }

    [Fact]
    public void Host_not_on_a_custom_allowlist_is_rejected()
    {
        var options = new FhirOptions { BaseUrl = "https://hapi.fhir.org/baseR4", AllowedHosts = ["localhost"] };

        var error = Assert.Throws<InvalidOperationException>(() => FhirBaseUrlValidator.Validate(options));

        Assert.Contains("hapi.fhir.org", error.Message);
    }
}
