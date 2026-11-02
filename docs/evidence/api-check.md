# API check against Microsoft Agent Framework `dotnet-1.24.0`

Source: the sparse clone at `.ref/af/dotnet`. Every path below is relative to that folder. Line numbers are 1-based and come from the clone.

Experimental ids come from `src/Shared/DiagnosticIds/DiagnosticsIds.cs`:
- `DiagnosticIds.Experiments.AgentsAIExperiments` = **`MAAI001`**. Every AF experimental API in this report uses it.
- `MEAI001` is the Microsoft.Extensions.AI experimental id. `OPENAI001` is the OpenAI SDK id. `MAAIANTHROPIC001` is the Anthropic beta id.
- `Microsoft.Agents.AI.Tools.Shell` and `Microsoft.Agents.AI.LocalCodeAct` have **no `[Experimental]` attributes**. Instead they ship as `preview` packages (`<VersionSuffix>preview</VersionSuffix>`). The same is true of `Microsoft.Agents.AI.Foundry.Hosting`, which also marks its public types `[Experimental(MAAI001)]`.

The `Package` column gives the `src/` project folder, which is also the NuGet package id.

Status key: **CONFIRMED** means the claim matches the source. **DRIFT** means the API exists but differs in a way that matters. **NOT FOUND** means it is absent.

---

## 1. Loop (`src/Microsoft.Agents.AI/Harness/Loop/`)

All loop types are in **`Microsoft.Agents.AI`** (namespace `Microsoft.Agents.AI`), not in `Microsoft.Agents.AI.Harness`. Every one is `[Experimental(MAAI001)]`.

| # | Claim | Status | Location | Exact signature | Exp. |
|---|---|---|---|---|---|
| 1 | `LoopEvaluator.EvaluateAsync` | CONFIRMED | `LoopEvaluator.cs:28,40` | `public abstract class LoopEvaluator` / `public abstract ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default);` | MAAI001 |
| 2 | `LoopContext` members | CONFIRMED (+2 extra members, Feedback is a list) | `LoopContext.cs:23` | see the member table below | MAAI001 |
| 3 | `LoopEvaluation` factories | CONFIRMED, but **no `Stop(reason)` overload** | `LoopEvaluation.cs:24,59,67,81` | `public static LoopEvaluation Stop()` / `public static LoopEvaluation Continue(string? feedback = null)` / `public static LoopEvaluation ContinueWithMessages(IEnumerable<ChatMessage> messages)` | MAAI001 |
| 4 | Evaluators run in order; the first that re-invokes wins | CONFIRMED | `LoopAgent.cs:333-377` | `EvaluateAndBuildNextAsync` (private) | MAAI001 |
| 5 | Stops on a pending `ToolApprovalRequestContent` | CONFIRMED | `LoopAgent.cs:196-199` (non-streaming), `:305-308` (streaming), `:464-478` | `private static bool HasPendingApprovalRequests(AgentResponse response)` | MAAI001 |
| 6 | `DelegateLoopEvaluator(Func<…>)` | CONFIRMED | `DelegateLoopEvaluator.cs:20,29` | `public DelegateLoopEvaluator(Func<LoopContext, CancellationToken, ValueTask<LoopEvaluation>> evaluate)` | MAAI001 |
| 7a | `TodoCompletionLoopEvaluator` + options | CONFIRMED | `TodoCompletionLoopEvaluator.cs:42,70`; `TodoCompletionLoopEvaluatorOptions.cs:13` | `public TodoCompletionLoopEvaluator(TodoCompletionLoopEvaluatorOptions? options = null)` | MAAI001 |
| 7b | `CompletionMarkerLoopEvaluator` | CONFIRMED | `CompletionMarkerLoopEvaluator.cs:23,55` | `public CompletionMarkerLoopEvaluator(string completionMarker, CompletionMarkerLoopEvaluatorOptions? options = null)` | MAAI001 |
| 7c | `AIJudgeLoopEvaluator` | CONFIRMED | `AIJudgeLoopEvaluator.cs:55,126` | `public AIJudgeLoopEvaluator(IChatClient judgeClient, AIJudgeLoopEvaluatorOptions? options = null)` | MAAI001 |
| 7d | (extra) `BackgroundTaskCompletionLoopEvaluator` | exists | `BackgroundTaskCompletionLoopEvaluator.cs:36,68` | `public BackgroundTaskCompletionLoopEvaluator(BackgroundTaskCompletionLoopEvaluatorOptions? options = null)` | MAAI001 |
| 7e | `LoopAgentOptions` | CONFIRMED | `LoopAgentOptions.cs:15` | see the member table below | MAAI001 |

### `LoopContext` public members (`LoopContext.cs`)

| Member | Type | Line | Notes |
|---|---|---|---|
| ctor | `LoopContext(AIAgent agent, AgentSession session, IReadOnlyList<ChatMessage> initialMessages, AgentResponse lastResponse, AgentRunOptions? runOptions = null)` | 37 | |
| `Agent` | `AIAgent` (get) | 52 | This is the **inner** agent wrapped by the loop. In a HarnessAgent that is the ToolApprovalAgent, which delegates on to the ChatClientAgent. |
| `Session` | `AgentSession` (get; internal set) | 62 | When `FreshContextPerIteration` is on, the loop swaps in a new session. |
| `InitialMessages` | `IReadOnlyList<ChatMessage>` | 65 | Not in the plan. |
| `RunOptions` | `AgentRunOptions?` | 68 | Not in the plan. |
| `Iteration` | `int` (get; internal set) | 71 | "Number of completed agent runs so far (1-based after the first run)". It is **1 on the first evaluation**, because it is incremented before evaluators run. |
| `LastResponse` | `AgentResponse` (get; internal set) | 74 | |
| `Feedback` | **`IReadOnlyList<string?>`** | 87 | A *log* with one entry per re-invoked iteration. An entry is null when that iteration had no feedback text. It is not a single string. |
| `AdditionalProperties` | `AdditionalPropertiesDictionary` | 96 | A per-run bag shared across evaluators, so evaluators can stay stateless. |

`LoopEvaluation` also exposes `bool ShouldReinvoke` (line 36) and `string? Feedback` (line 43). `Messages` (line 55) is **internal**. `Continue()` normalises null, empty or whitespace feedback to `null`.

### `LoopAgent` / `LoopAgentOptions`

- `public sealed class LoopAgent : DelegatingAIAgent` (`LoopAgent.cs:65`). `public const int DefaultMaxIterations = 10;` (`:68`).
- Constructors:
  - `LoopAgent(AIAgent innerAgent, LoopEvaluator evaluator, LoopAgentOptions? options = null, ILoggerFactory? loggerFactory = null)` (`:88`)
  - `LoopAgent(AIAgent innerAgent, IEnumerable<LoopEvaluator> evaluators, LoopAgentOptions? options = null, ILoggerFactory? loggerFactory = null)` (`:106`). It throws if the list is empty.

| `LoopAgentOptions` member | Type | Default | Line |
|---|---|---|---|
| `MaxIterations` | `int?` | null, which means 10 | 26 |
| `FreshContextPerIteration` | `bool` | false | 61 |
| `OnBehalfOfAuthorName` | `string?` | null | 75 |
| `ExcludeOnBehalfOfMessages` | `bool` | false | 89 |
| `NonStreamingReturnsLastResponseOnly` | `bool` | false | 102 |
| `SessionCreatedCallback` | `Func<AgentSession, CancellationToken, ValueTask>?` | null | 116 |

`HarnessAgentOptions.LoopEvaluators` (`IEnumerable<LoopEvaluator>?`, `src/Microsoft.Agents.AI.Harness/HarnessAgentOptions.cs:166`) and `HarnessAgentOptions.LoopAgentOptions` (`:176`) are both `[Experimental(MAAI001)]`. HarnessAgent adds the loop only when at least one evaluator is given, and adds it as the **outermost** decorator (`HarnessAgent.cs:176-183`). The resulting pipeline is HarnessAgent → LoopAgent → ToolApprovalAgent → OpenTelemetryAgent → ChatClientAgent.

### The exact `LoopAgent` algorithm (`RunCoreAsync`, `LoopAgent.cs:133-218`)

