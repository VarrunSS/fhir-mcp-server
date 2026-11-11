# fhir-mcp-server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Stop after every task and wait for the human to review the commit before starting the next one.**

**Goal:** A C# MCP server exposing read-only FHIR search and read tools, a Claude-backed clinical Q&A agent that uses it, and a golden-set eval suite that runs in CI.

**Architecture:** `FhirMcp.Server` (stdio MCP server) wraps a GET-only FHIR client and returns compact summaries. `FhirMcp.Agent` starts the server as a child process and runs a hand-written Claude tool loop that ends in a structured, cited answer. `FhirMcp.Evals` runs golden cases through the agent and scores them in code. A local HAPI FHIR container seeded from `data/` is the deterministic backend.

**Tech Stack:** .NET 10, C#, `ModelContextProtocol` 2.4.0, `Anthropic` (official C# SDK), `System.Text.Json` (no FHIR SDK: summaries need a handful of fields), xUnit, Docker (`hapiproject/hapi`), GitHub Actions.

**Spec:** `docs/specs/2026-11-11-fhir-mcp-server-design.md`

## Global Constraints

- All projects target `net10.0`, nullable enabled, warnings as errors.
- The server sends HTTP GET only. No POST, PUT, PATCH or DELETE anywhere under `src/FhirMcp.Server`.
- FHIR base URL host must be in `Fhir:AllowedHosts` (default `localhost`, `127.0.0.1`, `hapi.fhir.org`); https required, http for loopback only; redirects not followed.
- `count` defaults to 10, capped at 50. Document text: 500-character preview in search, 20,000-character maximum on read, with the marker `[truncated]`.
- Every summary has a `reference` field of the form `ResourceType/id`.
- Synthetic data only. No real names of people, employers or clients. No secrets in the repo; the agent reads `ANTHROPIC_API_KEY` from the environment.
- Agent model default `claude-sonnet-5-5`, overridable by `Agent:Model` / `AGENT_MODEL`.
- Eval pass threshold default 0.85.
- Commit after every task, message in imperative mood, ending with the Claude co-author trailer.

## Review Focus

Inputs the spec implies but does not spell out. Each has a test in the task named.

1. **FHIR server unreachable or slow** → the tool returns an error that says the FHIR server could not be reached; the MCP server process stays up. (Task 2)
2. **Ids that try to change the URL** (`pat-1/_history/1`, `x?_count=999`, `../metadata`, empty) → rejected before any request is sent. (Task 2)
3. **Allowlist look-alikes** (`hapi.fhir.org.evil.com`, `evil.com/hapi.fhir.org`, `http://hapi.fhir.org`, userinfo `https://hapi.fhir.org@evil.com`) → refused at startup. (Task 1)
4. **Documents that are not readable text** (missing `data`, invalid base64, `application/pdf`) → summary says no inline text is available rather than throwing. (Task 4)
5. **A patient id that does not exist** on a patient-scoped search → an empty result with `total: 0`, which the agent must report as "not in the record", not as an error. (Task 5 integration test, Task 7 eval case)

---

## File Structure

```
FhirMcp.slnx
Directory.Build.props                 net10.0, nullable, warnings as errors
docker-compose.yml                    HAPI on localhost:8080
data/*.json                           synthetic transaction bundles
src/FhirMcp.Server/
  Program.cs                          host, stdio transport, DI
  appsettings.json                    Fhir:BaseUrl, Fhir:AllowedHosts
  Fhir/FhirOptions.cs                 settings and limits
  Fhir/FhirBaseUrlValidator.cs        allowlist check
  Fhir/FhirId.cs                      id validation
  Fhir/FhirClient.cs                  GET-only client
  Fhir/FhirRequestException.cs
  Fhir/OperationOutcomeParser.cs      OperationOutcome → text
  Summaries/*.cs                      one static summarizer per resource type + SearchResult
  Tools/*.cs                          one tool class per resource type + ToolRunner
src/FhirMcp.Agent/
  Program.cs                          CLI: question in, answer out
  ClinicalAgent.cs                    the tool loop
  McpToolBridge.cs                    MCP tool ↔ Claude tool
  CitationGrounding.cs                grounding check
  AgentAnswer.cs, AgentOptions.cs, SystemPrompt.cs
tools/FhirMcp.Seed/Program.cs         POST bundles (the only writer)
tests/FhirMcp.Tests/                  Unit/ and Integration/
evals/FhirMcp.Evals/
  Program.cs, EvalCase.cs, EvalScorer.cs, cases.json
.github/workflows/ci.yml
CLAUDE.md, README.md
```

