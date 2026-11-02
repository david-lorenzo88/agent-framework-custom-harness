# Q3: Resuming a parked tool approval after `DeserializeSessionAsync`

**Answer: use option (b).** Build the approval response from the `ToolApprovalRequestContent` you saved when you parked the run. Store it in your park record next to the session JSON. When the approval arrives, call `request.CreateResponse(approved, reason)`. Option (a), reading the request back out of the restored history, **does not work for `HarnessAgent`**. The harness persists history per service call, below `FunctionInvokingChatClient`, so the history holds only the raw `FunctionCallContent`. It never holds a `ToolApprovalRequestContent`.

The binding decorator does not use the history at all. It trusts only its own record, which is stored in the session `StateBag` under key `_pendingApprovalRequests` and matched by `RequestId`. That record is part of the session JSON, so it survives `SerializeSessionAsync` and `DeserializeSessionAsync`. An end-to-end probe confirmed all of this (see "Empirical probe").

- **Source:** tag `dotnet-1.24.0`, at `.ref/af/dotnet/`. All `file:line` references below are relative to that folder.
- **External package:** Microsoft.Extensions.AI 10.10.0, with Abstractions 10.10.1 (`Directory.Packages.props:87-88`).
- **Experiment:** run on 2026-10-13 with .NET SDK 10.0.107. Packages were restored offline from `~/.nuget/packages`, using `Microsoft.Agents.AI 1.24.0`.
- **Scratch projects:** `<scratchpad>/q3e2e/` (end-to-end) and `.../scratchpad/q3probe/` (checks `CreateResponse` and the JSON round-trip).

---

## Recommended recipe

```csharp
// ===== PARK (inside CodeOwnerApprovalAgent.RunCoreAsync) =====
AgentResponse response = await this.InnerAgent.RunAsync(messages, session, options, ct);

var parked = response.Messages.SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()
    .Where(r => r.ToolCall is FunctionCallContent fc && IsProtectedPath(fc))
    .ToList();

if (parked.Count > 0)
{
    // Serialize only AFTER the inner run has fully completed. For streaming, that means after the
    // enumerator has finished or been disposed, because the binding writes its record in a finally block.
    JsonElement sessionJson = await this.SerializeSessionAsync(session, cancellationToken: ct); // delegates down to ChatClientAgent

    var record = new ParkRecord
    {
        SessionJson = sessionJson.GetRawText(),
        // Save the full request: RequestId plus the FunctionCallContent (polymorphic "$type": "toolApprovalRequest").
        Requests = parked.Select(r => JsonSerializer.Serialize<AIContent>(r, AIJsonUtilities.DefaultOptions)).ToList(),
        // Also store the requester, the protected paths, and the required owners.
    };
    await parkStore.SaveAsync(record, ct);
    // Mark the session as PARKED. No other RunAsync may run on it until the approval is resolved (see caveat 1).
    return StripApprovalRequests(response, parked);
}

// ===== RESUME (possibly in another process, with a newly built agent configured the same way) =====
AgentSession session = await agent.DeserializeSessionAsync(
    JsonDocument.Parse(record.SessionJson).RootElement, cancellationToken: ct);

var request = (ToolApprovalRequestContent)JsonSerializer.Deserialize<AIContent>(
    record.Requests[0], AIJsonUtilities.DefaultOptions)!;

// Microsoft.Extensions.AI.ToolApprovalRequestContent.CreateResponse(bool approved, string? reason = null)
//   -> Microsoft.Extensions.AI.ToolApprovalResponseContent { RequestId = request.RequestId, ToolCall = request.ToolCall (same ref), Reason }
// Equivalent: new ToolApprovalResponseContent(request.RequestId, approved, request.ToolCall) { Reason = reason }
ToolApprovalResponseContent answer = request.CreateResponse(approved: true, reason: $"Approved by code owner {approverId}");

// If several requests were parked from the SAME turn, put ALL the answers in ONE message (see caveat 4).
// Call the OUTERMOST agent (CodeOwnerApprovalAgent) so that LoopAgent and ToolApprovalAgent take part,
// and any new protected request gets parked again.
AgentResponse resumed = await agent.RunAsync(new ChatMessage(ChatRole.User, [answer]), session, cancellationToken: ct);

// Persist the session again. "resumed" may itself contain another ToolApprovalRequestContent
// (for example the next one queued by ToolApprovalAgent), which would need parking again.
```

