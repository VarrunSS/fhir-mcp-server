using FhirMcp.Server.Fhir;

namespace FhirMcp.Tests.Unit;

public class FhirIdTests
{
    [Theory]
    [InlineData("pat-ada")]
    [InlineData("A.b-9")]
    [InlineData("123")]
    public void Accepts_valid_ids(string id)
    {
        Assert.Equal(id, FhirId.Validate(id, "patientId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pat-1/_history/1")]
    [InlineData("x?_count=999")]
    [InlineData("../metadata")]
    [InlineData("a b")]
    [InlineData("pat-1\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 characters
    public void Rejects_ids_that_are_not_fhir_ids(string id)
    {
        var error = Assert.Throws<ArgumentException>(() => FhirId.Validate(id, "patientId"));
        Assert.Contains("patientId", error.Message);
    }
}