---

### Task 1: Solution scaffold and host allowlist

**Files:** `FhirMcp.slnx`, `Directory.Build.props`, `.gitignore`, `src/FhirMcp.Server/{FhirMcp.Server.csproj,Fhir/FhirOptions.cs,Fhir/FhirBaseUrlValidator.cs}`, `tests/FhirMcp.Tests/{FhirMcp.Tests.csproj,Unit/FhirBaseUrlValidatorTests.cs}`

**Interfaces — Produces:**
```csharp
public sealed class FhirOptions {
    public string BaseUrl { get; set; } = "http://localhost:8080/fhir";
    public string[] AllowedHosts { get; set; } = ["localhost", "127.0.0.1", "hapi.fhir.org"];
    public int DefaultCount { get; set; } = 10;
    public int MaxCount { get; set; } = 50;
    public int PreviewChars { get; set; } = 500;
    public int MaxDocumentChars { get; set; } = 20000;
}
public static class FhirBaseUrlValidator {
    // Returns the base URI with exactly one trailing slash. Throws InvalidOperationException with a message naming the rule broken.
    public static Uri Validate(FhirOptions options);
}
```

- [ ] **Step 1:** Create solution, the server console project and the xUnit project (`dotnet new`), add both to the solution, reference server from tests. `Directory.Build.props` sets `TargetFramework`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`.
- [ ] **Step 2: Failing tests** in `FhirBaseUrlValidatorTests`:
  - `Accepts_https_allowed_host`: `https://hapi.fhir.org/baseR4` → `https://hapi.fhir.org/baseR4/`
  - `Accepts_http_loopback`: `http://localhost:8080/fhir` and `http://127.0.0.1:8080/fhir`
  - `[Theory] Rejects`: `http://hapi.fhir.org/baseR4`, `https://hapi.fhir.org.evil.com/baseR4`, `https://evil.com/hapi.fhir.org`, `https://hapi.fhir.org@evil.com/baseR4`, `ftp://localhost/fhir`, `not a url`, `""` → `InvalidOperationException`
  - `Host_match_is_case_insensitive`: `https://HAPI.FHIR.ORG/baseR4` accepted
- [ ] **Step 3:** Run `dotnet test`; expect compile failure.
- [ ] **Step 4:** Implement `FhirOptions` and `FhirBaseUrlValidator` (compare `Uri.Host` exactly, reject non-empty `UserInfo`, use `Uri.IsLoopback` for the http exception).
- [ ] **Step 5:** `dotnet test` → all pass. Commit: `Scaffold solution and add FHIR host allowlist`.

### Task 2: GET-only FHIR client and error mapping

**Files:** `src/FhirMcp.Server/Fhir/{FhirId.cs,FhirClient.cs,FhirRequestException.cs,OperationOutcomeParser.cs}`, `tests/FhirMcp.Tests/Unit/{FakeHttpHandler.cs,FhirIdTests.cs,FhirClientTests.cs,OperationOutcomeParserTests.cs}`

**Interfaces — Consumes:** `FhirOptions`. **Produces:**
```csharp
public static class FhirId {
    // FHIR id pattern [A-Za-z0-9\-\.]{1,64}. Throws ArgumentException naming the parameter.
    public static string Validate(string id, string parameterName);
}
public sealed class FhirRequestException(string message, int? statusCode = null) : Exception(message) {
    public int? StatusCode { get; } = statusCode;
}
public static class OperationOutcomeParser {
    // "severity code: diagnostics" per issue, joined with "; ". Null when the body is not an OperationOutcome.
    public static string? Describe(string responseBody);
}
public sealed class FhirClient(HttpClient http) {
    public Task<JsonElement> ReadAsync(string resourceType, string id, CancellationToken ct);
    public Task<JsonElement> SearchAsync(string resourceType, IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct);
}
```
`FakeHttpHandler` records every `HttpRequestMessage` and returns a response from a `Func<HttpRequestMessage, HttpResponseMessage>`.