1. If the caller passes no session, the loop creates one with `InnerAgent.CreateSessionAsync` and calls `SessionCreatedCallback`.
2. If `FreshContextPerIteration` is on and the caller supplied the session, the loop snapshots it once with `InnerAgent.SerializeSessionAsync`.
3. Each iteration does the following:
   1. Run the inner agent with `RunAsync(currentMessages, activeSession, options, ct)`.
   2. Increment `iteration`. Accumulate usage.
   3. Append the "on-behalf-of" messages that drove this iteration, then this iteration's response messages, to `transcript`.
   4. Create the `LoopContext` on the first iteration only, then update `Iteration` and `LastResponse`.
   5. If **any** response message contains a `ToolApprovalRequestContent`, **return immediately**. This check comes before the MaxIterations check and before any evaluator runs.
   6. If `iteration >= MaxIterations`, log an Info message and return.
   7. Call the evaluators in order. The first one with `ShouldReinvoke == true` wins and the rest are skipped. If none asks to re-invoke, return. An evaluator's `Stop()` means "I don't request continuation". It is not a veto.
   8. If `FreshContextPerIteration` is on, swap the session. A caller-supplied session is replaced by a deserialized clone of the snapshot; a loop-owned session is replaced by a new session.
   9. Append `winner.Feedback` to the feedback log. The entry may be null.
   10. Build the next input:
       - If the winner used `ContinueWithMessages(...)`, send those messages verbatim.
       - In the **default mode (session reused)**, send only the latest feedback as **`new ChatMessage(ChatRole.User, feedback) { AuthorName = OnBehalfOfAuthorName, MessageId = Guid }`**. If the feedback is null or whitespace, send **an empty message list**, so the agent continues from its own history (`LoopAgent.cs:424-432`).
       - In **FreshContextPerIteration mode**, send the `InitialMessages` again, followed by one user message: `"## Feedback\n\n- <entry1>\n- <entry2>…"`. This message aggregates every non-empty feedback entry so far (`:409-421`, `:438-452`).
- **Return value (non-streaming).** By default the result is the *last* `AgentResponse` with its `Messages` **replaced by the whole transcript**: the injected feedback messages followed by each iteration's response messages. The caller's original input is not echoed back. `Usage` is aggregated across all iterations. With `NonStreamingReturnsLastResponseOnly = true`, only the last response's messages are returned, still with aggregated usage (`:459-462`; `src/Shared/Usage/UsageAggregationExtensions.cs:60`). `ExcludeOnBehalfOfMessages` drops the injected feedback messages from what is surfaced.
- **Streaming (`:221-327`).** Every iteration's updates are yielded live. The injected feedback messages are yielded as `AgentResponseUpdate`s, lazily, just before the first update of the iteration they drive, and are stamped with that iteration's `ResponseId`. The approval check runs on the aggregated updates of the iteration, so the approval request has already been streamed out before the loop stops with `yield break`. The same `yield break` handles MaxIterations and the "all evaluators stopped" case.
- **Pending approval.** The loop never resolves approvals itself; it returns that response to the caller. In a HarnessAgent the ToolApprovalAgent sits *inside* the loop. It applies auto-approval rules first, re-invoking the inner agent up to `DefaultMaxAutoApprovalIterations = 40` times (`ToolApprovalAgent.cs:55`). Only unresolved approval requests reach LoopAgent, and they arrive **one at a time** because ToolApprovalAgent queues the rest. On the caller's next turn the approval response becomes `initialMessages` of a new loop run, and the iteration counter starts again from 0.

### Evaluator decision logic

- **TodoCompletionLoopEvaluator** (`:97-127`):
  - Resolves `context.Agent.GetService<TodoProvider>()` and throws `InvalidOperationException` if it is missing.
  - If `Modes` is set, resolves `GetService<AgentModeProvider>()` (also throws if missing) and returns `Stop()` unless `await GetModeAsync(context.Session)` is in `Modes` (ordinal match).
  - Calls `await todoProvider.GetRemainingTodosAsync(session)`. If nothing remains it returns `Stop()`; otherwise it returns `Continue(feedback)`.
  - Default feedback (`DefaultFeedbackMessageTemplate`, `:52`): `"You still have incomplete todo items. Continue working until every item is complete, marking each item as complete when finished. The following items are still open:\n{remaining_todos}"`. The placeholder is filled with lines of the form `- {Id}: {Title} — {Description}`.
  - Options (`TodoCompletionLoopEvaluatorOptions.cs`): `IEnumerable<string>? Modes` (`:24`) and `string? FeedbackMessageTemplate` (`:35`). Modes are on the **options** object, not on the evaluator. Passing an empty Modes list throws `ArgumentException`.
- **CompletionMarkerLoopEvaluator.** Returns `Stop()` if `LastResponse.Text.Contains(marker)`, otherwise `Continue(feedback)`. Placeholders are `{completion_marker}` and `{last_response}`. Options: `FeedbackMessageTemplate`.
- **AIJudgeLoopEvaluator.** Calls `judgeClient.GetResponseAsync<JudgeVerdict>(…)` with the system instructions, the initial messages and the last response text. `JudgeVerdict` has `bool Answered` and `string GapAnalysis`. If structured output fails, it falls back to text markers `VERDICT: DONE` / `VERDICT: MORE`. Options: `Instructions`, `IEnumerable<string>? Criteria`, `FeedbackMessageTemplate` (placeholder `{gap_analysis}`).
- **BackgroundTaskCompletionLoopEvaluator.** Requires `BackgroundAgentsProvider` and continues while background tasks are incomplete.

---

## 2. Context providers (`src/Microsoft.Agents.AI.Abstractions/`)

| # | Claim | Status | Location | Exact signature | Namespace | Package | Exp. |
|---|---|---|---|---|---|---|---|
| 8a | `ProvideAIContextAsync(InvokingContext, CancellationToken)` | CONFIRMED (**protected virtual**) | `AIContextProvider.cs:227` | `protected virtual ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)` | `Microsoft.Agents.AI` | Microsoft.Agents.AI.Abstractions | no |
| 8b | `AIContext { Instructions, Messages, Tools }` | CONFIRMED | `AIContext.cs:31,56,83,108` | `public sealed class AIContext { string? Instructions {get;set;} IEnumerable<ChatMessage>? Messages {get;set;} IEnumerable<AITool>? Tools {get;set;} }` | `Microsoft.Agents.AI` | Abstractions | no |
| 8c | `InvokingContext` | CONFIRMED (a **nested** type) | `AIContextProvider.cs:372-420` | `public sealed class AIContextProvider.InvokingContext`. Members: `AIAgent Agent`, `AgentSession? Session`, `AIContext AIContext`. The ctor `(AIAgent agent, AgentSession? session, AIContext aiContext)` is `[Experimental(MAAI001)]`. | `Microsoft.Agents.AI` | Abstractions | ctor only |
| 8d | `StateKeys` | CONFIRMED | `AIContextProvider.cs:91` | `public virtual IReadOnlyList<string> StateKeys => this._stateKeys ??= [this.GetType().Name];` | | | no |
| 8e | Per-session state helper | CONFIRMED (`ProviderSessionState<TState>`) | `ProviderSessionState{TState}.cs:26-87` | `public class ProviderSessionState<TState> where TState : class`. Ctor: `(Func<AgentSession?, TState> stateInitializer, string stateKey, JsonSerializerOptions? jsonSerializerOptions = null)`. Methods: `TState GetOrInitializeState(AgentSession? session)`, `void SaveState(AgentSession? session, TState state)`, `string StateKey`. | `Microsoft.Agents.AI` | Abstractions | no |