Optional pre-flight diagnostic (uses an internal key name, so treat it as a diagnostic only and do not mutate the returned list):

```csharp
bool armed = session.StateBag.TryGetValue<List<ToolApprovalRequestContent>>(
                 "_pendingApprovalRequests", out var pending, AIJsonUtilities.DefaultOptions)
             && pending!.Any(p => p.RequestId == request.RequestId);
```

**Do not use** `request.CreateAlwaysApproveToolResponse()` or `CreateAlwaysApproveToolWithArgumentsResponse()` for code-owner approvals (see caveat 5).

---

## 1. Where the binding stores surfaced requests, and whether the record survives serialization

- **Not only a harness feature.** `ChatClientBuilderExtensions.UseApprovalResponseBinding()` (`src/Microsoft.Agents.AI/ChatClient/ChatClientBuilderExtensions.cs:188-231`) adds the internal class `ApprovalResponseBindingChatClient` (`src/Microsoft.Agents.AI/ChatClient/ApprovalResponseBindingChatClient.cs`). `HarnessAgent` adds it manually as the outermost chat-client decorator (`src/Microsoft.Agents.AI.Harness/HarnessAgent.cs:256-259`), because the harness uses `UseProvidedChatClientAsIs = true` (`HarnessAgent.cs:293`). A plain `ChatClientAgent` gets the same decorator by default (`src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs:55-65`).
- **Storage:**
  - **Location:** `session.StateBag`, under key `"_pendingApprovalRequests"` (`ApprovalResponseBindingChatClient.cs:62`).
  - **Value:** a `List<ToolApprovalRequestContent>`, written with `AgentJsonUtilities.DefaultOptions` (`:563-579`).
  - **Which session:** the current one, taken from the ambient `AIAgent.CurrentRunContext?.Session` (`:170-186`). With no run context, binding is skipped and a warning is logged.
  - **Keying:** in memory it is keyed by `RequestId` (`LoadPendingApprovalRequestLookup`, `:445-455`).
  - **Snapshots:** each request is stored as a deep-ish snapshot, a cloned `FunctionCallContent` (`:548-561`).
  - **Collisions:** if two different tool calls share one `RequestId`, that id is "poisoned" and never honored (`:493-542`).
- **When the record is written:**
  - Non-streaming: after the inner call returns, `RecordPendingApprovalRequests(response.Messages)` (`:100`).
  - Streaming: in a `finally` block, even if the consumer stops early (`:148-162`).
- **When the record is consumed:** the whole key is removed after **any successful** run that started with pending records (`:92-98`, `:150-153`, and the reasoning at `:218-222`). A failed run keeps the record so the same approval can be retried.
- **It survives serialization:**
  - `AgentSession.StateBag` is serialized as `"stateBag"` (`src/Microsoft.Agents.AI.Abstractions/AgentSession.cs:79-85`). The docs say "Data stored in the StateBag will be included when the session is serialized".
  - `AgentSessionStateBag.Serialize`/`Deserialize` cover the whole dictionary (`AgentSessionStateBag.cs:124-144`). Each value re-serializes from its typed cache (`AgentSessionStateBagValue.cs:42-58`).
  - `ChatClientAgentSession` serializes `conversationId` and `stateBag` (`src/Microsoft.Agents.AI/ChatClient/ChatClientAgentSession.cs:29-33, 52-53, 73-90`).
  - Every decorator (`HarnessAgent`, `LoopAgent`, `ToolApprovalAgent`, `OpenTelemetryAgent`, and your `DelegatingAIAgent`) passes serialization down to `ChatClientAgent.SerializeSessionCoreAsync` and `DeserializeSessionCoreAsync` (`DelegatingAIAgent.cs:80-85`, `ChatClientAgent.cs:472-487`). None of them override it.
  - The probe confirmed the serialized JSON is `conversationId = "_agent_local_chat_history"` plus `stateBag` keys `[_pendingApprovalRequests, toolApprovalState, InMemoryChatHistoryProvider, MessageInjectingChatClient.PendingInjectedMessages]`.
  - After deserializing into a **new agent instance**, `_pendingApprovalRequests` contained `ficc_call1/write_file`.