- [ ] **Step 1: Failing tests**
  - `FhirIdTests`: accepts `pat-ada`, `A.b-9`; rejects `""`, `pat-1/_history/1`, `x?_count=999`, `../metadata`, `a b`, a 65-character id.
  - `OperationOutcomeParserTests`: an outcome with one issue `{severity:"error", code:"not-found", diagnostics:"Resource Patient/zzz is not known"}` → `"error not-found: Resource Patient/zzz is not known"`; two issues joined with `"; "`; a `Patient` body and `"<html>"` → `null`; issue without `diagnostics` falls back to `details.text`.
  - `FhirClientTests`:
    - `Read_builds_url`: `ReadAsync("Patient","pat-ada")` → one request, method GET, URI `{base}Patient/pat-ada`, `Accept: application/fhir+json`.
    - `Search_encodes_query`: query `name=O'Brien & co` is percent-encoded.
    - `Error_with_operation_outcome`: 404 + outcome → `FhirRequestException`, `StatusCode == 404`, message contains `404` and the diagnostics text.
    - `Error_without_outcome`: 500 with HTML body → message contains `500`, does not contain the HTML.
    - `Redirect_is_an_error`: 302 with `Location: https://evil.example/` → `FhirRequestException` mentioning redirect; the fake saw exactly one request.
    - `Unreachable_server`: handler throws `HttpRequestException` → `FhirRequestException` whose message contains `could not be reached`.
    - `Timeout`: handler throws `TaskCanceledException` while the caller's token is not cancelled → `FhirRequestException` containing `timed out`.
    - `Body_that_is_not_json`: 200 with `"oops"` → `FhirRequestException`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement. `FhirClient` uses relative URIs against `HttpClient.BaseAddress`; the handler is configured with `AllowAutoRedirect = false` in Task 3's DI, and the client also treats any 3xx as an error so the rule holds with any handler.
- [ ] **Step 4:** `dotnet test` → pass. Commit: `Add GET-only FHIR client with OperationOutcome error mapping`.

### Task 3: MCP server host with patient tools

**Files:** `src/FhirMcp.Server/{Program.cs,appsettings.json,Summaries/SearchResult.cs,Summaries/PatientSummary.cs,Tools/ToolRunner.cs,Tools/PatientTools.cs}`, `tests/FhirMcp.Tests/Unit/{PatientSummaryTests.cs,PatientToolsTests.cs,ToolRunnerTests.cs}`, `tests/FhirMcp.Tests/Integration/ServerStartupTests.cs`

**Interfaces — Consumes:** `FhirClient`, `FhirOptions`, `FhirId`. **Produces:**
```csharp
public static class SearchResult {
    // { "total": int|null, "returned": int, "hasMore": bool, "results": [ ...summaries ] } as compact JSON.
    // hasMore is true when the bundle has a link with relation "next".
    public static string FromBundle(JsonElement bundle, Func<JsonElement, JsonObject> summarize);
}
public static class PatientSummary { public static JsonObject From(JsonElement patient); }
   // reference, name (given + family of the first name), gender, birthDate, identifiers[{system,value}]
public static class ToolRunner {
    // Runs the body; converts FhirRequestException and ArgumentException into McpException with the same message.
    public static Task<string> RunAsync(Func<Task<string>> body);
    // Clamps to 1..MaxCount, DefaultCount when null.
    public static int ClampCount(int? count, FhirOptions options);
}
[McpServerToolType] public sealed class PatientTools(FhirClient fhir, IOptions<FhirOptions> options) {
    [McpServerTool(Name = "search_patients", ReadOnly = true)] public Task<string> SearchPatients(string? name, string? birthDate, string? identifier, int? count, CancellationToken ct);
    [McpServerTool(Name = "read_patient", ReadOnly = true)]   public Task<string> ReadPatient(string patientId, CancellationToken ct);
}
```
FHIR parameters: `name`, `birthdate`, `identifier`, `_count`.