Other members of `AIContextProvider` (`AIContextProvider.cs:42`):
- `public ValueTask<AIContext> InvokingAsync(InvokingContext, CancellationToken = default)` (`:115`). This is the public entry point. It calls `protected virtual InvokingCoreAsync` (`:146`). The default `InvokingCoreAsync` filters the input messages to **External-only** before calling `ProvideAIContextAsync`, then merges the result into the incoming context: instructions are concatenated with `"\n"`, and messages and tools are appended. Messages a provider adds are tagged with source type `AIContextProvider`.
- `protected virtual ValueTask StoreAIContextAsync(InvokedContext, CancellationToken = default)` (`:331`). This is the post-run hook. It is skipped when the run threw.
- `public virtual object? GetService(Type serviceType, object? serviceKey = null)` (`:344`). It returns `this` when the requested type matches.
- The protected ctor (`:57`) accepts optional message filters: `provideInputMessageFilter`, `storeInputRequestMessageFilter`, `storeInputResponseMessageFilter`.

`ChatClientAgent` requires every provider's `StateKeys` to be unique (`ChatClientAgent.cs:156`, `ValidateAndCollectStateKeys`).

**Example pattern (TodoProvider, `src/Microsoft.Agents.AI/Harness/Todo/TodoProvider.cs`):**
```csharp
private readonly ProviderSessionState<TodoState> _sessionState;          // :66
public TodoProvider(TodoProviderOptions? options = null) {                // :78
    _sessionState = new ProviderSessionState<TodoState>(
        _ => new TodoState(), this.GetType().Name, AgentJsonUtilities.DefaultOptions);
}
public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey]; // :90
protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken ct = default) // :155
{   // returns Instructions + Tools (closures over context.Session) + optional synthetic user message "### Current todo list…"
    TodoState state = _sessionState.GetOrInitializeState(context.Session);
    ...
}
// tool body: var s = _sessionState.GetOrInitializeState(session); mutate; _sessionState.SaveState(session, s);
```
The state lives in `session.StateBag` under the key `"TodoProvider"` and is serialized with the session. TodoProvider guards access with a per-session `SemaphoreSlim`. Its tools are `todos_add(todos: [{title, description?}])`, `todos_complete(items: [{id, reason}])`, `todos_remove(ids)`, `todos_get_remaining()` and `todos_get_all()`.

---

## 3. Approvals (`src/Microsoft.Agents.AI/Harness/ToolApproval/`, package `Microsoft.Agents.AI`, namespace `Microsoft.Agents.AI`)

None of these types are experimental. The exception is `FileAccessProvider`, which is MAAI001 as a whole class.

| # | Claim | Status | Location | Exact signature | Exp. |
|---|---|---|---|---|---|
| 9a | `ToolApprovalAgentOptions.AutoApprovalRules` | CONFIRMED | `ToolApprovalAgentOptions.cs:48` | `public IEnumerable<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>>? AutoApprovalRules { get; set; }`. Also on the class: `JsonSerializerOptions? JsonSerializerOptions` (`:22`), `int? MaxAutoApprovalIterations` (`:68`, default 40). The class is not sealed (`public class`). | no |
| 9b | `ToolAutoApprovalRuleContext` | CONFIRMED | `ToolAutoApprovalRuleContext.cs:16-54` | Ctor: `(FunctionCallContent functionCallContent, AIAgent agent, AgentSession? session, IReadOnlyCollection<ChatMessage> requestMessages, AgentRunOptions? agentRunOptions)`. Props: `FunctionCallContent FunctionCallContent`, `AIAgent Agent`, `AgentSession? Session`, `IReadOnlyCollection<ChatMessage> RequestMessages`, `AgentRunOptions? RunOptions`. | no |
| 10a | `FileAccessProvider.ReadOnlyToolsAutoApprovalRule` | CONFIRMED | `FileAccess/FileAccessProvider.cs:206` | `public static Func<ToolAutoApprovalRuleContext, ValueTask<bool>> ReadOnlyToolsAutoApprovalRule { get; }`. It approves `file_access_read`, `file_access_read_lines`, `file_access_ls` and `file_access_grep`. | MAAI001 |
| 10b | `AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule` | CONFIRMED | `src/Microsoft.Agents.AI/Skills/AgentSkillsProvider.cs:108` | Same type. It approves `load_skill` and `read_skill_resource`. | no |
| 10c | `AgentSkillsProvider.AllToolsAutoApprovalRule` | CONFIRMED | `AgentSkillsProvider.cs:141` | Same type. It also approves `run_skill_script`. | no |
| 10d | (extra) `FileAccessProvider.AllToolsAutoApprovalRule` | exists | `FileAccessProvider.cs:244` | Same type. It approves all eight `file_access_*` tools. | MAAI001 |
| 10e | (extra) `ToolApprovalAgent.AllToolsAutoApprovalRule` | exists | `ToolApprovalAgent.cs:107` | Same type. It returns true for **every** tool. | no |
| 11a | `CreateResponse(approved, reason)` | CONFIRMED, but this is an **MEAI instance method**, not AF | MEAI `Microsoft.Extensions.AI.Abstractions` 10.10.1 | `ToolApprovalResponseContent ToolApprovalRequestContent.CreateResponse(bool approved, string? reason = null)` | not marked in AF usage |
| 11b | `CreateAlwaysApproveToolResponse` | CONFIRMED (AF extension) | `ToolApprovalRequestContentExtensions.cs:27` | `public static AlwaysApproveToolApprovalResponseContent CreateAlwaysApproveToolResponse(this ToolApprovalRequestContent request, string? reason = null)` | no |
| 11c | `CreateAlwaysApproveToolWithArgumentsResponse` | CONFIRMED (AF extension) | `ToolApprovalRequestContentExtensions.cs:51` | `public static AlwaysApproveToolApprovalResponseContent CreateAlwaysApproveToolWithArgumentsResponse(this ToolApprovalRequestContent request, string? reason = null)` | no |

- `AlwaysApproveToolApprovalResponseContent : AIContent` (`AlwaysApproveToolApprovalResponseContent.cs:26`) has an internal ctor and exposes `ToolApprovalResponseContent InnerResponse`, `bool AlwaysApproveTool` and `bool AlwaysApproveToolWithArguments`. ToolApprovalAgent unwraps it, records a `ToolApprovalRule` (`ToolName`, `IDictionary<string,string>? Arguments`) in the session StateBag, and forwards only `InnerResponse`.
- The **MEAI types** live in namespace `Microsoft.Extensions.AI` (NuGet `Microsoft.Extensions.AI.Abstractions` 10.10.1, as pinned in `Directory.Packages.props`). The members below were checked against the package XML docs in `~/.nuget/packages`:
  - `ToolApprovalRequestContent : InputRequestContent`. Ctor `(string requestId, ToolCallContent toolCall)`. Props: `string RequestId` (inherited from base), **`ToolCallContent ToolCall`** (there is no `FunctionCall` property; AF code does `request.ToolCall is FunctionCallContent fc`, see `ToolApprovalAgent.cs:458`), `bool RequiresConfirmation` (default true). Method: `CreateResponse(bool, string?)`.
  - `ToolApprovalResponseContent : InputResponseContent`. Ctor `(string requestId, bool approved, ToolCallContent toolCall)`. Props: `string RequestId`, `bool Approved`, `ToolCallContent ToolCall`, `string? Reason`.
  - The AF code that handles these types does **not** suppress `MEAI001` (for example LoopAgent and ToolApprovalAgent), so they are not experimental in MEAI 10.10.x.
- How ToolApprovalAgent behaves (`ToolApprovalAgent.cs:20-51`):
  1. It applies standing rules from earlier "always approve" responses.
  2. It runs `AutoApprovalRules` in order; the first rule that returns true approves the call.
  3. It surfaces any remaining requests **one at a time** and queues the others.
  4. When everything is resolved, it injects the collected responses and calls the inner agent again.
- **Approval-gated by default:** every FileAccessProvider tool, all three AgentSkillsProvider tools (including `load_skill`), `LocalShellExecutor.AsAIFunction` (default `requireApproval: true`), and LocalCodeAct `execute_code` when `ApprovalMode = AlwaysRequire` or when any registered tool is an `ApprovalRequiredAIFunction`.

---

## 4. File access tools (`src/Microsoft.Agents.AI/Harness/FileAccess/FileAccessProvider.cs`, package `Microsoft.Agents.AI`, `[Experimental(MAAI001)]`)

Ctor: `public FileAccessProvider(AgentFileStore fileStore, FileAccessProviderOptions? options = null)` (`:162`).