- **First-party session stores work the same way:** `InMemoryAgentSessionStore`, `FileSystemAgentSessionStore`, `AzureBlobAgentSessionStore`, and `FoundryAgentSessionStore` all simply call `agent.SerializeSessionAsync` (for example `src/Microsoft.Agents.AI.Foundry.Hosting/FileSystemAgentSessionStore.cs:158`). Your save-to-disk approach is the supported model. The binding's own warning says so: "register an AgentSessionStore so the approval request is recorded server-side" (`ApprovalResponseBindingChatClient.cs:584`).

## 2. What the binding does with an inbound `ToolApprovalResponseContent`

From `ValidateInboundApprovalResponses` (`:206-292`) and `BindApprovalResponses` (`:301-358`):

- **Authority:** the only authority is the `StateBag` record. Requests in the inbound messages or the history are never trusted (`:42-48`, `:212-216`).
- **Match key:** `response.RequestId` (`:328`). The tool call's `CallId` plays no part in matching.
- **Settled exemption:** if a `FunctionResultContent` with `response.ToolCall.CallId` is already in the inbound messages, the response is left untouched as settled history (`:233-249`, `:319-326`). Function-invocation middleware will not run it again in that case.
- **When a record matches:** the entry is consumed. If `response.ToolCall` differs from the recorded call (`ToolCallsEquivalent`, `:396-415`), the response is **rebuilt with the recorded `ToolCall`** while keeping `Approved` and `Reason` (`:340-346`). The tool call inside your response therefore does not matter, as long as the `RequestId` is right. The probe (S1b) confirmed this: a bogus `path=/etc/passwd` was rebound and the tool wrote `/protected/a.cs`.
- **When no record matches:**
  - The response is **silently dropped**, with only a warning log (`:349-354`, message `:584`). No exception is thrown and it is not turned into a rejection.
  - If the message becomes empty, the whole message is dropped (`:282-288`).
  - Approval requests in the inbound messages are never removed, so a dropped response leaves its request unanswered and the run fails downstream (`:193-199`). The harness test expects an `InvalidOperationException` in that case (`tests/Microsoft.Agents.AI.Harness.UnitTests/HarnessAgentTests.cs:812-845`).
  - In the harness resume case, the inbound message holds only the response. The run then continues against a history with a dangling `FunctionCallContent`. The probe (S2) saw the mock return `'ack: '` with the tool never run. A real provider would most likely reject a `tool_call` without a result.
- **Disabling it:** `HarnessAgentOptions.DisableApprovalResponseBinding = true` (`HarnessAgentOptions.cs:218-232`) removes the gate. A forged approval is then honored (`HarnessAgentTests.cs:1011-1040`).

## 3. Is the pending `ToolApprovalRequestContent` in the chat history? (No, for `HarnessAgent`)