- [ ] **Step 1: Failing unit tests**
  - `PatientSummaryTests.Maps_core_fields`: a full Patient JSON → exact expected summary; a Patient with no `name` → `name` is `null`, no exception.
  - `ToolRunnerTests`: `ClampCount(null)==10`, `ClampCount(0)==1`, `ClampCount(500)==50`; a body throwing `FhirRequestException("x")` surfaces `McpException` with message `x`.
  - `PatientToolsTests.Search_sends_only_supplied_parameters`: `SearchPatients(name:"Lovelace", …null)` → query is exactly `name=Lovelace&_count=10`. `Search_caps_count`: `count: 999` → `_count=50`. `Read_rejects_bad_id`: `ReadPatient("a/b")` → `McpException`, zero HTTP requests.
- [ ] **Step 2:** Run; expect failures. Implement summaries, runner, tools.
- [ ] **Step 3:** `Program.cs`: `Host.CreateApplicationBuilder`, console logging to stderr (`LogToStandardErrorThreshold = LogLevel.Trace`), bind `Fhir` section, apply `FHIR_BASE_URL` override, call `FhirBaseUrlValidator.Validate` before building the host (fail fast with the message on stderr and exit code 1), register `HttpClient` with `AllowAutoRedirect = false`, 30-second timeout and the validated base address, then `.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.
- [ ] **Step 4: Integration test** `ServerStartupTests` (trait `Category=Integration`; needs the built server but no FHIR server):
  - `Lists_patient_tools_as_read_only`: start the server with `StdioClientTransport` (`dotnet <path to FhirMcp.Server.dll>`), `ListToolsAsync` contains `search_patients` and `read_patient`, each with a non-empty description and `ReadOnlyHint == true`.
  - `Refuses_disallowed_host`: start with `FHIR_BASE_URL=https://evil.example/fhir` → client creation fails.
- [ ] **Step 5:** `dotnet test` → pass. Commit: `Add MCP server host with patient search and read tools`.

### Task 4: Remaining eight tools

**Files:** `src/FhirMcp.Server/Summaries/{ConditionSummary,ObservationSummary,MedicationRequestSummary,DocumentReferenceSummary}.cs`, `src/FhirMcp.Server/Tools/{ConditionTools,ObservationTools,MedicationRequestTools,DocumentReferenceTools}.cs`, matching unit test files, extend `ServerStartupTests`.

**Interfaces — Produces** (all `ReadOnly = true`, all return `Task<string>`, all take a trailing `CancellationToken`):

| Tool | Parameters | FHIR query |
|---|---|---|
| `search_conditions` | `patientId`, `clinicalStatus?`, `count?` | `patient`, `clinical-status`, `_sort=-recorded-date` |
| `read_condition` | `conditionId` | |
| `search_observations` | `patientId`, `code?` (LOINC code), `codeText?`, `category?`, `dateFrom?`, `dateTo?`, `count?` | `patient`, `code`, `code:text`, `category`, `date=ge…`, `date=le…`, `_sort=-date` |
| `read_observation` | `observationId` | |
| `search_medication_requests` | `patientId`, `status?`, `count?` | `patient`, `status`, `_sort=-authoredon` |
| `read_medication_request` | `medicationRequestId` | |
| `search_document_references` | `patientId`, `typeText?`, `dateFrom?`, `dateTo?`, `count?` | `patient`, `type:text`, `date=ge…`, `date=le…`, `_sort=-date` |
| `read_document_reference` | `documentReferenceId` | |

Summary fields:
- Condition: `reference`, `code` (text, else first coding display), `codings[{system,code}]`, `clinicalStatus`, `verificationStatus`, `onsetDateTime`, `recordedDate`, `note`.
- Observation: `reference`, `code`, `codings`, `category`, `effectiveDateTime`, `value` (number + unit, or string, or codeable text), `components[{code,value}]` (blood pressure), `interpretation`, `referenceRange`.
- MedicationRequest: `reference`, `medication`, `status`, `intent`, `authoredOn`, `dosage` (dosageInstruction text), `reason`.
- DocumentReference: `reference`, `type`, `date`, `author`, `description`, `contentType`, `text`, `textTruncated` (bool). `From(JsonElement doc, int maxChars)`; search passes `PreviewChars`, read passes `MaxDocumentChars`. Text comes from `content[0].attachment.data` (base64, UTF-8) when `contentType` starts with `text/`; otherwise `text` is `null` and `textNote` is `"No inline text is available for this document."`.

