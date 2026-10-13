# Draft issue for microsoft/agent-framework (not filed yet)

> This is a draft for David to review and file. Nothing has been posted.

**Title:** .NET: HarnessAgent in-loop compaction drops new messages when the request ends with the same TodoProvider message as the previous call

**Version:** `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Harness` 1.24.0 (tag `dotnet-1.24.0`, commit `245d4284`)

## Summary

When `HarnessAgentOptions.MaxContextWindowTokens` and `MaxOutputTokens` are both set, `HarnessAgent` adds a `CompactionProvider` to the chat-client pipeline. That provider keeps a `CompactionMessageIndex` in session state between service calls and calls `CompactionMessageIndex.Update(allMessages)` on each call.

`Update` returns early when the last message is content-equal to the last processed message:

```csharp
// CompactionMessageIndex.Update
if (this._lastProcessedMessage is not null &&
    allMessages.Count >= this.RawMessageCount &&
    allMessages[allMessages.Count - 1].ContentEquals(this._lastProcessedMessage))
{
    return;
}
```

`TodoProvider` appends a synthetic `### Current todo list ... - none yet` user message at the end of every run's request. On the next run the list again ends with an identical message, so `Update` returns and **every message inserted before it is ignored**. The model then sees the previous request again.

## Effects observed

- **Second user turn lost:** two plain turns (no tools) in a row. On turn 2 the model does not receive the new user message.
- **LoopAgent feedback lost:** an evaluator's `Continue(feedback)` message is not sent to the model when the previous request also ended with the todo message.
- **Approved tool result lost:** after a `ToolApprovalResponseContent`, the function call and its result are not sent. The model re-issues the same call, causing an approval loop.

None of this happens with the same configuration minus the two window values.

## Repro (xUnit, no model)

```csharp
var model = new RecordingChatClient(); // stores the messages of the last call
var agent = model.AsHarnessAgent(new HarnessAgentOptions
{
    DisableWebSearch = true, DisableFileMemory = true, DisableAgentSkillsProvider = true,
    MaxContextWindowTokens = 1_050_000, MaxOutputTokens = 128_000,
});
var session = await agent.CreateSessionAsync();
await agent.RunAsync("pregunta uno", session);
await agent.RunAsync("pregunta dos", session);
Assert.Contains("pregunta dos", model.LastUserText); // fails: last user messages are "pregunta uno / ### Current todo list"
```

## Workaround

Do not set the window values. Instead, apply the same strategy statelessly on each call:

```csharp
sealed class CompactingChatClient(IChatClient inner, CompactionStrategy strategy) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
        await base.GetResponseAsync(await CompactionProvider.CompactAsync(strategy, m, cancellationToken: ct), o, ct);
    // same for streaming
}
```

## Suggested fix

The early return should also require that the message *count* is unchanged, or should compare the whole tail. Alternatively, `TodoProvider`'s synthetic message could be excluded from the index's "last processed" bookkeeping.