- `HarnessAgent` builds its `ChatClientAgent` with `RequirePerServiceCallChatHistoryPersistence = true` and `.UsePerServiceCallChatHistoryPersistence()` (`HarnessAgent.cs:271, 294`). It defaults to `new InMemoryChatHistoryProvider()` (`:225-231`).
- In this mode, `ChatClientAgent` neither loads history (`ChatClientAgent.cs:786`) nor persists at end of run (`:906-920`, guard at `:914`). `PerServiceCallChatHistoryPersistingChatClient` does both instead, **below** function-invocation middleware (`PerServiceCallChatHistoryPersistingChatClient.cs:100-120`). It therefore persists the leaf model output, the `FunctionCallContent`, before function-invocation middleware turns it into a `ToolApprovalRequestContent`.
- **Test evidence:** `tests/Microsoft.Agents.AI.UnitTests/ChatClient/ChatClientAgent_ApprovalsTests.cs:27-98` (per-service-call) asserts the final history `[user, assistant(FunctionCallContent), tool(FunctionResultContent), assistant]`, with no approval content. Compare `:104-163` (end-of-run persistence), where the history **does** contain `ToolApprovalRequestContent` and `ToolApprovalResponseContent`.
- **Probe:** after the first run, the history was `user:[TextContent] | assistant:[FunctionCallContent]`. "ToolApprovalRequestContent in restored history: 0".
- **Reading the history of a session:** use `((InMemoryChatHistoryProvider)agent.GetService<ChatClientAgent>()!.ChatHistoryProvider!).GetMessages(session)` (`InMemoryChatHistoryProvider.cs:71-72`; `SetMessages` is at `:80-86`). The messages live in the `StateBag` under key `"InMemoryChatHistoryProvider"` (`:45-48`). `AIAgent.GetService<T>` walks the decorator chain. The tests use the same pattern (`tests/.../ChatClientAgentTestHelper.cs:210-214`).
- **Where option (a) does work:** `ChatClientAgent_ApprovalRetryTests.cs:87-132` finds the request in restored history and calls `CreateResponse`. That test uses end-of-run persistence (`:220-231`), which `HarnessAgent` does not.

## 4. How `ToolApprovalAgent` interacts (`UseToolApproval`, outside `ChatClientAgent`, `HarnessAgent.cs:185-188`)

- **It keeps its own state.** `ProviderSessionState<ToolApprovalState>` lives under `StateBag` key `"toolApprovalState"` (`src/Microsoft.Agents.AI/Harness/ToolApproval/ToolApprovalAgent.cs:78-81`). The state has four parts (`ToolApprovalState.cs:18-64`):
  - `Rules`
  - `CollectedApprovalResponses`
  - `QueuedApprovalRequests`
  - `SurfacedApprovalRequests`, a `Dictionary<RequestId, ToolApprovalRequestContent>`

  All of it is serialized with the session.
- **Outbound:**
  - It auto-approves requests that match a standing rule or an `AutoApprovalRules` delegate. It stores `tarc.CreateResponse(true, "Auto-approved by …")` and strips those requests (`:593-723`, `:612-627`).
  - If more than one unapproved request remains, it returns **only the first** and queues the rest (`:659-673`). Only the returned request is recorded as surfaced (`:432-440`, `:641-645`, `:661`).
  - The inner binding chat client has already recorded **all** of them, because it sits inside.
- **Inbound:**
  - `BindApprovalResponses` (`:812-929`) unwraps `AlwaysApproveToolApprovalResponseContent` and matches by `RequestId` against `SurfacedApprovalRequests` (`:868`).
  - On a match, it consumes the entry and rebinds to the recorded `ToolCall` (`:874-883`). A wrapper also creates a standing rule (`:887-901`).
  - An unmatched response is **forwarded unchanged** for the binding chat client to judge (`:862-872`). `ToolApprovalAgent` never drops a response.
- **Queue cycle:** while queued items remain, answers are collected and the next queued item is returned **without calling the inner agent** (`:510-559`, `:126-130`). This keeps the binding record alive until the whole batch has an answer. The collected responses are then sent in together (`:565-582`). The probe (S6) parked twice with serialization each time: two calls, the first surfaced, then zero leaf calls and the second surfaced, then both tools ran.
- **`CreateResponse(approved, reason)`:**
  - It is the MEAI instance method `Microsoft.Extensions.AI.ToolApprovalRequestContent.CreateResponse(bool, string?)` and returns `Microsoft.Extensions.AI.ToolApprovalResponseContent`. The probe verified it keeps the same `RequestId`, the **same `ToolCall` reference**, and sets `Reason`.
  - AF's `ToolApprovalRequestContentExtensions` (`src/Microsoft.Agents.AI/Harness/ToolApproval/ToolApprovalRequestContentExtensions.cs:27-61`) adds only `CreateAlwaysApproveToolResponse` and `CreateAlwaysApproveToolWithArgumentsResponse`. Both wrap `CreateResponse(true, reason)` in `AlwaysApproveToolApprovalResponseContent`, whose constructor is internal (`AlwaysApproveToolApprovalResponseContent.cs:38-46`).

