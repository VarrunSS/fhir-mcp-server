using FhirMcp.Seed;

namespace FhirMcp.Tests.Unit;

public class SeedTargetTests
{
    [Theory]
    [InlineData("http://localhost:8080/fhir/")]
    [InlineData("http://127.0.0.1:8080/fhir/")]
    [InlineData("http://[::1]:8080/fhir/")]
    public void Accepts_loopback_hosts(string url)
    {
        SeedTarget.EnsureLoopback(new Uri(url));
    }

    [Theory]
    [InlineData("https://hapi.fhir.org/baseR4/")]          // allowed for reading, never for seeding
    [InlineData("https://evil.example/fhir/")]
    [InlineData("http://localhost.evil.example/fhir/")]    // loopback name as a subdomain prefix
    [InlineData("http://10.0.0.5:8080/fhir/")]             // private network, not loopback
    [InlineData("http://192.168.1.20:8080/fhir/")]
    public void Refuses_non_loopback_hosts(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SeedTarget.EnsureLoopback(new Uri(url)));

        Assert.Contains("loopback", ex.Message);
    }
}
