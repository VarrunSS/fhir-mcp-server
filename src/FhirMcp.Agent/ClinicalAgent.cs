using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FhirMcp.Agent;

/// <summary>
/// Answers one clinical question by running Claude against the MCP server's tools.
/// The loop is written by hand (no beta tool runner): call the API, run the tools the model
/// asked for, send the results back, and repeat until the model gives its final answer.
/// </summary>
public sealed class ClinicalAgent(AnthropicClient claude, McpClient mcp, AgentOptions options)
{
    /// <summary>Schema of the final answer, sent with every request as structured output.</summary>
    private static readonly Dictionary<string, JsonElement> AnswerSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            answer = new { type = "string" },
            answerable = new { type = "boolean" },
            citations = new { type = "array", items = new { type = "string" } },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "answer", "answerable", "citations" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    public async Task<AgentAnswer> AskAsync(string question, string? patientId, CancellationToken ct)
    {
        // Step 1: the tools come from the MCP server, so the server stays the single source of truth.
        var tools = new List<ToolUnion>();
        foreach (var tool in await mcp.ListToolsAsync(cancellationToken: ct))
        {
            tools.Add(McpToolBridge.ToClaudeTool(tool));
        }

        // Step 2: history is append-only. Nothing is edited or dropped once added.
        var userText = patientId is null ? question : $"Patient id: {patientId}\n{question}";
        List<MessageParam> messages = [new UserMessageParam { Content = userText }];
        var calls = new List<ToolCallRecord>();

        for (var turn = 1; turn <= options.MaxTurns; turn++)
        {
            // Step 3: one API call. Tools and the output format go on every request; only the
            // response that ends the turn carries the JSON answer. No temperature, tool_choice or
            // thinking settings: this model rejects non-default sampling and forced tool choice.
            var response = await claude.Messages.Create(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                System = SystemPrompt.Text,
                Tools = tools,
                Messages = messages,
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new JsonOutputFormat { Schema = AnswerSchema },
                },
                // Caches the stable prefix (tools, system prompt, earlier turns) between turns.
                CacheControl = new CacheControlEphemeral(),
            }, ct);

            // Step 4: stop reason first, content second.
            // (StopReason is an ApiEnum: compare it with ==, its ToString() includes JSON quotes.)
            if (response.StopReason == "end_turn")
            {
                return Finish(response, calls);
            }
            if (response.StopReason == "refusal")
            {
                return Failed("refusal", "The model declined to answer this request.", calls);
            }
            if (response.StopReason != "tool_use")
            {
                // max_tokens (cut-off JSON), pause_turn, stop_sequence or anything new.
                return Failed("malformed_answer",
                    $"The model stopped with {response.StopReason} before giving a complete answer.", calls);
            }

            // Step 5: echo the assistant turn back exactly as received (thinking blocks keep
            // their signatures) and run every tool_use block in it.
            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults = [];
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                {
                    assistantContent.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                {
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });

                    var (resultText, isError) = await RunToolAsync(toolUse, ct);
                    calls.Add(new ToolCallRecord(toolUse.Name, JsonSerializer.Serialize(toolUse.Input), resultText, isError));
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = resultText, IsError = isError });
                }
            }

            // Step 6: the assistant message, then ALL of this turn's tool results in ONE user message.
            messages.Add(new AssistantMessageParam { Content = assistantContent });
            messages.Add(new UserMessageParam { Content = toolResults });
        }

        return Failed("max_turns", $"No final answer after {options.MaxTurns} model calls.", calls);
    }

    /// <summary>
    /// Runs one tool through MCP. A failing tool must not end the run: the error text goes back
    /// to the model as a tool_result flagged as an error, so it can try something else.
    /// </summary>
    private async Task<(string Text, bool IsError)> RunToolAsync(ToolUseBlock toolUse, CancellationToken ct)
    {
        try
        {
            var arguments = toolUse.Input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            var result = await mcp.CallToolAsync(toolUse.Name, arguments, cancellationToken: ct);
            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
            return (text, result.IsError == true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ($"Tool call failed: {ex.Message}", true);
        }
    }

    private static AgentAnswer Finish(Message response, List<ToolCallRecord> calls)
    {
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var parsed = AgentAnswerParser.TryParse(text);
        if (parsed is null)
        {
            return Failed("malformed_answer", "The model's final reply was not the expected JSON answer.", calls);
        }

        var ungrounded = CitationGrounding.FindUngrounded(parsed.Citations, calls.Select(c => c.ResultText));
        return new AgentAnswer(parsed.Answer, parsed.Answerable, parsed.Citations, ungrounded, calls, null);
    }

    private static AgentAnswer Failed(string problem, string explanation, List<ToolCallRecord> calls) =>
        new(explanation, false, [], [], calls, problem);
}