Argument names are the C# parameter names, which `AIFunctionFactory` keeps as-is (camelCase). The test suite `tests/Microsoft.Agents.AI.UnitTests/Harness/FileAccess/FileAccessProviderTests.cs` uses exactly these keys.

| # | Tool name (const) | Status | Args (JSON names) | Impl line | Approval group |
|---|---|---|---|---|---|
| 12 | `file_access_ls` (`LsToolName`, :95) | CONFIRMED | `directory?` (string, empty means root), `globPattern?` | 389 | read-only |
| 12 | `file_access_read` (`ReadFileToolName`, :86) | CONFIRMED | **`fileName`** | 310 | read-only |
| 12 | `file_access_read_lines` (`ReadLinesToolName`, :89) | CONFIRMED | **`fileName`**, `startLine` (int, 1-based), `endLine?` (int, inclusive) | 336 | read-only |
| 12 | `file_access_grep` (`GrepToolName`, :98) | CONFIRMED | `regexPattern` (case-insensitive), `globPattern?`, `directory?` | 480 | read-only |
| 12 | `file_access_write` (`WriteToolName`, :83) | CONFIRMED | **`fileName`**, `content`, `overwrite` (bool, default false) | 281 | write |
| 12 | `file_access_replace` (`ReplaceToolName`, :101) | CONFIRMED | **`fileName`**, `oldString`, `newString`, `replaceAll` (bool, default false) | 408 | write |
| 12 | `file_access_replace_lines` (`ReplaceLinesToolName`, :104) | CONFIRMED | **`fileName`**, `edits`: an array of `{ "line_number": int, "new_line": string, "expected_line"?: string }`. These are snake_case via `[JsonPropertyName]` in `FileStore/FileLineEdit.cs`. | 439 | write |
| 12 | `file_access_delete` (`DeleteFileToolName`, :92) | CONFIRMED | **`fileName`** | 364 | write |

The answer to the question is yes: **every** read, write, replace and delete tool takes `fileName`. `ls` and `grep` take `directory` instead.

Return strings include `File '{fileName}' written.`, `File '{fileName}' already exists. To replace it, write again with overwrite set to true.`, `File '{fileName}' not found.`, `File '{fileName}' deleted.`, `Replaced {n} occurrence(s) in '{fileName}'.` and `Replaced {n} line(s) in '{fileName}'.`

`FileAccessProviderOptions` (`FileAccessProviderOptions.cs`, MAAI001):

| Member | Type | Line |
|---|---|---|
| `Instructions` | `string?` | 21 |
| `DisableWriteTools` | `bool` | 33 |
| `DisableReadOnlyToolApproval` | `bool` | 46 |
| `DisableWriteToolApproval` | `bool` | 61 |

All tools require approval by default.

---

## 5. Sessions and modes

| # | Claim | Status | Location | Exact signature | Package | Exp. |
|---|---|---|---|---|---|---|
| 13a | `AIAgent.SerializeSessionAsync` | CONFIRMED (returns `ValueTask<JsonElement>`) | `src/Microsoft.Agents.AI.Abstractions/AIAgent.cs:187` | `public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)` | Abstractions | no |
| 13b | `AIAgent.DeserializeSessionAsync` | CONFIRMED | `AIAgent.cs:222` | `public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)` | Abstractions | no |
| 13c | `AIAgent.CurrentRunContext` | CONFIRMED (**static**, AsyncLocal) | `AIAgent.cs:102` | `public static AgentRunContext? CurrentRunContext { get; protected set; }` | Abstractions | no |
| 13d | `AgentRunContext` | — | `AgentRunContext.cs:10-41` | `public sealed class AgentRunContext`. Members: `AIAgent Agent`, `AgentSession? Session`, `IReadOnlyCollection<ChatMessage> RequestMessages`, `AgentRunOptions? RunOptions` | Abstractions | no |
| 14a | `AgentModeProvider.GetModeAsync` | CONFIRMED (async) | `src/Microsoft.Agents.AI/Harness/AgentMode/AgentModeProvider.cs:195` | `public async Task<string> GetModeAsync(AgentSession session, CancellationToken cancellationToken = default)` | Microsoft.Agents.AI | no |
| 14b | `AgentModeProvider.SetModeAsync` | CONFIRMED (two overloads) | `:220`, `:239` | `public Task SetModeAsync(AgentSession session, string mode, CancellationToken cancellationToken = default)` and `public async Task SetModeAsync(AgentSession session, string mode, bool disableNotification, CancellationToken cancellationToken = default)` | Microsoft.Agents.AI | no |
| 14c | `AgentModeProviderOptions` | CONFIRMED | `AgentModeProviderOptions.cs:12-89` | see below | Microsoft.Agents.AI | no |
| 14d | `GetService<AgentModeProvider>()` through HarnessAgent | CONFIRMED | `DelegatingAIAgent.cs:66`, `ChatClientAgent.cs:429-437` | see notes | | |
| 14e | Reading todos | CONFIRMED | `TodoProvider.cs:109,137` | `public async Task<IReadOnlyList<TodoItem>> GetAllTodosAsync(AgentSession session, CancellationToken cancellationToken = default)` / `public async Task<List<TodoItem>> GetRemainingTodosAsync(AgentSession session, CancellationToken cancellationToken = default)`. `TodoItem` has `int Id`, `string Title`, `string? Description`, `bool IsComplete`. | Microsoft.Agents.AI | no |

- **`AgentRunContext` semantics.** `AIAgent.RunAsync` sets `CurrentRunContext = new(this, session, …)` on every nested `RunAsync` (`AIAgent.cs:341`), so the value reflects the innermost running agent. `ChatClientAgent` replaces `Session` with the resolved, non-null session (`EnsureRunContextHasSession`). Inside a tool, `AIAgent.CurrentRunContext?.Session` is therefore the live session; `FileMemoryProvider` uses exactly this at `:182`.
- **`AgentModeProviderOptions` members:**
  - `string? Instructions`
  - `IReadOnlyList<AgentModeProviderOptions.AgentMode>? Modes`. `AgentMode` is a nested sealed class with ctor `(string name, string instructions)`.
  - `string? DefaultMode`
  - `bool DisableModeSetTool`
  - `bool DisableModeGetTool`

  When `Modes` is null, the default modes are **`"plan"` and `"execute"`**. The default mode is the first one in the list (`"plan"`). `SetModeAsync` with an unknown mode throws `ArgumentException("Invalid mode: …")`. Changing the mode from outside the agent (with `disableNotification = false`) injects a one-time user message `[Mode changed: The operating mode has been switched from "x" to "y". …]` on the next run. The tools are `mode_set(mode)` and `mode_get()`.
- **GetService chain.**
  - HarnessAgent, LoopAgent, ToolApprovalAgent and OpenTelemetryAgent are all `DelegatingAIAgent`s. `DelegatingAIAgent.GetService` returns `this` when the type matches and otherwise asks `InnerAgent`. None of these four override it.
  - `ChatClientAgent.GetService` (`:429`) checks `base`, then metadata, `IChatClient`, `ChatOptions` and `ChatClientAgentOptions`, then **`AIContextProviders.Select(p => p.GetService(type, key)).FirstOrDefault(not null)`**, then the ChatHistoryProvider, then the ChatClient.
  - So `harnessAgent.GetService<AgentModeProvider>()`, `GetService<TodoProvider>()`, `GetService<FileAccessProvider>()` and `GetService<AgentSkillsProvider>()` all work. TodoCompletionLoopEvaluator depends on this.
  - A non-null `serviceKey` is passed through, and providers return null for keyed lookups.

---

## 6. Shell (`src/Microsoft.Agents.AI.Tools.Shell/`, namespace `Microsoft.Agents.AI.Tools.Shell`, preview package, no `[Experimental]`)

### 15. `LocalShellExecutorOptions` (`LocalShellExecutorOptions.cs`) — CONFIRMED, plus `Environment`

