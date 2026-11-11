# fhir-mcp-server

[![CI](https://github.com/VarrunSS/fhir-mcp-server/actions/workflows/ci.yml/badge.svg)](https://github.com/VarrunSS/fhir-mcp-server/actions/workflows/ci.yml)

A C# [MCP](https://modelcontextprotocol.io) server that exposes read-only FHIR search and read operations as agent tools, a small Claude-backed clinical Q&A agent that answers questions through those tools, and a golden-set eval suite that scores the agent with plain code and runs in CI when an API key is available.

**All data is synthetic, and this is a portfolio and learning project. It is not for clinical use.** The three patients in `data/` are invented, and nothing here has been clinically validated.

## Architecture

```
            question                 MCP over stdio            HTTP GET only
 user ─────────────► FhirMcp.Agent ───────────────► FhirMcp.Server ───────────► FHIR server
                      (Claude API                    (10 read-only              (local HAPI
                       tool loop)                     tools)                     container)
                          ▲
 FhirMcp.Evals ───────────┘  runs each golden case through the agent and scores it
```

| Path | What it is |
| --- | --- |
| `src/FhirMcp.Server` | The MCP server (stdio). Owns the FHIR client, the ten tools and the server-side guardrails. |
| `src/FhirMcp.Agent` | Console app and library: starts the server as a child process and runs a hand-written Claude tool loop to answer one question. |
| `evals/FhirMcp.Evals` | Runs the golden cases in `cases.json` through the agent, scores them in code, prints a score and sets the exit code. |
| `tools/FhirMcp.Seed` | Loads `data/` into a FHIR server. The only code in the repo that writes, and only to a loopback host. |
| `tests/FhirMcp.Tests` | xUnit tests: unit tests that need nothing, and integration tests (tagged `Integration`) that need the HAPI container. |
| `data/` | Three synthetic patients as FHIR R4 transaction bundles, with fixed resource ids. See `data/README.md`. |

All projects target .NET 10. The FHIR JSON is handled with `System.Text.Json`; there is no FHIR SDK.

## Quick start

You need the .NET 10 SDK and Docker.

```
docker compose up -d
dotnet run --project tools/FhirMcp.Seed
dotnet test
```

HAPI takes a minute or two to start. The seed tool waits for it (up to 180 seconds by default; `--wait-seconds N` changes that), so you can run it straight after `docker compose up -d`. The bundles use `PUT` with fixed ids, so seeding twice changes nothing. The HAPI data lives in the container, so after `docker compose down` and `up` you need to seed again.

`dotnet test` runs everything and needs HAPI up and seeded. The fast loop needs no Docker and no API key:

```
dotnet test --filter "Category!=Integration"
```

## The tools

Ten tools, all marked read-only in their MCP annotations. Search tools take a `patientId` (the part after `Patient/`) and, where listed, optional filters. Every search also takes `count` (default 10, maximum 50). Optional parameters are marked `?`.

| Tool | Parameters | Returns |
| --- | --- | --- |
| `search_patients` | `name?`, `birthDate?` (YYYY-MM-DD), `identifier?`, `count?` | Patient summaries. Supply at least one criterion. |
| `read_patient` | `patientId` | One patient summary (demographics). |
| `search_conditions` | `patientId`, `clinicalStatus?`, `count?` | A patient's conditions, most recently recorded first: name, clinical status, onset. |
| `read_condition` | `conditionId` | One condition summary. |
| `search_observations` | `patientId`, `code?` (LOINC), `codeText?`, `category?`, `dateFrom?`, `dateTo?`, `count?` | Lab results, vital signs and social history, newest first: what was measured, value with unit, date. |
| `read_observation` | `observationId` | One observation summary. |
| `search_medication_requests` | `patientId`, `status?`, `count?` | Medication orders, most recently written first: medication, status, date, dosage. A stopped or completed request is not a current medication. |
| `read_medication_request` | `medicationRequestId` | One medication request summary. |
| `search_document_references` | `patientId`, `typeText?`, `dateFrom?`, `dateTo?`, `count?` | Clinical documents, newest first: type, date, author and a 500-character text preview with a `textTruncated` flag. |
| `read_document_reference` | `documentReferenceId` | One document with its decoded text, cut at 20,000 characters (`textTruncated` says so). |

Dates are `YYYY`, `YYYY-MM` or `YYYY-MM-DD`.

Tools return compact JSON summaries, not raw FHIR. Every summary carries a `reference` field of the form `ResourceType/id` (for example `Condition/cond-ada-t2dm`). That field is what the agent cites and what the evals check, so it is never dropped.

Every search tool wraps its summaries in the same envelope:

```json
{ "total": 3, "returned": 3, "hasMore": false, "results": [ ... ] }
```

`total` is the server's match count when it reports one (otherwise null), `returned` is how many summaries are in `results`, and `hasMore` is true when the FHIR server offered a next page, meaning the list was cut by `count` and the caller should narrow the search or raise `count`.

## Connect it to Claude Code

Build first, then register the built DLL:

```
dotnet build -c Release
claude mcp add fhir -- dotnet <absolute path to repo>/src/FhirMcp.Server/bin/Release/net10.0/FhirMcp.Server.dll
```

The DLL is used instead of `dotnet run --project ...` on purpose. The server talks MCP over stdio, so its stdout carries the protocol; `dotnet run` can print build output to stdout, which would corrupt it. The server itself logs to stderr only. It finds its `appsettings.json` next to the DLL, so it works from whatever folder Claude Code starts it in.

By default the server reads `http://localhost:8080/fhir`, so HAPI must be running and seeded. `FHIR_BASE_URL` overrides the base URL. For example, to point it at the public HAPI demo server (`hapi.fhir.org` is on the allowlist; that server holds other people's test data, not this repo's patients):

```
claude mcp add fhir --env FHIR_BASE_URL=https://hapi.fhir.org/baseR4 -- dotnet <absolute path to repo>/src/FhirMcp.Server/bin/Release/net10.0/FhirMcp.Server.dll
```

Prompts to try against the seeded local data:

- "Using the fhir tools, what is the most recent HbA1c for patient pat-ada, and when was it taken?"
- "Why was Ada Quillfeather's glipizide stopped? Check the notes as well as the medication record."
- "Which of pat-chidi's medications are currently active?"

## Run the Q&A agent

The agent needs an Anthropic API key in the environment. Set `ANTHROPIC_API_KEY` yourself; the repo never reads it from a file and never stores it.

```
dotnet run --project src/FhirMcp.Agent -- --patient pat-ada "What is the patient's most recent HbA1c, and when was it taken?"
```

`--patient` is optional; without it the agent finds the patient with `search_patients`. The agent starts the MCP server as a child process (honouring `FHIR_BASE_URL`), so HAPI must be up and seeded.

The output, in order: the answer text; `Answerable: yes` or `no`; the list of citations (FHIR references); a `WARNING: cited but never retrieved:` block if any citation did not come from a tool result in this run; and the list of tool calls made, with their arguments, marked `ERROR` where a call failed. If the run ended abnormally (the model refused, the turn limit was reached, or the final reply was not the expected JSON) it prints `Problem: ...` instead of the answerable and citations lines and exits with code 1. A missing key exits with code 2 before anything is started.

The model defaults to `claude-sonnet-5-5`. Set `AGENT_MODEL` to use another.

## Evals

### What a case is

`evals/FhirMcp.Evals/cases.json` is a list of golden questions. Each case has:

| Field | Meaning |
| --- | --- |
| `id`, `patientId`, `question` | Which patient to ask about, and what to ask. |
| `answerable` | Whether the record holds the answer. If not, the right result is `answerable = false` and no invented fact. |
| `mustContain` | A list of facts. Each fact is a list of accepted wordings (for example `7.2` and `7.2%`); any one of them satisfies it. |
| `mustCite` | FHIR references the answer must cite, spelled exactly. |
| `mustNotContain` | Strings that must not appear in the answer (known wrong answers). |
| `notes` | For the human reader; never scored. |
| `unknownPatient` | Marks the one case whose patient id deliberately does not exist. |

The file has 24 cases: 8 for Ada, 8 for Bram, 7 for Chidi and 1 for a patient id that does not exist. 8 of the 24 are unanswerable (the record deliberately lacks the fact, for example allergies, or Bram's weight; see "Deliberately absent facts" in `data/README.md`). 6 require citing a `DocumentReference`, because the fact exists only in a clinical note.

### How a case is scored

`EvalScorer` applies six rules, in code. A case passes only if none of them fails:

1. The agent run must have ended normally (no refusal, turn-limit stop or malformed final reply). If it did not, the case fails and the other rules are skipped.
2. `answerable` in the answer must equal the case's `answerable`.
3. Every `mustContain` fact must appear in the answer text, in at least one of its accepted wordings (case-insensitive, whitespace collapsed).
4. Every `mustCite` reference must be among the answer's citations, spelled exactly.
5. No `mustNotContain` string may appear in the answer.
6. No citation may be ungrounded, meaning cited but never returned by a tool call in that run.

There is no LLM judge. The reason is that plain string checks give the same verdict for the same answer every time, a failure names the exact rule that broke, they cost nothing to run, and they are easy to defend. The cost is strictness: a correct answer that is worded in a way no accepted variant covers will fail, and the remedy is to widen that case's accepted wordings, not to loosen the rules. The scorer has its own unit tests, which need no API key.

### Running them

The suite needs HAPI up and seeded, and `ANTHROPIC_API_KEY` set.

```
dotnet run --project evals/FhirMcp.Evals
dotnet run --project evals/FhirMcp.Evals -- --only ada-01-latest-hba1c
```

Other options: `--cases <path>`, `--threshold <0 to 1>` and `--out <path>`. The runner prints one `PASS` or `FAIL` line per case (with the reasons for a failure), then a `Score:` line and `RESULT: PASS` or `RESULT: FAIL`. The pass threshold is 0.85 (at least 85% of cases). Exit codes: 0 when the score meets the threshold, 1 when it does not, and 2 for a usage problem, a missing API key or an unreadable cases file.

The runner writes `eval-results.json` (git-ignored) with the model, passed and total counts, the threshold and, for every case, the question, verdict, failure reasons, the agent's answer, its citations and ungrounded citations, any problem, and the tool calls made (name, arguments, whether it errored). Tool result text is left out to keep the file small.

### In CI

`.github/workflows/ci.yml` has two jobs. `build-test` runs on every push and pull request: restore, build, the unit tests, seeding a HAPI service container, then all tests. The `evals` job runs after it. It first checks whether the `ANTHROPIC_API_KEY` repository secret exists; if not, it writes `Evals skipped: ANTHROPIC_API_KEY is not set` to the job summary and skips every other step, so forks and repositories without the secret stay green. If the secret is present it runs the suite, appends the `Score:` and `RESULT:` lines to the job summary, and uploads `eval-results.json` as an artifact. Pull requests from forks skip the job.

### Results

No scored run has been recorded yet, because the suite needs an API key that was not available while the project was being built. The first recorded score, the model used and any cases changed as a result will be added here.

## Guardrails

| Guardrail | Where |
| --- | --- |
| **Read-only by construction.** The FHIR client has only `ReadAsync` and `SearchAsync`, both HTTP GET. Every tool is annotated read-only, and a test pins the exact list of ten tools and checks every one is read-only, so a new or write tool cannot appear unnoticed. | `src/FhirMcp.Server/Fhir/FhirClient.cs`; `tests/FhirMcp.Tests/Integration/ServerStartupTests.cs` |
| **Host allowlist.** The base URL's host must be in `Fhir:AllowedHosts` (default `localhost`, `127.0.0.1`, `hapi.fhir.org`), compared as a whole host. The scheme must be https, or http for loopback only. A URL with user information is refused. The server will not start otherwise. | `src/FhirMcp.Server/Fhir/FhirBaseUrlValidator.cs`, `FhirOptions.cs`, `appsettings.json` |
| **No redirects.** Automatic redirects are off, and a 3xx response is turned into an error, so a redirect cannot move a request to a host that is not on the list. | `src/FhirMcp.Server/Program.cs`, `Fhir/FhirClient.cs` |
| **Validation before any request.** Ids must match the FHIR id pattern (so `pat-1/_history/1` or `x?_count=999` cannot change the URL). Date arguments must be `YYYY`, `YYYY-MM` or `YYYY-MM-DD`. | `Fhir/FhirId.cs`, `Tools/FhirQuery.cs` |
| **Size limits.** `count` is clamped to 1 to 50 whatever the model asks for. Document text is cut to 500 characters in searches and 20,000 on read, with a `[truncated]` marker. | `Tools/ToolRunner.cs`, `Fhir/FhirOptions.cs`, `Summaries/DocumentReferenceSummary.cs` |
| **Error handling.** For a failed FHIR request, only the text of a well-formed `OperationOutcome` is passed to the model (with the HTTP status); any other response body is dropped. Unreachable server, timeout and redirects each get their own message. Unexpected exceptions are not converted, so the MCP SDK shows the model only a generic error. | `Fhir/FhirClient.cs`, `Fhir/OperationOutcomeParser.cs`, `Tools/ToolRunner.cs` |
| **Seed tool is loopback-only.** It applies the same allowlist, then refuses any host that is not loopback before it sends anything. | `tools/FhirMcp.Seed/SeedTarget.cs`, `Program.cs` |
| **Cite or decline.** The system prompt requires every fact to come from a tool result and to carry its reference, and tells the agent to say so and set `answerable` to false when the record lacks the answer, instead of using general medical knowledge. | `src/FhirMcp.Agent/SystemPrompt.cs` |
| **Citation grounding check.** After the answer, code checks that each cited reference appeared in a tool result in this run; ungrounded ones are flagged in the output and fail the eval case. | `src/FhirMcp.Agent/CitationGrounding.cs` |
| **Turn limit and refusals.** The loop stops after 12 model calls. A refusal, the turn limit and a malformed final reply are reported as a `Problem`, not returned as an answer. | `src/FhirMcp.Agent/AgentOptions.cs`, `ClinicalAgent.cs` |
| **No secrets in the repo.** The server needs none. The agent and eval runner read `ANTHROPIC_API_KEY` from the environment only, and in CI it is passed only to the one step that runs the evals. `.env` files are git-ignored. | `src/FhirMcp.Agent/Program.cs`, `.github/workflows/ci.yml`, `.gitignore` |

## Why a HAPI container and not recorded fixtures

The agent chooses its own search queries. Recorded HTTP fixtures can only replay the queries recorded in advance, so an agent that words a search differently would miss through no fault of its own. A real FHIR server answers any valid query, and it also catches mistakes in the server's search parameters. The cost is Docker and container start-up time. The image tag in `docker-compose.yml` is pinned (`hapiproject/hapi:v8.12.0-2`), because the integration tests assert on HAPI's search behaviour and an unannounced upgrade must not change it.

The container also turned up a pitfall, recorded in `docker-compose.yml`: HAPI caches identical searches for 60 seconds by default, so a search made before seeding kept answering "0 results" after it. The compose file and the CI service container both set `HAPI_FHIR_REUSE_CACHED_SEARCH_RESULTS_MILLIS` to `-1` to turn that cache off.

The public `https://hapi.fhir.org/baseR4` stays usable for demos through the allowlist, but nothing automated depends on it.

## Limitations

- Out of scope by design: write operations, authentication (no SMART on FHIR), HTTP transport (stdio only), FHIR versions other than R4, resource types beyond Patient, Condition, Observation, MedicationRequest and DocumentReference, and a web UI.
- Synthetic data only. Nothing here is clinically validated, and the agent's answers must not be used for care.
- Code-based scoring is strict: a correctly reworded answer can fail a case.
- Three patients and 24 cases is a small world. A score on it says little about other data.
- Agent answers vary between runs, which is why the threshold is 85% and not 100%.
- The agent loop (`ClinicalAgent`) has no unit tests, because it needs a live API. Only its pure parts do: answer parsing, citation grounding, the MCP-to-Claude tool conversion and the eval scorer.
- The citation check proves a cited resource was retrieved, not that it supports the claim it is attached to.
- `--patient` is a hint in the prompt, not a restriction: the tools are not scoped to one patient.
- `DocumentReference` text is available only when the attachment is inline base64 with a `text/*` content type. PDFs, images and attachments given as URLs report that no inline text is available; the server never fetches attachment URLs.

## How this was built with Claude Code

What the human (the repo owner) did and decided:

- Chose the project and its scope, and set the constraints: read-only, synthetic data only, written from scratch, and honest claims.
- Approved the design, then the written spec, then the implementation plan, each before any code was written.
- Chose the HAPI container over recorded fixtures when offered both.

What Claude wrote:

- Claude Fable 5.2, running in Claude Code, wrote the [design spec](docs/specs/2026-11-11-fhir-mcp-server-design.md), the [implementation plan](docs/plans/2026-11-11-fhir-mcp-server.md) and `CLAUDE.md`, and implemented Tasks 1 to 4 (scaffold and allowlist, FHIR client, server host and patient tools, remaining tools), test-first.
- Tasks 1 to 3 were each stopped for the human to read before the next began. From Task 4 on, at the human's request, the tasks ran back to back without a review stop between them.
- For Tasks 5 to 8 (data, seed tool and integration tests; agent; evals; CI and this README) the human asked for a split: Claude Sonnet 5.5 subagents wrote the code from written briefs, and Fable 5.2 wrote the briefs, read the diffs and re-ran the tests before accepting each task. The commit trailers (`git log`) show which model wrote each commit.

Things the agents got wrong or ran into along the way:

- A shell-quoting failure wrote no files and had to be redone.
- The HAPI 60-second search cache hid freshly seeded data (see above).
- The C# SDK's stop-reason `ToString()` includes JSON quotes, which would have made every answer look malformed. The agent compares with `==` instead.
- A C# raw-string brace-count error in a test failed to compile.

What was not done by an agent and remains the human's: setting the API key, the first live agent run and eval run, creating the GitHub repository and pushing.

Practices used: the spec and plan committed before code; `CLAUDE.md` rules; one commit per task; tests written before implementation; and a fast unit-test loop kept separate from the Docker-dependent tests.