Date parameters are validated as `YYYY`, `YYYY-MM` or `YYYY-MM-DD`; anything else is an `ArgumentException`.

- [ ] **Step 1: Failing unit tests** per summarizer (one full resource → exact summary; one sparse resource → no exception), plus:
  - `DocumentReferenceSummaryTests`: 600-character note with `maxChars: 500` → `text` is 500 characters + ` [truncated]`, `textTruncated == true`; missing `data`, invalid base64 and `application/pdf` each → `text == null`, `textNote` set, no exception.
  - `ObservationSummaryTests`: `valueQuantity` → `"7.2 %"`; blood pressure with two components → both in `components`.
  - Tool tests: each search sends `patient=<id>` and its sort; `search_observations` with `dateFrom:"2026-01"` and `dateTo:"2026-06-30"` sends `date=ge2026-01` and `date=le2026-06-30`; `dateFrom:"last week"` → `McpException`, zero requests; bad `patientId` → `McpException`, zero requests.
- [ ] **Step 2:** Run; expect failures. Implement.
- [ ] **Step 3:** Extend `ServerStartupTests`: tool list equals exactly the ten names in the spec, every one read-only, every parameter has a description.
- [ ] **Step 4:** `dotnet test` → pass. Commit: `Add condition, observation, medication and document tools`.

### Task 5: Synthetic data, seed tool, HAPI container, integration tests

**Files:** `data/{patient-ada.json,patient-bram.json,patient-chidi.json}`, `tools/FhirMcp.Seed/{FhirMcp.Seed.csproj,Program.cs}`, `docker-compose.yml`, `tests/FhirMcp.Tests/Integration/{HapiFixture.cs,ToolsAgainstHapiTests.cs}`

**Data rules:** FHIR R4 transaction bundles, every entry `PUT ResourceType/<fixed-id>` so reseeding is idempotent. Three invented adults with clearly fictional names. Each has 3–5 conditions, 8–12 observations (at least one lab repeated across three dates, one blood pressure with components), 3–5 medication requests (at least one stopped), 2–3 `DocumentReference` notes as base64 `text/plain`. Some facts appear only in a note. Deliberately absent facts (for the "not in the record" evals) are listed in `data/README.md` (JSON has no comments): for example no allergies recorded, no smoking status, no HbA1c for the third patient. Every patient carries `meta.tag` `synthetic`.

**Interfaces — Produces:**
- `dotnet run --project tools/FhirMcp.Seed -- [--base-url http://localhost:8080/fhir] [--data data] [--wait-seconds 180]` waits for `GET metadata` to return 200, POSTs each bundle, exits 0 on success. It applies the same `FhirBaseUrlValidator` and additionally refuses any non-loopback host, so it can never write to a public server.
- `docker-compose.yml`: service `hapi`, image `hapiproject/hapi` at a pinned version tag, port `8080:8080`.
- `HapiFixture` (xUnit collection fixture): reads `FHIR_BASE_URL` (default `http://localhost:8080/fhir`), fails with the message `HAPI is not reachable at <url>. Run: docker compose up -d, then the seed tool.` when it is down, starts the MCP server over stdio and exposes `McpClient Client`.

- [ ] **Step 1:** Write the three bundles and `data/README.md`.
- [ ] **Step 2:** Write the seed tool and `docker-compose.yml`. Run `docker compose up -d`, then the seed tool; expected output ends with `Seeded 3 bundles`.
- [ ] **Step 3: Integration tests** (`Category=Integration`), all through the MCP client:
  - `Search_patients_by_name` finds `Patient/pat-ada`.
  - one search + one read per resource type, asserting a known `reference` and a known value from the bundle.
  - `Observations_date_range_filters` returns only the in-range results, newest first.
  - `Count_limit_reports_more`: `count: 2` on a patient with more observations → `returned == 2`, `hasMore == true`.
  - `Read_unknown_id_is_a_tool_error`: `read_patient("does-not-exist")` → `IsError == true`, text contains `404`.
  - `Unknown_patient_search_is_empty_not_error`: `search_conditions("no-such-patient")` → `IsError != true`, `total == 0`.
  - `Document_text_is_decoded`: `read_document_reference` returns the note's first sentence.
