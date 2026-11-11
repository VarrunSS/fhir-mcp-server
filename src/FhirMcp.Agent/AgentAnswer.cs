namespace FhirMcp.Agent;

/// <summary>One tool call the model made, kept so a run can be inspected and citations checked against it.</summary>
public sealed record ToolCallRecord(string Name, string ArgumentsJson, string ResultText, bool IsError);

/// <summary>
/// The outcome of one question. <paramref name="Problem"/> is null on success, otherwise
/// "refusal", "max_turns" or "malformed_answer"; then <paramref name="Answer"/> is a short
/// explanation, <paramref name="Answerable"/> is false and <paramref name="Citations"/> is empty.
/// <paramref name="UngroundedCitations"/> lists cited references no tool call ever returned.
/// </summary>
public sealed record AgentAnswer(
    string Answer,
    bool Answerable,
    IReadOnlyList<string> Citations,
    IReadOnlyList<string> UngroundedCitations,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    string? Problem);
