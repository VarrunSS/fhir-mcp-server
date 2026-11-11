# fhir-mcp-server

C# MCP server exposing read-only FHIR tools, a Claude Q&A agent that uses it,
and a golden-set eval suite. Design: `docs/specs/`. Plan: `docs/plans/`.

## Commands

```
dotnet build
dotnet test --filter "Category!=Integration"    # fast loop, no Docker, no API key
docker compose up -d                            # local HAPI FHIR on :8080
dotnet run --project tools/FhirMcp.Seed         # load data/ into local HAPI
dotnet test                                     # everything, needs HAPI up
dotnet run --project evals/FhirMcp.Evals        # agent evals, needs ANTHROPIC_API_KEY
```

Run the fast loop after every change. Run the full `dotnet test` before each commit
that touches tools, data or the server host.

## Rules

- The server is read-only. Never add a tool or client method that sends
  anything but HTTP GET. `tools/FhirMcp.Seed` is the only code that writes,
  and only to a loopback host.
- Synthetic data only. Never add real patient data, real people's names, or
  any employer or client name.
- No secrets in the repo. Never read, print, ask for or store an API key;
  the agent reads `ANTHROPIC_API_KEY` from the environment.
- Every tool summary keeps its `reference` field (`ResourceType/id`); the
  agent's citations and the evals depend on it.
- Changing a fixed id or a value in `data/` means updating `evals/FhirMcp.Evals/cases.json`
  and the integration tests in the same commit.
- Do not weaken a failing eval case or lower the threshold to get a green
  build. Fix the agent, the tool, or a case that is actually wrong, and say which.
- Write the test first. One task from the plan per commit; stop for human
  review after each.

## Conventions

- .NET 10, nullable on, warnings as errors.
- `System.Text.Json` for FHIR; no FHIR SDK.
- Tool names are snake_case; C# follows the usual .NET naming.
- stdout of the server belongs to the MCP protocol. Log to stderr only.

## Do not touch without asking

- `Fhir:AllowedHosts` defaults and `FhirBaseUrlValidator`.
- The eval threshold and scoring rules in `EvalScorer`.
- Git config, remotes and pushes: the human does these.