| Member | Type | Default | Line | Notes |
|---|---|---|---|---|
| `Mode` | `ShellMode` (`Stateless`, `Persistent`) | **`Persistent`** | 22 | A persistent executor must not be shared across sessions or users. |
| `Shell` | `string?` | null | 29 | Fallback order: `AGENT_FRAMEWORK_SHELL` env var, then OS default (`/bin/bash`, then `/bin/sh` on mac/Linux; `pwsh`, `powershell`, `cmd` on Windows). Cannot be combined with `ShellArgv`. |
| `ShellArgv` | `IReadOnlyList<string>?` | null | 36 | |
| `WorkingDirectory` | `string?` | null (the process cwd) | 43 | |
| `ConfineWorkingDirectory` | `bool` | **true** | 51 | In persistent mode, every command is prefixed with `cd` back to the working directory. |
| **`Environment`** | `IReadOnlyDictionary<string, string?>?` | null | 57 | **Adds or overrides variables. A `null` value removes a variable.** It is applied *after* the CleanEnvironment stripping, so it can add variables on top of a clean environment. |
| `CleanEnvironment` | `bool` | **false** | 63 | When true, the environment is cleared except for an allow-list (below). |
| `Policy` | `ShellPolicy?` | null, which becomes an empty policy that allows any non-empty command | 73 | |
| `Timeout` | `TimeSpan?` | **null, meaning no timeout** | 80 | `DefaultTimeout` (30 s) is only a *recommended* value and is **not** applied automatically. |
| `MaxOutputBytes` | `int` | 65536 (64 KiB) per stream, head+tail truncation | 83 | |
| `AcknowledgeUnsafe` | `bool` | false | 90 | Required to call `AsAIFunction(requireApproval: false)`. |

**CleanEnvironment allow-list** (`EnvironmentSanitizer.cs:20-30`, case-insensitive lookup): `PATH`, `HOME`, `USER`, `USERNAME`, `USERPROFILE`, `SystemRoot`, `TEMP`, `TMP`. Both the stateless path (`LocalShellExecutor.RunStatelessAsync`) and the persistent path (`ShellSession.cs:181-201`) use this list. Under PowerShell, `PSDefaultParameterValues=Out-File:Encoding=utf8` is also forced.

### 16. `ShellPolicy` — CONFIRMED (all parameters optional)

- `ShellPolicy.cs:175`: `public ShellPolicy(IEnumerable<string>? denyList = null, IEnumerable<string>? allowList = null, Func<ShellRequest, ShellPolicyOutcome?>? custom = null)`. Patterns are compiled as regexes with `IgnoreCase` and a 1 s match timeout.
- `Evaluate(ShellRequest)` (`:201`) checks in this order:
  1. An empty or trimmed-empty command is denied with `"empty command"`.
  2. Each deny pattern is tried; a match is denied with `"matched deny pattern: {regex}"`. A deny regex that times out also denies.
  3. If an allow-list is set, a command with no match is denied with `"command does not match allow list"`.
  4. `custom(request)` runs next. A non-null outcome wins.
  5. Otherwise the command is allowed.
- `ShellRequest(string command, string? workingDirectory = null)` and `ShellPolicyOutcome(bool allowed, string? reason = null)` are structs. `ShellPolicyOutcome.Allow` and `ShellPolicyOutcome.Deny(reason)` are provided.
- **On denial**, `LocalShellExecutor.RunAsync` **throws `ShellCommandRejectedException`** with the message `"Command rejected by policy: {reason}"` (`LocalShellExecutor.cs:138`). For example: `Command rejected by policy: matched deny pattern: rm\s+-rf`. The **AIFunction produced by `AsAIFunction` catches that exception and returns the message string to the model** as the tool result (`:360`). The model sees text, and no exception reaches the agent. Commands that run return `ShellResult.FormatForModel()`: stdout, then `stderr: …`, then `[command timed out]` when applicable, then `exit_code: N`. A timeout gives exit code 124.

### 17. `LocalShellExecutor` / `ShellEnvironmentProvider` — CONFIRMED

| API | Location | Signature |
|---|---|---|
| `DefaultTimeout` | `LocalShellExecutor.cs:60` | `public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);` |
| ctors | `:79`, `:87` | `public LocalShellExecutor()` / `public LocalShellExecutor(LocalShellExecutorOptions options)` |
| `AsAIFunction` | `:339` (abstract on `ShellExecutor.cs:88`) | `public override AIFunction AsAIFunction(string name = "run_shell", string? description = null, bool requireApproval = true)`. The single tool argument is **`command`** (string). It throws `InvalidOperationException` when `requireApproval: false` is passed without `AcknowledgeUnsafe`. |
| other | `ShellExecutor.cs:49-91` | `ShellExecutor : IAsyncDisposable`. Members: `InitializeAsync`, `RunAsync(string command, CancellationToken)` returning `Task<ShellResult>`, `DisposeAsync`. `LocalShellExecutor.ResolvedShellBinary`. |
| `ShellEnvironmentProvider` | `ShellEnvironmentProvider.cs:59,71` | `public ShellEnvironmentProvider(ShellExecutor executor, ShellEnvironmentProviderOptions? options = null)`. It is an `AIContextProvider` that adds **instructions only** and no tools. It probes the shell version, the working directory and the versions of `git`, `dotnet`, `node`, `python` and `docker` on first use, then caches the result. Options: `ProbeTools`, `OverrideFamily`, `ProbeTimeout` (5 s), `InstructionsFormatter`. Also exposes `RefreshAsync()` and `CurrentSnapshot`. |

---

## 7. CodeAct (`src/Microsoft.Agents.AI.LocalCodeAct/`, namespace `Microsoft.Agents.AI.LocalCodeAct`, preview package, no `[Experimental]`)

| # | Claim | Status | Location | Exact signature / facts |
|---|---|---|---|---|
| 18a | ctor | CONFIRMED | `LocalCodeActProvider.cs:31,48` | `public sealed class LocalCodeActProvider : AIContextProvider, IDisposable` / `public LocalCodeActProvider(string pythonExecutablePath, LocalCodeActProviderOptions? options = null)`. **It is IDisposable** (`Dispose()` at `:230` only sets a flag). There is no default Python path. |
| 18b | options | CONFIRMED | `LocalCodeActProviderOptions.cs` | `ProcessExecutionLimits? ExecutionLimits` (:14), **`IEnumerable<AIFunction>? Tools`** (:19), `LocalCodeActApprovalMode ApprovalMode = NeverRequire` (:32; values `AlwaysRequire`, `NeverRequire`), `IEnumerable<FileMount>? FileMounts` (:37), `IReadOnlyDictionary<string,string>? Environment` (:50), `string? WorkingDirectory` (:56), `RunnerScriptPath` (:62), `ValidatorScriptPath` (:68), `bool ValidationDisabled` (:77), `AllowedImports`, `BlockedImports`, `AllowedBuiltins`, `BlockedBuiltins` (:83-101) |
| 18c | limits | CONFIRMED | `ProcessExecutionLimits.cs` | **`int TimeoutSeconds = 30`**, `ValidationTimeoutSeconds = 10`, `MaxStdoutBytes`, `MaxStderrBytes` and `MaxResultBytes` = 10 MiB, `MaxCapturedFileBytes = 1 MiB`, `MaxTotalCapturedFileBytes = 10 MiB` |
| 18d | tool name | CONFIRMED | `Internal/ExecuteCodeFunction.cs:17,46` | **`execute_code`** with a single argument **`code`** (string). It returns stdout, stderr and the value of a top-level `result` variable. **State does not persist between calls.** |
| 18e | host tool calls | CONFIRMED | `Resources/runner.py:162,166`; `Internal/InstructionBuilder.cs` | Python calls host tools with **`await call_tool("<name>", **kwargs)`**. Each tool whose name is a valid identifier is *also* injected as a global async function, so `await my_tool(x=1)` works. Only tools registered on the provider can be called. |

Further facts:
- The tool registry can change at runtime through `AddTools`, `GetTools`, `RemoveTools`, `ClearTools`, `AddFileMounts`, `GetFileMounts`, `RemoveFileMounts` and `ClearFileMounts`.
- `execute_code` requires approval when `ApprovalMode == AlwaysRequire`, **or** when any registered tool is an `ApprovalRequiredAIFunction`.
- The child Python process starts with a **cleared environment** that contains only `options.Environment`. On Windows it also gets `SYSTEMROOT`, `SYSTEMDRIVE`, `COMSPEC`, `PATHEXT`, `TEMP` and `TMP`.
- The README warns that this is **not a sandbox**.
- A standalone `LocalExecuteCodeFunction(string pythonExecutablePath, LocalCodeActProviderOptions? options = null) : AIFunction` also exists.