- [ ] **Step 4:** `dotnet test` → all pass with HAPI up; `dotnet test --filter "Category!=Integration"` passes with HAPI down. Commit: `Add synthetic data, seed tool and HAPI integration tests`.

### Task 6: Q&A agent

**Files:** `src/FhirMcp.Agent/{FhirMcp.Agent.csproj,Program.cs,AgentOptions.cs,AgentAnswer.cs,SystemPrompt.cs,McpToolBridge.cs,CitationGrounding.cs,ClinicalAgent.cs}`, `tests/FhirMcp.Tests/Unit/{CitationGroundingTests.cs,McpToolBridgeTests.cs}`

**Interfaces — Produces:**
```csharp
public sealed record AgentOptions(string Model = "claude-sonnet-5-5", int MaxTurns = 12, int MaxTokens = 16000);
public sealed record ToolCallRecord(string Name, string ArgumentsJson, string ResultText, bool IsError);
public sealed record AgentAnswer(
    string Answer, bool Answerable, IReadOnlyList<string> Citations,
    IReadOnlyList<string> UngroundedCitations, IReadOnlyList<ToolCallRecord> ToolCalls,
    string? Problem);   // Problem: null, "refusal", "max_turns" or "malformed_answer"
public static class CitationGrounding {
    // Every "ResourceType/id" value of a "reference" property found in any tool result text.
    public static IReadOnlySet<string> RetrievedReferences(IEnumerable<string> toolResultTexts);
    public static IReadOnlyList<string> FindUngrounded(IEnumerable<string> citations, IEnumerable<string> toolResultTexts);
}
public static class McpToolBridge {
    public static Tool ToClaudeTool(McpClientTool tool);   // name, description, input schema copied across
}
public sealed class ClinicalAgent(AnthropicClient claude, McpClient mcp, AgentOptions options) {
    public Task<AgentAnswer> AskAsync(string question, string? patientId, CancellationToken ct);
}
```
The final answer uses structured output with the schema `{ answer: string, answerable: boolean, citations: string[] }`, all required, no additional properties. Thinking is left at the model default; effort `medium`. The system prompt is one fixed string (stable, so it can be cached): answer only from tool results, cite the `reference` of every resource used, set `answerable` to false and say the record does not contain the answer when it does not, never use general medical knowledge to fill a gap, give no treatment advice.

Loop: one assistant turn may hold several tool calls; run each through `mcp.CallToolAsync`, return all results in a single user message, mark MCP errors with `IsError`. Echo assistant content back unchanged, including thinking blocks. Stop on `end_turn`; `refusal` → `Problem = "refusal"`; reaching `MaxTurns` → `Problem = "max_turns"`.

CLI: `dotnet run --project src/FhirMcp.Agent -- --patient pat-ada "What was the latest HbA1c?"` prints the answer, the citations, any ungrounded citations as a warning, and the tool calls made. Missing `ANTHROPIC_API_KEY` → a one-line message and exit code 2.

- [ ] **Step 1: Failing unit tests**
  - `CitationGroundingTests`: tool text containing `"reference":"Condition/cond-ada-t2dm"` → retrieved; citation `Observation/obs-never-fetched` → ungrounded; search envelopes with several results all counted; malformed JSON text ignored without exception; comparison is exact and case-sensitive.
  - `McpToolBridgeTests`: name, description, `properties` and `required` carried over from an MCP tool definition.
- [ ] **Step 2:** Run; expect failures. Implement grounding and bridge; tests pass.
- [ ] **Step 3:** Implement `ClinicalAgent` and `Program.cs`. Build must pass with no API key.
- [ ] **Step 4: Manual check** (human runs it; needs `ANTHROPIC_API_KEY` and HAPI): one answerable question returns an answer with grounded citations; one unanswerable question returns `answerable: false`.
- [ ] **Step 5:** Commit: `Add clinical Q&A agent with citation grounding check`.

### Task 7: Eval suite

**Files:** `evals/FhirMcp.Evals/{FhirMcp.Evals.csproj,Program.cs,EvalCase.cs,EvalScorer.cs,cases.json}`, `tests/FhirMcp.Tests/Unit/{EvalScorerTests.cs,EvalCasesFileTests.cs}`

