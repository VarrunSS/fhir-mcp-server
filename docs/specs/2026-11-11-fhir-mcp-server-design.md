# fhir-mcp-server: design

Date: 2026-11-11. Status: awaiting review.

## Purpose

A public portfolio project that shows three things with working code: an MCP
server, an LLM agent that uses it, and automated evals with guardrails.

The resume line it must be able to back up, and nothing more:

> FHIR MCP server (C#, Claude Code): exposes FHIR search and read as agent
> tools, with a clinical Q&A agent and an automated eval suite of [N] cases
> run in CI.

Written from scratch on synthetic data. No employer code, no client names, no
real patient data.

## Architecture

```
            question                 MCP over stdio            HTTP GET only
 user ─────────────► FhirMcp.Agent ───────────────► FhirMcp.Server ───────────► FHIR server
                      (Claude API                    (10 read-only              (local HAPI
                       tool loop)                     tools)                     container)
                          ▲
 FhirMcp.Evals ───────────┘  runs each golden case through the agent and scores it
```

| Project | Kind | Job |
|---|---|---|
| `src/FhirMcp.Server` | console app | MCP server. Owns the FHIR client, the tools and the guardrails. |
| `src/FhirMcp.Agent` | console app + library code | Answers one clinical question through the MCP tools. |
| `tools/FhirMcp.Seed` | console app | Loads the synthetic bundles into a FHIR server. The only code that writes. |
| `tests/FhirMcp.Tests` | xUnit | Unit and integration tests. No API key needed. |
| `evals/FhirMcp.Evals` | console app | Runs the golden set through the agent, prints a score, sets the exit code. |
| `data/` | JSON | Synthetic FHIR R4 transaction bundles. |

All projects target .NET 10.

## MCP server

Package: `ModelContextProtocol` 2.4.0 (checked against its docs on 2026-11-11).
Transport: stdio, because that is what Claude Code launches with one command
and it needs no port or auth. Logs go to stderr, because stdout carries the
protocol.

### Tools

All tools are read-only and are marked as such in their MCP annotations.

| Tool | Inputs | Returns |
|---|---|---|
| `search_patients` | `name?`, `birthDate?`, `identifier?`, `count?` | Patient summaries |
| `read_patient` | `patientId` | One patient summary |
| `search_conditions` | `patientId`, `clinicalStatus?`, `count?` | Condition summaries |
| `read_condition` | `conditionId` | One condition |
| `search_observations` | `patientId`, `code?`, `category?`, `dateFrom?`, `dateTo?`, `count?` | Observation summaries, newest first |
| `read_observation` | `observationId` | One observation |
| `search_medication_requests` | `patientId`, `status?`, `count?` | MedicationRequest summaries |
| `read_medication_request` | `medicationRequestId` | One medication request |
| `search_document_references` | `patientId`, `type?`, `dateFrom?`, `dateTo?`, `count?` | Document metadata plus a short text preview |
| `read_document_reference` | `documentReferenceId` | Document metadata plus its decoded text |

Design rules for the tools:

- **Summaries, not raw FHIR.** Each tool returns a compact JSON summary with
  the fields a clinician question needs, and always a `reference` field such
  as `Condition/cond-ada-diabetes`. Raw FHIR resources are large and mostly
  noise for a model; the `reference` is what the agent cites.
- **Size limits.** `count` defaults to 10 and is capped at 50. Search results
  say when more matches exist than were returned. Document text is cut to a
  preview in search results and to a fixed maximum on read, with a marker
  that says it was cut.
- **Typed inputs.** Each parameter has a type and a description. Ids are
  validated against the FHIR id pattern before they go into a URL.
- **Errors.** A FHIR error response carries an `OperationOutcome`. The server
  turns it into a tool error whose text holds the HTTP status and the
  outcome's issue details, so the model can read what went wrong and react
  (for example "Patient/xyz was not found"). Unexpected exceptions return a
  generic message so internals do not leak.

### Guardrails

- **Read-only by construction.** The FHIR client class has only GET methods.
  There is no code path in the server that sends POST, PUT, PATCH or DELETE.
- **Host allowlist.** The base URL comes from configuration (`Fhir:BaseUrl`,
  overridable by the `FHIR_BASE_URL` environment variable). The server refuses
  to start unless the host is in `Fhir:AllowedHosts` (default: `localhost`,
  `127.0.0.1`, `hapi.fhir.org`) and the scheme is https, or http for loopback
  only. HTTP redirects are not followed, so a redirect cannot move a request
  to a host that is not on the list.
- **No secrets.** The server needs none. The agent reads `ANTHROPIC_API_KEY`
  from the environment only.
- **Synthetic data only.** All patients are invented and hand-written.

## Data

Three invented patients in `data/`, as FHIR R4 transaction bundles, each with
conditions, observations (labs and vitals over time), medication requests and
two or three clinical notes stored as `DocumentReference` with inline text.
Resources use fixed ids (for example `pat-ada`, `obs-ada-hba1c-2026-03`), so
eval cases can name the exact resource an answer must cite.

**Deterministic server: a local HAPI FHIR container** (`hapiproject/hapi`,
pinned tag), started by `docker compose` locally and as a service container in
CI, then loaded by `FhirMcp.Seed`.

Why not recorded HTTP fixtures: the agent chooses its own search queries.
Fixtures can only replay queries recorded in advance, so an agent that words a
search differently would miss through no fault of its own. A real FHIR server
answers any valid query and also catches mistakes in the server's search
parameters. The cost is container start-up time and needing Docker.

The public server `https://hapi.fhir.org/baseR4` stays usable for demos
through the allowlist, but nothing automated depends on it.

## Q&A agent

Package: `Anthropic` (the official C# SDK). Model: configurable, default
`claude-sonnet-5-5`, chosen to keep eval runs cheap.

Flow:

1. Start the MCP server as a child process and list its tools.
2. Convert the MCP tool definitions into Claude API tool definitions.
3. Run a plain tool loop: send the question, execute each tool call through
   the MCP client, send results back, repeat until the model stops or a
   maximum number of turns is reached.
4. Ask for the final answer as structured output: `answer` (text),
   `answerable` (bool) and `citations` (list of FHIR references).

The loop is written by hand rather than with the SDK's beta tool runner. It
is about 60 lines, has no beta dependency, and is the part most worth being
able to explain.

Agent guardrails:

- **Cite or decline.** The system prompt requires every clinical fact to come
  from a tool result and to carry its FHIR reference. If the record does not
  hold the answer, the agent says so and sets `answerable` to false. It does
  not fall back on general medical knowledge.
- **Citation grounding check.** After the model answers, code checks that
  every cited reference was actually returned by a tool call in this run. A
  citation that was never retrieved is flagged in the output and fails the
  eval case.
- **Turn limit** so a confused run ends.
- **Refusals** from the API are reported as such, not treated as an answer.

## Evals

About 24 cases in `evals/cases.json`. Each case has a patient, a question and
expectations:

- `answerable`: whether the record holds the answer.
- `mustContain`: facts the answer must state, each with accepted variants
  (for example `7.2` and `7.2%`).
- `mustCite`: FHIR references the answer must cite.
- `mustNotContain`: optional, for known wrong answers.

At least six cases are "not in the record": the correct result is
`answerable = false` with no invented fact.

Scoring is done in code, with no LLM judge. A case passes only if every check
passes and the grounding check found no ungrounded citation. The runner prints
a table of cases, the pass rate, and exits non-zero under the threshold
(start at 85%, since model output varies between runs). Scoring by code is
strict and can fail a correct answer that is worded unusually; that trade-off
is accepted because the result is easy to defend and costs nothing to run.
The scorer has its own unit tests, which need no API key.

## Tests

| Layer | What it covers | Needs |
|---|---|---|
| Unit | Allowlist, id validation, size limits, `OperationOutcome` mapping, summary mapping, eval scorer | nothing (fake HTTP handler) |
| Integration | Every tool called through a real MCP client against seeded HAPI | Docker |

Integration tests are tagged so `dotnet test --filter Category!=Integration`
runs the unit tests alone.

## CI (GitHub Actions)

- **Job `build-test`**, every push and pull request: restore, build, start the
  HAPI service container, seed, run all tests.
- **Job `evals`**: same setup, then the eval runner. Skipped with a clear
  message when the `ANTHROPIC_API_KEY` secret is not set, so forks and the
  first push stay green.

## Agentic practice kept in the repo

- This spec and an implementation plan, committed before any code.
- `CLAUDE.md`: commands, conventions, what not to touch.
- One small commit per step, each reviewed by the human before the next.
- A documented test loop (`dotnet test` filter for the fast unit tests).
- README section "How this was built with Claude Code", stating plainly what
  the agent wrote and what the human reviewed and decided.

## Out of scope

Write operations, authentication (SMART on FHIR), HTTP transport, FHIR
versions other than R4, resource types beyond the five listed, a web UI, and
any clinical validation. The README will list these as limitations.

## Done

`dotnet build` and `dotnet test` pass, the server answers tool calls against
the seeded container, the agent answers a sample question with citations, the
eval runner prints a score, the CI workflow is in place, and the README is
complete.