---

## 8. Skills (`src/Microsoft.Agents.AI/Skills/`, package `Microsoft.Agents.AI`, namespace `Microsoft.Agents.AI`, not experimental)

| # | Claim | Status | Location | Exact signature |
|---|---|---|---|---|
| 19a | `AgentFileSkillsSource` ctors | CONFIRMED | `File/AgentFileSkillsSource.cs:88,104` | `public AgentFileSkillsSource(string skillPath, AgentFileSkillScriptRunner? scriptRunner = null, AgentFileSkillsSourceOptions? options = null, ILoggerFactory? loggerFactory = null)` / `public AgentFileSkillsSource(IEnumerable<string> skillPaths, AgentFileSkillScriptRunner? scriptRunner = null, AgentFileSkillsSourceOptions? options = null, ILoggerFactory? loggerFactory = null)` |
| 19b | script runner delegate | CONFIRMED | `File/AgentFileSkillScriptRunner.cs:24` | `public delegate Task<object?> AgentFileSkillScriptRunner(AgentFileSkill skill, AgentFileSkillScript script, JsonElement? arguments, IServiceProvider? serviceProvider, CancellationToken cancellationToken);`. Use `AgentFileSkillScript.FullPath` to locate the script. |
| 19c | `AgentSkillsProviderBuilder.UseFileSkills` | CONFIRMED (with a caveat) | `AgentSkillsProviderBuilder.cs:75` (and `UseFileSkill` at :60, `UseFileScriptRunner` at :189, `Build()` at :282) | `public AgentSkillsProviderBuilder UseFileSkills(IEnumerable<string> skillPaths, AgentFileSkillsSourceOptions? options = null, AgentFileSkillScriptRunner? scriptRunner = null)`. **Caveat:** the builder *requires* a runner, passed either here or through `UseFileScriptRunner`. Without one, `Build()` throws `InvalidOperationException`. The direct `AgentFileSkillsSource` and `AgentSkillsProvider(string path, …)` ctors accept a null runner; in that case `run_skill_script` throws `InvalidOperationException` only when a script is actually run. |
| 19d | `AgentSkillsProvider` ctors | — | `AgentSkillsProvider.cs:183,203,225,237,265` | `(string skillPath, AgentFileSkillScriptRunner? scriptRunner = null, AgentFileSkillsSourceOptions? fileOptions = null, AgentSkillsProviderOptions? options = null, ILoggerFactory? loggerFactory = null)`. The same shape exists with `IEnumerable<string> skillPaths`. Also `(params AgentSkill[] skills)`, `(IEnumerable<AgentSkill> skills, AgentSkillsProviderOptions? options = null, ILoggerFactory? loggerFactory = null)`, and `(AgentSkillsSource source, AgentSkillsProviderOptions? options = null, ILoggerFactory? loggerFactory = null, bool ownsSource = false)` |
| 19e | tools | CONFIRMED | `AgentSkillsProvider.cs:55-61,318-341` | **`load_skill(skillName)`**, **`read_skill_resource(skillName, resourceName)`**, **`run_skill_script(skillName, scriptName, arguments?)`**. `arguments` is any JSON value; a file script's own schema is an array of strings. All three are exposed whenever at least one skill exists. **All three require approval by default.** |

- `AgentSkillsProviderOptions` has `SkillsInstructionPrompt` (must contain the skills placeholder), `IncludeDetailedErrors`, `DisableLoadSkillApproval`, `DisableReadSkillResourceApproval` and `DisableRunSkillScriptApproval`.
- `AgentFileSkillsSourceOptions` has `AllowedResourceExtensions` (default `.md .json .yaml .yml .csv .xml .txt`), `AllowedScriptExtensions` (default `.py .js .sh .ps1 .cs .csx`), `SearchDepth` (default 2), `ScriptFilter` and `ResourceFilter`.
- **HarnessAgent default:** `new AgentSkillsProvider(Directory.GetCurrentDirectory())` with **no script runner**, unless `HarnessAgentOptions.AgentSkillsSource` is set.

---

## 9. Hosting over Responses (`src/Microsoft.Agents.AI.Foundry.Hosting/`, namespace `Microsoft.Agents.AI.Foundry.Hosting`, preview, public types `[Experimental(MAAI001)]`)

| # | Claim | Status | Location | Exact signature |
|---|---|---|---|---|
| 20a | `AddFoundryResponses(agent, agentSessionStore?, configure?)` | CONFIRMED | `ServiceCollectionExtensions.cs:115` (class `FoundryHostingExtensions` at :35, MAAI001) | `public static IServiceCollection AddFoundryResponses(this IServiceCollection services, AIAgent agent, AgentSessionStore? agentSessionStore = null, Action<FoundryResponsesOptions>? configure = null)`. There is also an overload `AddFoundryResponses(this IServiceCollection services, Action<FoundryResponsesOptions>? configure = null)` (`:65`) that uses keyed `AIAgent`s. |
| 20b | `MapFoundryResponses()` | CONFIRMED | `:368` | `public static IEndpointRouteBuilder MapFoundryResponses(this IEndpointRouteBuilder endpoints, string prefix = "")`. It also maps **`GET /readiness`** if that route is missing. |
| 20c | `FoundryAgentSessionStore` | CONFIRMED | `FoundryAgentSessionStore.cs:52,86` | `public sealed class FoundryAgentSessionStore : AgentSessionStore` (MAAI001). Ctor: `(Uri? endpoint = null, TokenCredential? credential = null, string storeName = DefaultStoreName, int itemTtlSeconds = FoundryStateStore.DefaultItemTtlSeconds)`. `DefaultStoreName = "agent-framework/sessions"`. **It is the default store** when `agentSessionStore` is null (`CreateDefaultAgentSessionStore`, `:456`). Outside Foundry it falls back to the AgentServer SDK's local store under `~/.agentserver/state_stores`. Alternatives: `InMemoryAgentSessionStore` and `FileSystemAgentSessionStore` (both MAAI001). |
| 20d | `ToolApprovalIdMap` | exists, **internal** | `ToolApprovalIdMap.cs:25` | `internal static class ToolApprovalIdMap`. It maps the AF `RequestId` to a wire id `mcpr_<50 hex of SHA256>` and stores the mapping in the session StateBag under `"Microsoft.Agents.AI.Foundry.Hosting.ToolApprovalIdMap"`. |
| 20e | `x-agent-user-id` | CONFIRMED | `HostedSessionIsolationKeyProvider.cs`, `PlatformHostedSessionIsolationKeyProvider.cs:28-44`, `HostedSessionContext.cs`, `HostedSessionContextExtensions.cs:45` | The default (internal) provider reads `ResponseContext.PlatformContext.UserIdKey`, which comes from the header, into `HostedSessionContext(string userId)`. When hosted, a missing user id throws (HTTP 500). Locally it is tolerated. The extension point is `public abstract class HostedSessionIsolationKeyProvider { ValueTask<HostedSessionContext?> GetKeysAsync(ResponseContext, CreateResponse, CancellationToken) }`. Read it back with `public static HostedSessionContext? GetHostedContext(this AgentSession session)`. |
| 20f | `FoundryJsonCheckpointStore` | exists (public, MAAI001) | `FoundryJsonCheckpointStore.cs:58,112,142` | `public sealed class FoundryJsonCheckpointStore : JsonCheckpointStore`. Ctors: `(Uri? endpoint = null, TokenCredential? credential = null, string storeName = "agent-framework/checkpoints", int itemTtlSeconds = …, ILoggerFactory? loggerFactory = null)` and `(int maxIndexUpdateAttempts, …same…)`. Used for workflow checkpoints. |
| 20g | `FoundryStateStoreBinding` | exists, **internal** | `FoundryStateStoreBinding.cs:23` | `internal sealed class FoundryStateStoreBinding`. It lazily resolves `FoundryStateStore` once. It is not public API. |