**Interfaces — Produces:**
```csharp
public sealed record EvalCase(
    string Id, string PatientId, string Question, bool Answerable,
    IReadOnlyList<IReadOnlyList<string>> MustContain,   // each inner list: accepted variants of one fact
    IReadOnlyList<string> MustCite, IReadOnlyList<string> MustNotContain);
public sealed record CaseResult(string Id, bool Passed, IReadOnlyList<string> Failures);
public static class EvalScorer { public static CaseResult Score(EvalCase c, AgentAnswer a); }
```
Scoring rules, each producing a named failure: `answerable` mismatch; a `MustContain` fact with no variant present (case-insensitive, whitespace-normalised); a `MustCite` reference missing from `Citations`; a `MustNotContain` string present; any `UngroundedCitations`; `Problem` not null. An unanswerable case additionally fails if it cites anything listed in its `MustNotContain`.

Runner: `dotnet run --project evals/FhirMcp.Evals -- [--cases evals/FhirMcp.Evals/cases.json] [--threshold 0.85] [--only <id>]`. Starts one MCP server, runs cases one at a time, prints one line per case (`PASS`/`FAIL`, id, failures), then `Score: 22/24 (91.7%), threshold 85.0%`. Exit 0 at or above the threshold, 1 below, 2 when `ANTHROPIC_API_KEY` is missing. Writes `eval-results.json` (ignored by git) with every answer and tool call for inspection.

`cases.json`: 24 cases spread across the three patients: latest and trend lab values, active versus resolved conditions, current versus stopped medications, facts found only in a note, date-bounded questions, one unknown-patient case, and at least six `answerable: false` cases.

- [ ] **Step 1: Failing tests**
  - `EvalScorerTests`: one test per rule above, plus an all-correct answer passing and a variant match (`"7.2 %"` satisfied by `7.2%`).
  - `EvalCasesFileTests` (no API key, no HAPI): file parses; ids unique; at least 20 cases; at least six unanswerable; every `MustCite` reference and every `PatientId` (except the marked unknown-patient case) exists in `data/*.json`; no answerable case has an empty `MustCite`.
- [ ] **Step 2:** Run; expect failures. Implement scorer and write the cases.
- [ ] **Step 3:** Implement the runner. `dotnet test` passes.
- [ ] **Step 4: Manual check** (human, with key): full run prints a score. Review each failure: fix the case if the case is wrong, the prompt or a tool description if the agent is wrong. Record the first score and what changed in the README.
- [ ] **Step 5:** Commit: `Add golden-set eval suite with code-based scoring`.

### Task 8: CI and README

**Files:** `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md` (update commands if they changed)

- [ ] **Step 1:** `ci.yml`, on push and pull request. Job `build-test` on `ubuntu-latest`: `actions/setup-dotnet` 10.0.x, HAPI as a service container on port 8080, `dotnet build -c Release`, seed, `dotnet test -c Release --no-build`. Job `evals` (needs `build-test`): a first step checks whether `secrets.ANTHROPIC_API_KEY` is set and writes a job-summary line `Evals skipped: ANTHROPIC_API_KEY is not set` when it is not; the remaining steps run only when it is; uploads `eval-results.json` as an artifact. Actions pinned to major versions; `permissions: contents: read`.
- [ ] **Step 2:** README sections: what it is; architecture sketch; quick start (Docker, seed, test); the ten tools; connect to Claude Code (`claude mcp add fhir -- dotnet run --project src/FhirMcp.Server`); run the agent; run the evals and how scoring works; guardrails; why a HAPI container and not fixtures; limitations (the spec's out-of-scope list, plus: synthetic data only, not clinically validated, code-based scoring is strict); "How this was built with Claude Code" (what the agent wrote, what the human reviewed and decided, links to the spec and this plan).
- [ ] **Step 3:** Verify every README command by running it. Commit: `Add CI workflow and README`.

### Task 9: Final verification and push

- [ ] **Step 1:** From a clean clone into a temp folder: `dotnet build`, seed, `dotnet test`. All pass.
- [ ] **Step 2:** Search the repo for secrets and real names (`sk-ant`, employer and client names): none.
- [ ] **Step 3:** The human creates the empty public GitHub repository, adds the remote and pushes. The human adds the `ANTHROPIC_API_KEY` repository secret. Confirm both CI jobs are green.
- [ ] **Step 4:** Report the final eval case count for the resume line.