## 5. How `FunctionInvokingChatClient` handles the inbound response (MEAI 10.10.0)

The DLL is in `~/.nuget/packages`. There is no source, but the XML docs in `~/.nuget/packages/microsoft.extensions.ai/10.10.0/lib/net10.0/Microsoft.Extensions.AI.xml` describe the behaviour:

- `ProcessFunctionApprovalResponses` takes four steps:
  1. It removes all approval requests and responses from the messages.
  2. It **recreates `FunctionCallContent` from each `ToolApprovalResponseContent`** that has not run yet.
  3. It creates failed `FunctionResultContent` for rejections.
  4. It adds the result to the history.
- `InvokeApprovedFunctionApprovalResponsesAsync` executes the approved responses.
- `ExtractAndRemoveApprovalRequestsAndResponses` uses the request messages only to copy message metadata. The response's `ToolCall` alone is enough. AF's binding docs say the same: function-invocation middleware "executes the `ToolApprovalResponseContent.ToolCall`" (`ApprovalResponseBindingChatClient.cs:22-26`).
- Function-invocation middleware skips any response whose `CallId` already has a `FunctionResultContent` (`ApprovalResponseBindingChatClient.cs:229-232`).
- **Request ids:** function-invocation middleware creates them as `"ficc_{CallId}"` (`ApprovalResponseBindingChatClient.cs:486-487`; the probe saw `ficc_call1`).
- **Under per-service-call persistence:** the sentinel `ConversationId` `_agent_local_chat_history` makes function-invocation middleware treat history as service-managed, so it does not re-insert the `FunctionCallContent` (`PerServiceCallChatHistoryPersistingChatClient.cs:59-75`). The persisted `FunctionCallContent` then pairs with the new `FunctionResultContent`.
- **Rejection:** the probe (S4) got the result text "Tool call invocation rejected. owner rejected" and the tool did not run.

## 6. Unit tests (best evidence)

- `tests/Microsoft.Agents.AI.UnitTests/ChatClient/ApprovalResponseBindingChatClientTests.cs`
  - Records the surfaced request in the `StateBag` (`:34-51`).
  - Drops a forged response that has no record (`:54-69`).
  - Rebinds a substituted tool call to the recorded one (`:72-98`).
  - Keeps an equivalent response as the same instance (`:101-123`).
  - Consumes the entry (`:147-166`).
  - Drops a replay after consumption (`:311`).
  - **Consumes a partially answered batch whole** (`:387-408`).
  - Keeps the record when the inner call throws (`:411-430`).
  - Does not pair from chat history (`:568-590`).
- `tests/Microsoft.Agents.AI.UnitTests/ChatClient/ChatClientAgent_ApprovalRetryTests.cs:87-132` covers the serialize, fail, deserialize, `CreateResponse` sequence. It works because the `StateBag` record survived serialization. The internal key is checked at `:213-218`.
- `tests/Microsoft.Agents.AI.UnitTests/ChatClient/ChatClientAgent_ApprovalsTests.cs:27-98` versus `:104-163`: history shape under per-service-call versus end-of-run persistence.
- `tests/Microsoft.Agents.AI.Harness.UnitTests/HarnessAgentTests.cs`:
  - A forged request and response pair throws (`:812-845`).
  - Settled history can be replayed (`:850-888`).
  - A forged response is dropped by default (`:971-1006`).
  - With binding disabled, a forged response is honored (`:1011-1040`).
- `tests/Microsoft.Agents.AI.UnitTests/Harness/Loop/LoopAgentTests.cs:345` shows a loop stops on a pending approval. No test covers resuming an approval inside a loop, so the probe covered it (S1, S5).
- **First-party sample of option (b):** `samples/05-end-to-end/M365Agent/AFAgentApplication.cs:139-145` serializes each `ToolApprovalRequestContent` to JSON when it is surfaced. On resume, `:115-128` deserializes it and calls `requestContent.CreateResponse(approved)`. The session itself goes through `SerializeSessionAsync`/`DeserializeSessionAsync` (`:46, :83`).