**`FoundryResponsesOptions`** (`FoundryResponsesOptions.cs`, MAAI001):

| Member | Type | Default | Line |
|---|---|---|---|
| `AllowStoredOutputEnabled` | `bool` | **false** | 36 |
| `IncludeReasoningEncryptedContent` | `bool` | true | 52 |
| `ResilientBackground` | `bool` | false | 84 |
| `SteerableConversations` | `bool` | false | 98 |

**Approvals over Responses:**
- An AF `ToolApprovalRequestContent` wrapping a `FunctionCallContent` goes out as an **`mcp_approval_request`** output item (`OutputConverter.cs:243-289`).
- An incoming `mcp_approval_response { approval_request_id, approve }` becomes `ToolApprovalResponseContent(afRequestId, approve, functionCall)` (`InputConverter.cs:343-357`).
- The wire format carries **no reason and no "always approve"**, so `CreateAlwaysApprove*` cannot be expressed over this protocol.

**Sessions:** the session key comes from `conversation_id`, then the `previous_response_id` partition, then the new response id (`HostedConversationKey.cs`). The container session id is not used. The key is also partitioned per user.

**Port:** the listen port is forced to `PORT` (default 8088) **only** when `FOUNDRY_HOSTING_ENVIRONMENT` is set (`ServiceCollectionExtensions.cs:534-545`). Locally, set `ASPNETCORE_URLS`.

---

## 10. Hosting over Invocations (`samples/04-hosting/FoundryHostedAgents/invocations/Hosted-Invocations-EchoAgent`)

**CONFIRMED.** AF has **no Invocations adapter in `src/`**; nothing in `src` references `Azure.AI.AgentServer.Invocations`. The sample drives the agent by hand.

- `Program.cs`:
  ```csharp
  builder.Services.AddSingleton<EchoAIAgent>();
  builder.Services.AddInvocationsServer();                       // (this IServiceCollection, Action<InvocationsServerOptions>? configure = null)
  builder.Services.AddScoped<InvocationHandler, EchoInvocationHandler>();
  var app = builder.Build();
  app.MapGet("/readiness", () => Results.Ok());                  // Invocations SDK does NOT map /readiness; without it -> HTTP 424 session_not_ready
  app.MapInvocationsServer();                                    // (this IEndpointRouteBuilder, string prefix = "") -> POST /invocations, GET /invocations/{id}, POST /invocations/{id}/cancel
  ```
- `EchoInvocationHandler : InvocationHandler`:
  ```csharp
  public override async Task HandleAsync(HttpRequest request, HttpResponse response, InvocationContext context, CancellationToken cancellationToken)
  // reads body as text -> agent.RunAsync(input, cancellationToken: ct) -> response.WriteAsync(agentResponse.Text)
  ```
  Other virtual methods (all return 404 by default): `GetAsync(invocationId, …)`, `CancelAsync(invocationId, …)`, `GetOpenApiAsync`, `GetAsyncApiJsonAsync` and `GetAsyncApiYamlAsync`.
- **Package versions in the sample csproj:**
  - `Microsoft.Agents.AI.Abstractions` `1.22.0` (`AgentFrameworkVersion`)
  - **`Azure.AI.AgentServer.Invocations` `1.0.0-beta.7`**
  - `OpenTelemetry.Api` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` `1.18.0`
  - `DotNetEnv` `3.2.0`
  - Target `net10.0`, SDK `Microsoft.NET.Sdk.Web`.

  The repo's `Directory.Packages.props` pins `Azure.AI.AgentServer.Invocations` `1.0.0-beta.6`, `Core` `1.0.0-beta.29` and `Responses` `1.0.0-beta.8`.
- **`InvocationContext`** (namespace `Azure.AI.AgentServer.Invocations`; read from the package XML docs in `~/.nuget/packages/azure.ai.agentserver.invocations/1.0.0-beta.7`):

  | Member | Type | Source |
  |---|---|---|
  | `InvocationId` | `string` | `x-agent-invocation-id` header, or a new UUID |
  | **`SessionId`** | `string` | `agent_session_id` query parameter, then the `FOUNDRY_AGENT_SESSION_ID` env var, then a new UUID |
  | `ClientHeaders` | `IReadOnlyDictionary<string,string>` | `x-client-*` headers |
  | `QueryParameters` | `IReadOnlyDictionary<string,StringValues>` | all query parameters |
  | **`PlatformContext`** | `Azure.AI.AgentServer.Core.PlatformContext` | see below |

  `PlatformContext` has **`string? UserIdKey`**, taken from the `x-agent-user-id` header, and `string? CallId`, taken from `x-agent-foundry-call-id`. The user id is therefore `context.PlatformContext.UserIdKey`.
- **Port and readiness:** `.env.example` and `azure.yaml` set `ASPNETCORE_URLS=http://+:8088`. Readiness is `GET /readiness`, which the sample maps itself. The protocol is declared in `azure.yaml` as `invocations` `1.0.0`.

---

## 11. Foundry Responses `IChatClient` and server-side storage (affects compaction)

**Ways to build the client:**
- **Harness samples** (for example `samples/02-agents/Harness/Harness_Step01_Research/Program.cs:72-81`):
  ```csharp
  new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential(), new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
      .GetProjectOpenAIClient()          // Azure.AI.Extensions.OpenAI.ProjectOpenAIClient (NuGet Azure.AI.Extensions.OpenAI 3.0.0-beta.3)
      .GetResponsesClient()              // inherited OpenAI.OpenAIClient -> OpenAI.Responses.ResponsesClient
      .AsIChatClient(deploymentName)     // MEAI.OpenAI extension
      .AsHarnessAgent(new HarnessAgentOptions { ... });
  ```
- **Store-disabled variants**, both `[Experimental(MAAI001)]`:
  - `public static IChatClient AsIChatClientWithStoredOutputDisabled(this ResponsesClient responseClient, string? model = null, bool includeReasoningEncryptedContent = true)`. In `src/Microsoft.Agents.AI.OpenAI/Extensions/OpenAIResponseClientExtensions.cs:120`, namespace **`OpenAI.Responses`**, package `Microsoft.Agents.AI.OpenAI`.
  - `public static IChatClient AsIChatClientWithStoredOutputDisabled(this ProjectResponsesClient responseClient, string? deploymentName = null, bool includeReasoningEncryptedContent = true)`. In `src/Microsoft.Agents.AI.Foundry/ProjectResponsesClientExtensions.cs:35`, namespace **`Azure.AI.Extensions.OpenAI`**, package `Microsoft.Agents.AI.Foundry`. You get a `ProjectResponsesClient` from `GetProjectOpenAIClient().GetProjectResponsesClient()` or `GetProjectResponsesClientForModel(model)`.

  Both set `CreateResponseOptions.StoredOutputEnabled = false` and add `IncludedResponseProperty.ReasoningEncryptedContent`.
- **Ready-made agent:** `AIProjectClient.AsAIAgent(string model, string instructions, string? name = null, string? description = null, IList<AITool>? tools = null, Func<IChatClient,IChatClient>? clientFactory = null, ILoggerFactory? loggerFactory = null, IServiceProvider? services = null)` returns a `ChatClientAgent` (`src/Microsoft.Agents.AI.Foundry/AIProjectClientExtensions.cs:183`). The hosted Responses samples use it.

**Store is ON by default when you use plain `.AsIChatClient()`.** Nothing in AF sets `store` on that path, and AF's own code treats an unset value as stored:
- `StoredOutputProbeChatClient.cs:86`: `responseOptions.StoredOutputEnabled ?? true`.
- `AgentFrameworkResponseHandler.cs:668-671`: "when the backend is set to not store (store = false) the returned … ConversationId comes null. If … set it means that the storage setting was enabled".
- The integration test fixture `tests/OpenAIResponse.IntegrationTests/OpenAIResponseFixture.cs` reads history from the service through `session.ConversationId` when store is true, and from the `ChatHistoryProvider` when store is false.

The MEAI internals are not in the clone, so this conclusion is inferred from that AF code.

