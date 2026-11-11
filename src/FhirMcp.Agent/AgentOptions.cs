namespace FhirMcp.Agent;

/// <summary>
/// Knobs for one agent run. MaxTurns caps the number of API calls so a model that keeps
/// calling tools cannot loop (and spend) forever; MaxTokens bounds one response, thinking included.
/// </summary>
public sealed record AgentOptions(string Model = "claude-sonnet-5-5", int MaxTurns = 12, int MaxTokens = 16000);
