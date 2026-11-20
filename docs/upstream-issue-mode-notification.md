# Draft issue for microsoft/agent-framework (not filed yet)

> This is a draft for David to review and file. Nothing has been posted.

**Title:** .NET: AgentModeProvider's "[Mode changed …]" notification is blocked by Azure OpenAI content filtering (HTTP 400 content_filter)

**Version:** `Microsoft.Agents.AI` 1.24.0 (tag `dotnet-1.24.0`). Model: Azure AI Foundry `gpt-5.4` deployment with the default content filter.

## Summary

When the mode is changed from outside the agent, `AgentModeProvider.SetModeAsync(session, mode)` (with `disableNotification: false`, the default overload) injects this user message on the next run:

```
[Mode changed: The operating mode has been switched from "plan" to "execute". You must now adjust your behavior to match the "execute" mode.]
```

Azure OpenAI's content filter (Prompt Shields) rejects the request: `HTTP 400 (invalid_request_error: content_filter)`, "Parameter: prompt … filtered due to the prompt triggering Azure OpenAI's content management policy". In our tests this happened 7 times out of 7. The same request with `disableNotification: true` succeeds.

## Where it hits

- Any host that switches modes in code (headless jobs, hosted agents).
- The harness sample console (`Harness_Shared_Console`): `/mode execute` (`ModeCommandHandler`) and the "Approve and switch to execute mode" choice (`PlanningOutputObserver`). Both call the notifying overload, so the next turn fails with a 400.

## Repro

```csharp
var agent = chatClient.AsHarnessAgent(new HarnessAgentOptions { DisableWebSearch = true });
var session = await agent.CreateSessionAsync();
await agent.GetService<AgentModeProvider>()!.SetModeAsync(session, "execute");
await agent.RunAsync("Haz inventario de …", session); // ClientResultException: HTTP 400 content_filter
```

## Suggestions

- Reword the notification so it doesn't read like an instruction override (e.g. "Current mode: execute."). Or deliver it through the provider's instructions, which already state the current mode on every call.
- Make the notification text configurable in `AgentModeProviderOptions`.
- In the sample console, use `disableNotification: true`.
