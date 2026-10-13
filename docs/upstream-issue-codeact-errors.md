# Draft issue for microsoft/agent-framework (not filed yet)

> This is a draft for David to review and file. Nothing has been posted.

**Title:** .NET: with HarnessAgent, LocalCodeAct errors reach the model only as "Error: Function failed." (no traceback, no tool parameters)

**Version:** `Microsoft.Agents.AI.Harness` 1.24.0 and `Microsoft.Agents.AI.LocalCodeAct` (preview), tag `dotnet-1.24.0`. Model: Azure AI Foundry `gpt-5.4`.

## Summary

`LocalCodeActProvider` turns every failure of the generated code into an exception: AST validation (`CodeValidationException`), a Python exception (`InvalidOperationException` with the traceback from `runner.py`), or the timeout (`TimeoutException`). `HarnessAgent` builds its `FunctionInvokingChatClient` with the default `IncludeDetailedErrors = false`. `HarnessAgentOptions` has no option to change that. So the model gets back only:

```
Error: Function failed.
```

The model can't fix code when it can't see the error. In our runs gpt-5.4 retried `execute_code` once and then gave up on it. It fell back to direct file reads: 13 model calls and 474k input tokens, against 5 calls and 41k with detailed errors on.

A second issue makes failures more likely. `execute_code`'s description (`InstructionBuilder`) lists each tool's name and description but not its parameters. The model guessed keyword names and called `call_tool("list_source_files")` without its argument.

A third issue: `execute_code`'s description says it "returns … the value of a top-level `result` variable when set". `runner.py` doesn't do that. It wraps the code in `async def __local_codeact_main__()` and returns the value of the **last expression**. An assignment `result = …` is a local of that function, so it is never returned unless the block ends with a bare `result` line. gpt-5.4 lost a retry on this in most runs, and another on calling `asyncio.run(...)` inside the block, which fails because the code already runs in an event loop. Neither behavior is in the instructions.

## Repro

```csharp
using var codeAct = new LocalCodeActProvider(pythonPath, new LocalCodeActProviderOptions { Tools = tools });
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions { AIContextProviders = [codeAct] });
// Ask for something that makes the model write code calling a tool with a wrong keyword argument,
// or that raises (e.g. KeyError). The function result the model sees is "Error: Function failed.".
```

## Workaround

```csharp
agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true;
```

## Suggestions

- Return the failure as the result of `execute_code` (exception type, message and traceback), as `runner.py` already formats it, instead of throwing. Or expose `IncludeDetailedErrors` (or a FICC configure callback) in `HarnessAgentOptions`.
- Include each tool's parameter names and types in the `execute_code` description.
- Make the description match the runner: either return a top-level `result` (as documented) or say "the value of the last expression; top-level `await` is supported, do not call `asyncio.run`".
