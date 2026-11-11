using System.Text.Json;

namespace FhirMcp.Agent;

/// <summary>The three fields the model's final JSON carries.</summary>
public sealed record ParsedAnswer(string Answer, bool Answerable, IReadOnlyList<string> Citations);

/// <summary>
/// Turns the model's final structured-output text into a <see cref="ParsedAnswer"/>.
/// The API constrains the output to the schema, but this stays defensive: a truncated or
/// otherwise odd response must become a "malformed_answer" problem, not an exception.
/// </summary>
public static class AgentAnswerParser
{
    public static ParsedAnswer? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("answerable", out var answerable)
                || answerable.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("citations", out var citations) || citations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<string>();
            foreach (var item in citations.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return null;
                }
                list.Add(item.GetString()!);
            }

            return new ParsedAnswer(answer.GetString()!, answerable.GetBoolean(), list);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