**What happens in the harness when store is on:**
1. The Responses service returns `ChatResponse.ConversationId` (the response id).
2. `PerServiceCallChatHistoryPersistingChatClient` (`ChatClient/PerServiceCallChatHistoryPersistingChatClient.cs:95-136`) sees the real id and calls `ChatClientAgent.UpdateSessionConversationId`.
3. Because HarnessAgent configures a `ChatHistoryProvider` and leaves `ClearOnChatHistoryProviderConflict` at its default of **true** (`ChatClientAgentOptions.cs:87`; HarnessAgent only turns Warn and Throw off), the agent **sets `ChatHistoryProvider = null`** for the whole instance (`ChatClientAgent.cs:866-869`) and stores `session.ConversationId`.
4. From then on, every service call is "service managed" (`isServiceManaged`). The client skips loading local history and sends only the new messages chained by `previous_response_id`.
5. **The result is that client-side compaction is not real in that configuration.** The `InMemoryChatHistoryProvider` reducer and the `CompactionProvider` never shape what the model sees; the service keeps its full chain. This is why AF's chat-reduction sample (`samples/02-agents/Agents/Agent_Step13_ChatReduction/Program.cs:8-10,22,31`) says "You must disable client side conversation storage" and uses `AsIChatClientWithStoredOutputDisabled`.
6. **To make compaction real, use `AsIChatClientWithStoredOutputDisabled(...)`,** or set `RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false }` in ChatOptions.

**Under Foundry Responses hosting, store is forced OFF for each run** unless `FoundryResponsesOptions.AllowStoredOutputEnabled = true`:
- `InputConverter.cs:119-213` wraps the agent's `RawRepresentationFactory` and sets `StoredOutputEnabled = false`.
- A readiness health check reports agents that would still store.
- At run time, a non-null `session.ConversationId` raises a 501 error (`HostedStoredOutputCompatibility`).
- When the agent has **no** ChatHistoryProvider, hosting injects a volatile provider filled from the platform's history. HarnessAgent always has one, so it keeps its own `InMemoryChatHistoryProvider`, which lives in the session StateBag and is persisted by the session store (`AgentFrameworkResponseHandler.cs:316-330`).

---

## Summary of DRIFT / NOT FOUND

No claim is NOT FOUND. Every API in the plan exists. These are the points where the source differs from the plan:

1. **LoopContext (#2).** `Feedback` is `IReadOnlyList<string?>`, a log with one entry per re-invocation, not a string. The type also has `InitialMessages` and `RunOptions`. `Iteration` is 1-based: it is 1 at the first evaluation.
2. **LoopEvaluation (#3).** There is no `Stop(reason)` overload, only `Stop()`. `Continue(string? feedback = null)`. `ContinueWithMessages(IEnumerable<ChatMessage> messages)`.
3. **AIContextProvider (#8).** `ProvideAIContextAsync` is `protected virtual`; the public entry point is `InvokingAsync`. `InvokingContext` is the nested type `AIContextProvider.InvokingContext` with `Agent`, `Session?` and `AIContext`, and its ctor is MAAI001. Input messages are filtered to External-only by default.
4. **Approval helpers (#11).** `CreateResponse(bool approved, string? reason = null)` is an MEAI *instance* method on `ToolApprovalRequestContent`. Only the two `CreateAlwaysApprove*` methods are AF extensions, and they return `AlwaysApproveToolApprovalResponseContent`. The property is `ToolCall` (a `ToolCallContent`, which you cast to `FunctionCallContent`). There is no `FunctionCall` property. `RequestId` comes from the `InputRequestContent` base.
5. **Sessions (#13).** Both `SerializeSessionAsync` and `DeserializeSessionAsync` take an optional `JsonSerializerOptions?` and a `CancellationToken`, and return `ValueTask`. `CurrentRunContext` is **static**, of type `AgentRunContext?`, with `Agent`, `Session?`, `RequestMessages` and `RunOptions`.
6. **AgentModeProvider (#14).** `GetModeAsync` returns `Task<string>`. `SetModeAsync` returns `Task` and has an extra overload with `bool disableNotification`. An invalid mode throws. The default modes are "plan" and "execute", and the default is "plan".
7. **Shell timeout (#15/#17).** `LocalShellExecutorOptions.Timeout` defaults to **null, meaning no timeout**. `DefaultTimeout` (30 s) is only a recommendation. The options also have an `Environment` dictionary.
8. **Shell policy (#16).** All three constructor parameters are optional. `custom` is `Func<ShellRequest, ShellPolicyOutcome?>`.
9. **ShellEnvironmentProvider (#17).** The parameter is `ShellExecutor executor`, with an optional `ShellEnvironmentProviderOptions`.
10. **Skills builder (#19).** `UseFileSkills` *requires* a script runner, either as an argument or through `UseFileScriptRunner`, or `Build()` throws. The direct ctors accept null.
11. **Hosting internals (#20).** `ToolApprovalIdMap` and `FoundryStateStoreBinding` are **internal**. `FoundryJsonCheckpointStore` is public (MAAI001).
12. **Package placement.** Loop, approval, todo, mode, file-access and skills types are in package **`Microsoft.Agents.AI`**. Only `HarnessAgent`, `HarnessAgentOptions` and `AsHarnessAgent` are in `Microsoft.Agents.AI.Harness`.

## Key behavioural facts

- **LoopAgent algorithm.**
  - After each inner run the order is: (1) check for a pending approval and return if there is one; (2) check `MaxIterations` (default **10**) and return if reached; (3) run the evaluators in order, where the first `ShouldReinvoke` wins and the loop stops only if every evaluator returns Stop.
  - The caller's input is sent once.
  - Non-streaming returns the last response with `Messages` replaced by the **full transcript** (injected feedback plus every iteration's output) and aggregated `Usage`. `NonStreamingReturnsLastResponseOnly` switches to the last response only.
  - Streaming yields every iteration live, including the injected feedback messages as updates.
- **Feedback delivery.**
  - Default mode (session reused): a single **`ChatRole.User` message containing the latest feedback text** (AuthorName = `OnBehalfOfAuthorName`). If there is no feedback, **no messages** are sent.
  - `FreshContextPerIteration`: the original input again, plus one user message `"## Feedback\n\n- …"` aggregating all feedback so far, on a fresh or restored session.
  - `ContinueWithMessages`: the given messages, verbatim.
- **Approval stop.** Any `ToolApprovalRequestContent` in the iteration's messages makes the loop return or `yield break` immediately; it never auto-resolves. In HarnessAgent, ToolApprovalAgent sits inside the loop: it auto-approves through rules (up to 40 re-invocations) and surfaces one unresolved request at a time.
- **Shell environment.** `CleanEnvironment` defaults to **false**, so the full parent environment is inherited. When true, only `PATH`, `HOME`, `USER`, `USERNAME`, `USERPROFILE`, `SystemRoot`, `TEMP` and `TMP` are kept. `Environment` (`IReadOnlyDictionary<string,string?>`) adds or overrides variables after cleaning, and a null value removes one.
- **Shell denial.** `RunAsync` throws `ShellCommandRejectedException("Command rejected by policy: {reason}")`. Reasons are `empty command`, `matched deny pattern: {regex}` and `command does not match allow list`, or a custom reason. The `run_shell` AIFunction catches the exception and **returns that string to the model**. Approval is required by default, and the tool argument is `command`.
- **File tool arguments.** Every read, read_lines, write, replace, replace_lines and delete tool uses **`fileName`**. `ls` takes `directory?` and `globPattern?`. `grep` takes `regexPattern`, `globPattern?` and `directory?`. `replace_lines` takes `edits: [{line_number, new_line, expected_line?}]`. All eight tools require approval by default.
- **Store and conversation default.** Plain `GetResponsesClient().AsIChatClient(model)` leaves `store` unset, so the service stores. The returned `ConversationId` flips the session to server-managed history, and with `ClearOnChatHistoryProviderConflict = true` the harness drops its `ChatHistoryProvider`. **Client-side compaction then has no effect.** Use `AsIChatClientWithStoredOutputDisabled(...)` (MAAI001) to keep history and compaction client-side. Foundry Responses hosting forces `store = false` per run by default (`AllowStoredOutputEnabled = false`).