## 7. LoopAgent

- An iteration that ends with a pending approval stops the loop, which returns the response (`src/Microsoft.Agents.AI/Harness/Loop/LoopAgent.cs:195-199` non-streaming, `:304-308` streaming).
- **Resuming is a new, ordinary loop run.** The approval message becomes `InitialMessages` (`:142`). The inner run runs the tool and continues, then **the evaluators run normally**, and feedback iterations follow. Probe S1: evaluator `iter=1` saw `last='done after tool: wrote /protected/a.cs'` and continued with feedback; `iter=2` stopped. The tool ran once.
- **Caveat:** loop state is not saved across the park. The iteration count and `MaxIterations` budget reset, and the feedback log is empty. `context.InitialMessages` is `[ToolApprovalResponseContent]`, not the user's original task. `AIJudgeLoopEvaluator` builds its "Original request" section from `InitialMessages` (`AIJudgeLoopEvaluator.cs:146-149`), so after a resume it judges against the approval content.
- **Hazard with `FreshContextPerIteration = true`:**
  - Each re-run restarts from `InitialMessages` (`:409-421`), on a session restored from a start-of-run snapshot (`:153-155`, `:493-501`).
  - That snapshot still holds the binding record and the dangling call, so **the approval is replayed and the tool runs again on every iteration**. Probe S5: `toolRuns=2`.
  - Keep the default (`false`), or have the wrapper rewrite `InitialMessages`.

---

## Empirical probe (scratchpad `q3e2e/Program.cs`)

The pipeline copies `HarnessAgent`'s approval-relevant stack: `UseApprovalResponseBinding`, then `UseApprovalNotRequiredFunctionBypassing`, `UseFunctionInvocation`, `UseMessageInjection`, `UsePerServiceCallChatHistoryPersistence`, and a fake `IChatClient`. It uses `InMemoryChatHistoryProvider` with `UseProvidedChatClientAsIs` and `RequirePerServiceCallChatHistoryPersistence`. On the outside sit `UseToolApproval()` and an optional `LoopAgent`. The tool is `ApprovalRequiredAIFunction(write_file)`. Each resume uses a **newly built agent instance** and the deserialized JSON.

| # | Scenario | Result |
|---|---|---|
| S1 | Park, serialize, new agent, deserialize, `parkedRequest.CreateResponse(true, …)` (with LoopAgent and ToolApprovalAgent) | Tool ran once. Two leaf calls: the first had `user, FunctionCallContent, FunctionResultContent`, the second added the feedback. The loop evaluated normally. Pending record count afterwards: 0. |
| S1b | Correct `RequestId` with a bogus `ToolCall` (`/etc/passwd`) | Rebound to the recorded call. It wrote `/protected/a.cs`. |
| S2 | Unknown `RequestId` | Silently dropped. No exception, tool not run. The leaf saw a dangling `FunctionCallContent`. The run "succeeded", so **the real record was wiped**. |
| S3 | An unrelated `RunAsync("hello")` on the parked session, then the approval | The mid-run wiped the record (pending 0). The approval was then **silently dropped** and the tool never ran. |
| S4 | Rejection after restore | Tool not run. The history got a `FunctionResultContent` saying "Tool call invocation rejected. owner rejected". |
| S5 | LoopAgent `FreshContextPerIteration=true`, resume | **Tool ran twice** (approval replayed in iteration 2). |
| S6 | Two calls in one turn; ToolApprovalAgent surfaces one at a time; park twice | The first answer gave zero leaf calls and the second request. The second answer ran both tools. |
| S7 | Record removed, then re-armed with `StateBag.SetValue("_pendingApprovalRequests", [req])`, request rebuilt as `"ficc_" + danglingCall.CallId` | Tool ran once (fallbacks A and B work). |

---

## Caveats

