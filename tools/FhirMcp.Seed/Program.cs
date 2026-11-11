using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using FhirMcp.Seed;
using FhirMcp.Server.Fhir;

// The only code in the repo that sends anything but GET. It loads data/*.json into a local HAPI.
// Usage: dotnet run --project tools/FhirMcp.Seed -- [--base-url URL] [--data DIR] [--wait-seconds N]

var baseUrl = "http://localhost:8080/fhir";
var dataDirectory = "data";
var waitSeconds = 180;

try
{
    for (var i = 0; i < args.Length; i += 2)
    {
        var value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"{args[i]} needs a value.");
        switch (args[i])
        {
            case "--base-url": baseUrl = value; break;
            case "--data": dataDirectory = value; break;
            case "--wait-seconds":
                waitSeconds = int.TryParse(value, out var seconds) && seconds >= 0
                    ? seconds
                    : throw new ArgumentException("--wait-seconds must be a whole number of seconds.");
                break;
            default: throw new ArgumentException($"Unknown option '{args[i]}'.");
        }
    }

    // Same allowlist as the server, then the stricter rule that writing is loopback only.
    var baseUri = FhirBaseUrlValidator.Validate(new FhirOptions { BaseUrl = baseUrl });
    SeedTarget.EnsureLoopback(baseUri);

    if (!Directory.Exists(dataDirectory))
    {
        throw new ArgumentException($"Data folder '{dataDirectory}' does not exist. Run from the repo root or pass --data.");
    }

    var bundles = Directory.GetFiles(dataDirectory, "*.json").Order().ToArray();
    if (bundles.Length == 0)
    {
        throw new ArgumentException($"No *.json bundles found in '{dataDirectory}'.");
    }

    using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        BaseAddress = baseUri,
        Timeout = TimeSpan.FromSeconds(60),
    };

    if (!await WaitForServerAsync(http, baseUri, waitSeconds))
    {
        Console.Error.WriteLine($"HAPI did not answer GET {baseUri}metadata within {waitSeconds} seconds. Is it running? Try: docker compose up -d");
        return 1;
    }

    foreach (var path in bundles)
    {
        using var content = new StringContent(await File.ReadAllTextAsync(path), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        using var response = await http.PostAsync(string.Empty, content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var detail = OperationOutcomeParser.Describe(body) ?? "(no OperationOutcome in the response)";
            Console.Error.WriteLine($"Seeding {Path.GetFileName(path)} failed: HTTP {(int)response.StatusCode}. {detail}");
            return 1;
        }

        Console.WriteLine($"Loaded {Path.GetFileName(path)} (HTTP {(int)response.StatusCode})");
    }

    Console.WriteLine($"Seeded {bundles.Length} bundles");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"fhir-mcp seed: {ex.Message}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"fhir-mcp seed: the FHIR server could not be reached: {ex.Message}");
    return 1;
}

// HAPI needs a minute or two to start; keep asking until the capability statement is served.
static async Task<bool> WaitForServerAsync(HttpClient http, Uri baseUri, int waitSeconds)
{
    var clock = Stopwatch.StartNew();
    var announced = false;
    while (true)
    {
        try
        {
            using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await http.GetAsync("metadata", probe.Token);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Not up yet: connection refused or no answer within 5 seconds.
        }

        if (clock.Elapsed.TotalSeconds >= waitSeconds)
        {
            return false;
        }

        if (!announced)
        {
            Console.WriteLine($"Waiting for HAPI at {baseUri} (up to {waitSeconds} s)...");
            announced = true;
        }

        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}
