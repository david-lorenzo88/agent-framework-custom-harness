# Draft issue for microsoft/agent-framework (not filed yet)

> This is a draft for David to review and file. Nothing has been posted.

**Title:** .NET: a network error after a tool call leaves the HarnessAgent session with an orphan function call (every later request fails with HTTP 400)

**Version:** `Microsoft.Agents.AI.Harness` 1.24.0 (tag `dotnet-1.24.0`). Azure AI Foundry Responses API with `store` disabled (`AsIChatClientWithStoredOutputDisabled`), `gpt-5.4`.

## Summary

`HarnessAgent` persists the chat history per service call (`UsePerServiceCallChatHistoryPersistence`). Here is what happens when the connection drops on the call that carries a tool result:

1. Model call N returns a `function_call`. It is persisted.
2. `FunctionInvokingChatClient` runs the tool. The result goes into the request for call N+1.
3. Call N+1 fails (connection reset, network timeout). Nothing from that request is persisted.

The session now holds a `function_call` without a `function_call_output`. With `store` disabled the whole history is sent on every request, and the Responses API rejects every later request:

```
HTTP 400 (invalid_request_error: )
Parameter: input
No tool output found for function call call_SYM1DMLSiA27u70Pcsn75qV8.
```

The session can't be used again. Telling the agent to continue doesn't help. We hit this once in about 25 long runs against Foundry.

## Repro (no model)

See `tests/UpgradeCaptain.Harness.Tests/ToolCallRepairTests.cs`. A fake `IChatClient` asks for a tool on call 1 and throws `IOException` on call 2. On the next `RunAsync("Sigue", session)` the history sent to the model contains the call without its result.

## Workaround

A `DelegatingChatClient` next to the leaf client adds a synthetic `FunctionResultContent` ("interrupted, call again if needed") for every `FunctionCallContent` without a result. Note: it can't filter on `InformationalOnly`, because `FunctionInvokingChatClient` sets that flag on calls it already executed, and the orphan is one of those.

## Suggestions

- Persist tool results as soon as they exist, before the next service call. Or persist the request messages even when the call fails.
- Or repair orphan calls when the history is loaded or sent.