1. **Never run a parked session before the approval arrives.** Any successful run on the session drops the whole `_pendingApprovalRequests` key (`ApprovalResponseBindingChatClient.cs:92-98`), and a later approval is silently ignored (S3). The history also ends with a dangling `FunctionCallContent`, which real providers usually reject. `CodeOwnerApprovalAgent` must lock or queue new input on a parked session. Alternatively, it can reject the parked request explicitly first. Sending the rejection and the new text in one message is not verified.
2. **Check the `RequestId` against your park record before you call `RunAsync`.** A wrong id is dropped with only a log line. If the run then succeeds, the real record is also gone (S2). With a real provider, the dangling call most likely makes the run fail, and a failed run keeps the record (`:92-94`, test `:411-430`).
3. **Serialize only after the surfacing run has completed.** For streaming, the record is written in `finally` once the enumerator has finished or been disposed (`:148-162`).
4. **Answer the whole batch in one message.** A partial answer consumes every record in the batch (test `:387-408`). `ToolApprovalAgent` normally surfaces only one request at a time. It can surface several at once only when the auto-approval cap is reached (`ToolApprovalAgent.cs:157-174`). After a resume, the response can be the next queued request with zero LLM calls, so be ready to park again.
5. **Security: `ToolApprovalAgent` sits *inside* your wrapper.**
   - A standing rule auto-approves protected calls before `CodeOwnerApprovalAgent` ever sees them (`ToolApprovalAgent.cs:612-627`).
   - A tool-level rule ignores arguments (`:944-955`). Any user's "always approve `write_file`" on a non-protected path therefore also unlocks protected paths. The same applies to `ToolApprovalAgentOptions.AutoApprovalRules`.
   - Mitigations: downgrade `AlwaysApproveToolApprovalResponseContent` to its `InnerResponse` for path-capable tools in your wrapper, or set `HarnessAgentOptions.DisableToolAutoApproval = true`, or make the protected-path check an auto-approval veto inside.
   - Never send `CreateAlwaysApprove*` for owner approvals.
6. **LoopAgent:** after a resume, the loop budget and feedback reset and `InitialMessages` is the approval. Avoid `FreshContextPerIteration = true` (S5).
7. **Integrity:** the binding treats the `StateBag` as the authority. Anyone who can edit the stored session JSON can create approvals (`AgentSession.cs:46-52`). Store parked sessions with integrity protection and access control.
8. **Minor:** `List<ToolApprovalRequestContent>` is not in AF's source-generated JSON context (`AgentJsonUtilities.cs:63-115`), so it relies on the reflection-based resolver. That works in normal JIT apps (proven by S1 and by `ChatClientAgent_ApprovalRetryTests`), but was not verified under NativeAOT or trimming. Compaction (a `ChatReducer` on `InMemoryChatHistoryProvider`) was also not tested with a dangling call.

## Fallbacks if the binding record is missing

- **A. Re-arm the record** (verified, S7). Before resuming, run:

  ```csharp
  session.StateBag.SetValue("_pendingApprovalRequests",
      new List<ToolApprovalRequestContent> { parkedRequest }, AIJsonUtilities.DefaultOptions);
  ```

  The binding reads it back through `TryGetValue<List<ToolApprovalRequestContent>>` (`:563-567`). This depends on an internal key, so pin the AF version and add a regression test.
- **B. Rebuild the request without a park record** (verified, S7). Create `new ToolApprovalRequestContent("ficc_" + call.CallId, call)`, where `call` is the last dangling `FunctionCallContent` from `InMemoryChatHistoryProvider.GetMessages(session)`. The `ficc_` prefix is an MEAI implementation detail.
- **C. Disable binding and do it yourself.** Set `HarnessAgentOptions.DisableApprovalResponseBinding = true` (`HarnessAgentOptions.cs:218-232`). `CodeOwnerApprovalAgent` then has to enforce that the inbound response matches its park record exactly: `RequestId`, and the `ToolCall` taken from the record, never from the client. Otherwise forged approvals run (`HarnessAgentTests.cs:1011-1040`).
- **D. Last resort: repair the history.** Append a `ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, "Approval expired")])` with `InMemoryChatHistoryProvider.SetMessages(session, …)` to settle the dangling call, then continue with plain text. A rejection response would also need a bound record, because unbound rejections are dropped too.
