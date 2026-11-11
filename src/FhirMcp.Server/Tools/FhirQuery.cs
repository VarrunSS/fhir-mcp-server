using System.Collections;
using System.Text.RegularExpressions;
using FhirMcp.Server.Fhir;

namespace FhirMcp.Server.Tools;

/// <summary>
/// Builds the query for a FHIR search from tool arguments. Arguments are validated here,
/// before any request is sent, and blank optional arguments are left out.
/// </summary>
public sealed partial class FhirQuery : IEnumerable<KeyValuePair<string, string>>
{
    private readonly List<KeyValuePair<string, string>> _parameters = [];

    // FHIR dates at year, month or day precision.
    [GeneratedRegex(@"\A\d{4}(-\d{2}(-\d{2})?)?\z")]
    private static partial Regex DatePattern();

    /// <summary>Starts a search scoped to one patient. Every clinical search goes through this.</summary>
    public static FhirQuery ForPatient(string patientId)
    {
        FhirId.Validate(patientId, nameof(patientId));
        return new FhirQuery().Add("patient", patientId);
    }

    public FhirQuery Add(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _parameters.Add(new(name, value.Trim()));
        }

        return this;
    }

    /// <summary>Adds date=ge[from] and date=le[to], FHIR's way of expressing a range.</summary>
    public FhirQuery AddDateRange(string? dateFrom, string? dateTo)
    {
        Add("date", Prefixed("ge", dateFrom, nameof(dateFrom)));
        Add("date", Prefixed("le", dateTo, nameof(dateTo)));
        return this;
    }

    public FhirQuery SortAndLimit(string sort, int? count, FhirOptions options) =>
        Add("_sort", sort).Add("_count", ToolRunner.ClampCount(count, options).ToString());

    private static string? Prefixed(string prefix, string? date, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        if (!DatePattern().IsMatch(date.Trim()))
        {
            throw new ArgumentException($"{parameterName} must be a date as YYYY, YYYY-MM or YYYY-MM-DD.", parameterName);
        }

        return prefix + date.Trim();
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _parameters.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
