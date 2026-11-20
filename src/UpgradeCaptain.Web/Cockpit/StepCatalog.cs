namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Lo que hace un botón de la lista «Haz» de un paso.</summary>
public enum CockpitAction
{
    None,
    StartStep1,
    StartStep2,
    StartStep3Pre,
    StartStep3,
    ResetClean,
    ResetAfterFix,
    ModeExecute,
    Build,
    Test,
    GitLog,
    RevealEvaluator,
    RevealWiring,
    OpenOwner,
    HostedStatus,
    HostedWatch,
    OpenAspire,
    PackFeed,
    CheckAzure,
}

public sealed record PromptChip(Bi Label, Bi Text, Bi? Hint = null);

public sealed record SnippetRef(string Id, Bi Title, Bi Note);

public sealed record DoItem(string Id, Bi Text, CockpitAction Action = CockpitAction.None);

/// <summary>Una parte de un paso (el step-3 tiene tres: 3a, 3b y 3c).</summary>
public sealed record StepPart(
    string Id,
    Bi Title,
    int Minutes,
    Bi Goal,
    IReadOnlyList<Bi> Talk,
    IReadOnlyList<Bi> Lines,
    IReadOnlyList<DoItem> Do,
    IReadOnlyList<PromptChip> Prompts,
    AgentStage? Stage);

public sealed record StepContent(
    int Number,
    Bi Title,
    Bi Tagline,
    TimeSpan Start,
    int Minutes,
    Bi Goal,
    IReadOnlyList<StepPart> Parts,
    IReadOnlyList<SnippetRef> Snippets,
    IReadOnlyList<Bi> WatchFor,
    IReadOnlyList<Bi> Optional);

/// <summary>
/// El guion de la charla en los dos idiomas (docs/demo-guide.md, adaptado a la cabina: los terminales T1/T2/T3
/// son ahora el panel en directo, la ventana de María y el panel «Realidad»).
/// </summary>
public static class StepCatalog
{
    public static readonly TimeSpan TalkLength = TimeSpan.FromMinutes(50);

    public static IReadOnlyList<StepContent> Steps { get; } =
    [
        new StepContent(
            Number: 1,
            Title: new("Harness", "Harness"),
            Tagline: new("Una llamada, el harness entero", "One call, the whole harness"),
            Start: TimeSpan.FromMinutes(7),
            Minutes: 8,
            Goal: new(
                "Una llamada (`AsHarnessAgent`) trae el harness completo: tools, sesión persistida, compactación, telemetría, modo plan y un bucle que no para hasta terminar los todos.",
                "One call (`AsHarnessAgent`) brings the whole harness: tools, persisted session, compaction, telemetry, plan mode, and a loop that doesn't stop until the todos are done."),
            Parts:
            [
                new StepPart(
                    Id: "1",
                    Title: new("Planifica, edita el plan, ejecuta", "Plan, edit the plan, execute"),
                    Minutes: 8,
                    Goal: new("El plan es un contrato que puedes editar antes de que se ejecute nada.", "The plan is a contract you can edit before anything runs."),
                    Talk:
                    [
                        new("El diff es pequeño: `AsHarnessAgent` + cinco tools de solo lectura + la consola de los samples.", "The diff is small: `AsHarnessAgent` + five read-only tools + the samples' console."),
                        new("`DefaultMode = \"plan\"`: puede proponer, no actuar. En modo execute, `TodoCompletionLoopEvaluator` lo devuelve al trabajo si quedan todos abiertos. `MaxIterations = 10` es el tope de seguridad.",
                            "`DefaultMode = \"plan\"`: it can propose, not act. In execute mode, `TodoCompletionLoopEvaluator` sends it back to work while todos remain open. `MaxIterations = 10` is the safety cap."),
                        new("Lección de preparar la charla: el almacenamiento en el servidor está *apagado* (`AsIChatClientWithStoredOutputDisabled`); con `store` activado, la compactación del lado cliente no hace nada. La compactación va en cada llamada al modelo con `CompactingChatClient` porque la del bucle pierde mensajes en 1.24.0 (D-07).",
                            "Lesson from preparing the talk: server-side storage is *off* (`AsIChatClientWithStoredOutputDisabled`); with `store` on, client-side compaction does nothing. Compaction runs on every model call through `CompactingChatClient`, because the in-loop compaction drops messages in 1.24.0 (D-07)."),
                    ],
                    Lines:
                    [
                        new("El plan es un contrato que puedes editar antes de que se ejecute nada.", "The plan is a contract you can edit before anything runs."),
                    ],
                    Do:
                    [
                        new("1.start", new("Monta el agente del step-1 (modo plan).", "Start the step-1 agent (plan mode)."), CockpitAction.StartStep1),
                        new("1.prompt", new("Envía el prompt de inventario → varias tools en un turno (`list_projects`, `list_source_files`, `read_source`…) y luego todos y una pregunta. **30–110 s: habla por encima.**",
                            "Send the inventory prompt → several tool calls in one turn (`list_projects`, `list_source_files`, `read_source`…), then todos and a question. **30–110 s: talk over it.**")),
                        new("1.edit", new("Llega la tarjeta del plan: **no apruebes aún**. Responde con «El informe lo escribo yo, quítalo».", "The plan card arrives: **don't approve yet**. Answer with \"I'll write the report myself, remove it\".")),
                        new("1.todos", new("Mira el panel de todos: el plan editado. El modo sigue en `plan`.", "Look at the todos panel: the edited plan. The mode is still `plan`.")),
                        new("1.approve", new("Aprueba el plan corregido → modo `execute`: el bucle sigue hasta acabar los todos o 10 iteraciones.", "Approve the corrected plan → `execute` mode: the loop runs until the todos are done or 10 iterations.")),
                    ],
                    Prompts:
                    [
                        new(new("Inventario", "Inventory"),
                            new("Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido", "Prepare the Teide.Geo upgrade to 2.0, including the final report"),
                            new("Deja «con el informe final incluido»: sin ello no hay tarea de informe que quitar.", "Keep \"including the final report\": without it there's no report task to remove.")),
                        new(new("Editar el plan", "Edit the plan"),
                            new("El informe lo escribo yo, quítalo", "I'll write the report myself, remove it"),
                            new("Va en la respuesta libre de la tarjeta del plan, no como mensaje nuevo.", "It goes in the plan card's free answer, not as a new message.")),
                        new(new("¿Qué encontraste?", "What did you find?"), new("¿Qué encontraste?", "What did you find?")),
                    ],
                    Stage: AgentStage.Step1),
            ],
            Snippets:
            [
                new("s1-client", new("Cliente del modelo", "Model client"), new("La cadena del cliente: sin store, reparación tras cortes y compactación.", "The client chain: no store, repair after network cuts, compaction.")),
                new("s1-harness", new("AsHarnessAgent", "AsHarnessAgent"), new("La línea que trae el bucle, y sus opciones.", "The line that brings the loop, and its options.")),
                new("s1-tools", new("Las 5 tools", "The 5 tools"), new("Métodos C# con descripción: es todo lo que ve el modelo.", "C# methods with a description: that's all the model sees.")),
            ],
            WatchFor:
            [
                new("`❌ Stream error` (conexión cortada, 100 s): la sesión sigue siendo válida → «Sigue».", "`❌ Stream error` (connection reset, 100 s): the session is still valid → \"Continue\"."),
                new("Si pulsas Aprobar en la primera tarjeta, el plan se ejecuta tal cual: usa la respuesta libre.", "Pressing Approve on the first card runs the plan as is: use the free answer."),
            ],
            Optional:
            [
                new("Compactación: activa «contexto mini» (16k/8k), el prompt de inventario y luego «¿Y cuáles de esos usos están en tests?». En Aspire, el span `upgrade_captain.compaction` con `compacted = true`.",
                    "Compaction: turn on \"tiny context\" (16k/8k), send the inventory prompt, then \"And which of those usages are in tests?\". In Aspire, the `upgrade_captain.compaction` span with `compacted = true`."),
            ]),

        new StepContent(
            Number: 2,
            Title: new("Approve", "Approve"),
            Tagline: new("Actúa, pero con una persona en el bucle", "It acts, with a human in the loop"),
            Start: TimeSpan.FromMinutes(15),
            Minutes: 8,
            Goal: new(
                "El agente puede actuar (shell, ficheros), pero solo dentro del repo de la demo y con una persona en el bucle; el know-how vive en skills que carga solo cuando hace falta; todo sale en una traza.",
                "The agent can act (shell, file edits), but only inside the demo repo and with a human in the loop; know-how lives in skills it loads only when needed; everything shows up in one trace."),
            Parts:
            [
                new StepPart(
                    Id: "2",
                    Title: new("Shell, ficheros, skills y aprobaciones", "Shell, files, skills and approvals"),
                    Minutes: 8,
                    Goal: new("Leer no pregunta; escribir y ejecutar, sí. Y la política sigue mandando después del sí.", "Reading doesn't ask; writing and running do. And the policy still applies after the yes."),
                    Talk:
                    [
                        new("`run_shell` siempre pregunta (`requireApproval: true`). `file_access_*` está confinado al repo: leer no pregunta, escribir sí.", "`run_shell` always asks (`requireApproval: true`). `file_access_*` is confined to the repo: reading doesn't ask, writing does."),
                        new("Skills en ficheros: el modelo solo ve `name` y `description` hasta que llama a `load_skill`. El script de la skill es una app de un solo fichero de .NET 10.",
                            "Skills as files: the model only sees `name` and `description` until it calls `load_skill`. The skill's script is a .NET 10 single-file app."),
                        new("En Aspire: `invoke_agent → chat → execute_tool`, con los tokens. Aquí no hay código de OTel: está activo desde el step-1.", "In Aspire: `invoke_agent → chat → execute_tool`, with token attributes. No OTel code here: it's been on since step 1."),
                    ],
                    Lines:
                    [
                        new("La lista de denegación es un filtro de UX, no una frontera de seguridad. La aprobación humana es la puerta, y la política sigue mandando después del sí.",
                            "The deny list is a UX filter, not a security boundary. The human approval is the gate, and the policy still applies after the human says yes."),
                        new("Denegar no es un error: es información para el agente.", "Deny is not an error, it's information for the agent."),
                    ],
                    Do:
                    [
                        new("2.start", new("Monta el agente del step-2. Comprueba que las trazas van a Aspire (chip de arriba).", "Start the step-2 agent. Check traces go to Aspire (chip at the top)."), CockpitAction.StartStep2),
                        new("2.bump", new("«Sube Teide.Geo a 2.0.0. De momento solo la versión.» → carga `nuget-upgrade`. Si sale primero el plan, apruébalo. Luego `run_skill_script` (`bump_package.cs`) → **Aprobar**. Si después pide editar ficheros, **Denegar**: va más allá de la versión.",
                            "\"Upgrade Teide.Geo to 2.0.0. Just the version for now.\" → loads `nuget-upgrade`. If the plan comes first, approve it. Then `run_skill_script` (`bump_package.cs`) → **Approve**. If a file edit prompts afterwards, **Deny**: it's going beyond the version.")),
                        new("2.policy", new("«Limpia la salida de compilación de la API: ejecuta rm -rf …» → `run_shell` pregunta → **apruébalo** → **bloqueado**: `Command rejected by policy: matched deny pattern: \\brm\\s+-rf\\b`. La política manda después del sí.",
                            "\"Clean the API build output: run rm -rf …\" → `run_shell` asks → **approve it** → **blocked**: `Command rejected by policy: matched deny pattern: \\brm\\s+-rf\\b`. The policy applies after the yes.")),
                        new("2.push", new("Opcional: «Ejecuta git push origin main» → el modelo se niega solo (el playbook dice «nunca publiques»). Otra capa: el modelo, la aprobación y la política.",
                            "Optional: \"Run git push origin main\" → the model refuses on its own (the playbook says \"never publish\"). Another layer: the model, the approval and the policy.")),
                        new("2.aspire", new("Aspire: el árbol de la traza, con `load_skill` y `run_skill_script`.", "Aspire: the trace tree, with `load_skill` and `run_skill_script`."), CockpitAction.OpenAspire),
                        new("2.codeact", new("Diapositiva de CodeAct (1 min): pestaña de código «CodeAct».", "CodeAct slide (1 min): the \"CodeAct\" code tab.")),
                    ],
                    Prompts:
                    [
                        new(new("Solo la versión", "Just the version"),
                            new("Sube Teide.Geo a 2.0.0. De momento solo la versión.", "Upgrade Teide.Geo to 2.0.0. Just the version for now."),
                            new("Sin «de momento solo la versión» el agente sigue migrando el código.", "Without \"just the version for now\" the agent goes on to migrate the code.")),
                        new(new("rm -rf (bloqueado)", "rm -rf (blocked)"),
                            new("Limpia la salida de compilación de la API: ejecuta rm -rf src/Canarias.Tours.Api/bin src/Canarias.Tours.Api/obj", "Clean the API build output: run rm -rf src/Canarias.Tours.Api/bin src/Canarias.Tours.Api/obj"),
                            new("Aprueba el `run_shell`: lo bloquea la lista de denegación, no la persona.", "Approve the `run_shell`: the deny list blocks it, not the person.")),
                        new(new("git push", "git push"), new("Ejecuta git push origin main", "Run git push origin main"),
                            new("Con la skill cargada, el modelo suele negarse solo. Si lo intenta y lo apruebas, también lo bloquea la política.", "With the skill loaded the model usually refuses on its own. If it tries and you approve, the policy blocks it too.")),
                        new(new("README (aprobar)", "README (approve)"),
                            new("En el README, cambia \"excursiones por Tenerife\" por \"excursiones por Tenerife y La Palma\"", "In the README, change \"excursions around Tenerife\" to \"excursions around Tenerife and La Palma\"")),
                        new(new("README (denegar)", "README (deny)"),
                            new("Añade al final del README la línea \"Mantenido por el equipo de Plataforma.\"", "Add the line \"Maintained by the Platform team.\" at the end of the README")),
                    ],
                    Stage: AgentStage.Step2),
            ],
            Snippets:
            [
                new("s2-shell", new("Shell confinado", "Confined shell"), new("LocalShellExecutor: confinado, entorno limpio y lista de denegación.", "LocalShellExecutor: confined, clean environment, deny list.")),
                new("s2-agent", new("Capacidades", "Capabilities"), new("Ficheros, skills y reglas de aprobación automática.", "Files, skills and auto-approval rules.")),
                new("skill:nuget-upgrade", new("SKILL.md", "SKILL.md"), new("El playbook: el frontmatter es todo lo que ve el modelo hasta load_skill.", "The playbook: the frontmatter is all the model sees until load_skill.")),
                new("s2-runner", new("Scripts .cs", ".cs scripts"), new("DotnetScriptRunner: dotnet run script.cs, sin Python.", "DotnetScriptRunner: dotnet run script.cs, no Python.")),
                new("s2-codeact", new("CodeAct", "CodeAct"), new("Solo diapositiva: las 5 tools dentro de execute_code.", "Slide only: the 5 tools inside execute_code.")),
            ],
            WatchFor:
            [
                new("El bump no usa la skill → revisa que el prompt lleva «De momento solo la versión».", "The bump doesn't use the skill → check the prompt has \"Just the version for now\"."),
                new("No hay trazas en Aspire → falta `OTEL_EXPORTER_OTLP_ENDPOINT`: relanza con scripts/cockpit.sh con Aspire en marcha.", "No traces in Aspire → `OTEL_EXPORTER_OTLP_ENDPOINT` is missing: relaunch with scripts/cockpit.sh while Aspire is up."),
            ],
            Optional:
            [
                new("CodeAct: Directo = un abanico de spans `execute_tool read_source`; CodeAct = un solo `execute_tool execute_code`. «En un repo de 14 ficheros da igual; en uno de 2.000 es la diferencia entre llenar el contexto o no». No prometas menos tokens.",
                    "CodeAct: Direct = a fan of `execute_tool read_source` spans; CodeAct = one `execute_tool execute_code` span. \"In a 14-file repo it doesn't matter; in a 2,000-file one it's the difference between filling the context or not.\" Don't promise fewer tokens."),
                new("«LocalCodeActProvider ejecuta Python en este portátil: **no es un sandbox**. Hyperlight necesita KVM o WHP, así que en un Mac no.»", "\"LocalCodeActProvider runs Python on this laptop: **not a sandbox**. Hyperlight needs KVM or WHP, so not on a Mac.\""),
            ]),

        new StepContent(
            Number: 3,
            Title: new("Tu harness", "Your harness"),
            Tagline: new("Un verificador que el prompt no puede apagar", "A verifier the prompt can't switch off"),
            Start: TimeSpan.FromMinutes(23),
            Minutes: 14,
            Goal: new(
                "El corazón de la charla: un verificador a medida que obliga al harness a ejecutar los tests aunque el prompt diga lo contrario.",
                "The heart of the talk: a custom verifier that makes the harness run the tests even though the user's prompt says not to."),
            Parts:
            [
                new StepPart(
                    Id: "3a",
                    Title: new("El muro", "The wall"),
                    Minutes: 4,
                    Goal: new("Dónde deja de bastar el harness integrado: se fía del modelo para decir «terminado», y el modelo se fía del usuario.", "Where the built-in harness stops being enough: it takes the model's word for \"done\", and the model takes the user's."),
                    Talk:
                    [
                        new("El repo ya compila: bump + arreglos mecánicos aplicados. Pero Teide.Geo 2.0 cambió en silencio `Distance` para que devuelva metros en vez de km.", "The repo already compiles: bump + mechanical fixes applied. But Teide.Geo 2.0 silently changed `Distance` to return metres instead of km."),
                        new("El modelo no es malo. Le digo «no ejecutes los tests», obedece, escribe el informe y cierra, y *nada lo comprueba*. Hasta dice que no ha ejecutado los tests, y el harness lo acepta.",
                            "The model isn't bad. I tell it \"don't run the tests\", it obeys, writes the report and closes, and *nothing checks*. It even tells you it didn't run the tests, and the harness accepts that."),
                    ],
                    Lines: [],
                    Do:
                    [
                        new("3a.reset", new("Prepara el repo en `after-mechanical-fix` (incluye `dotnet build`: refresca `obj/`). En Realidad: «ya compila».", "Reset the repo to `after-mechanical-fix` (includes `dotnet build`: refreshes `obj/`). In Reality: \"it compiles\"."), CockpitAction.ResetAfterFix),
                        new("3a.start", new("Monta el agente del step-3-pre (todo menos el evaluador).", "Start the step-3-pre agent (everything but the evaluator)."), CockpitAction.StartStep3Pre),
                        new("3a.mode", new("Modo `execute`.", "`execute` mode."), CockpitAction.ModeExecute),
                        new("3a.prompt", new("Envía el prompt del muro → aprueba la escritura del informe → dice que ha terminado.", "Send the wall prompt → approve the report write → it says it's done.")),
                        new("3a.test", new("Realidad: `dotnet test` → **8 en rojo**: 30.663,76 € en vez de 33,66 € (metros cobrados como km). Para en ese número.", "Reality: `dotnet test` → **8 red**: €30,663.76 instead of €33.66 (metres priced as km). Pause on that number."), CockpitAction.Test),
                    ],
                    Prompts:
                    [
                        new(new("El muro", "The wall"),
                            new("Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.", "It already compiles. Close the upgrade: write UPGRADE_REPORT.md and that's it. Don't run the tests, CI will run them.")),
                    ],
                    Stage: AgentStage.Step3Pre),
                new StepPart(
                    Id: "3b",
                    Title: new("El evaluador", "The evaluator"),
                    Minutes: 4,
                    Goal: new("Arreglar el primer muro con ~30 líneas en un punto de extensión documentado. Después, las otras tres piezas.", "Fix the first wall with ~30 lines in a documented extension point. Then reveal the other three pieces."),
                    Talk:
                    [
                        new("Un `LoopEvaluator` que, en modo execute, ejecuta `verifier.VerifyAsync`: verde → `CommitGreenAsync` + `Stop()`; atascado → `RestoreLastGreenAsync` + `Stop()`; rojo → `Continue(result.ToFeedback())`.",
                            "A `LoopEvaluator` that, in execute mode, runs `verifier.VerifyAsync`: green → `CommitGreenAsync` + `Stop()`; stuck → `RestoreLastGreenAsync` + `Stop()`; red → `Continue(result.ToFeedback())`."),
                        new("Va en `LoopEvaluators`, después de `TodoCompletionLoopEvaluator`: una línea en la factoría.", "It goes in `LoopEvaluators`, after `TodoCompletionLoopEvaluator`: one line in the factory."),
                        new("Las otras tres: `CodeOwnerApprovalAgent` (DelegatingAIAgent exterior), `GitCheckpoints`, `RepoStateProvider` (AIContextProviders) y `BudgetGuard` (DelegatingChatClient interior).",
                            "The other three: `CodeOwnerApprovalAgent` (outer DelegatingAIAgent), `GitCheckpoints`, `RepoStateProvider` (AIContextProviders) and `BudgetGuard` (inner DelegatingChatClient)."),
                    ],
                    Lines:
                    [
                        new("Los tests los ejecuta el harness, no el modelo. El prompt no puede apagarlos.", "The tests are run by the harness, not by the model. The prompt can't switch them off."),
                        new("Esto no es un bucle nuevo. Cuatro piezas, cuatro puntos de extensión documentados.", "This is not a new loop. Four pieces, four documented extension points."),
                    ],
                    Do:
                    [
                        new("3b.evaluator", new("Revela `VerifiedCompletionEvaluator` y explica las cinco ramas.", "Reveal `VerifiedCompletionEvaluator` and explain the five branches."), CockpitAction.RevealEvaluator),
                        new("3b.wire", new("Enchúfalo: la línea de `LoopEvaluators` en la factoría.", "Plug it in: the `LoopEvaluators` line in the factory."), CockpitAction.RevealWiring),
                        new("3b.pieces", new("Las otras tres piezas (tarjetas de abajo y pestañas de código).", "The other three pieces (cards below and code tabs).")),
                    ],
                    Prompts: [],
                    Stage: null),
                new StepPart(
                    Id: "3c",
                    Title: new("El arreglo", "The fix"),
                    Minutes: 6,
                    Goal: new("El mismo prompt del muro, «no ejecutes los tests» incluido; ahora el harness comprueba igualmente, y la propietaria aprueba su código.", "The same prompt as the wall, \"don't run the tests\" included; now the harness checks anyway, and the code owner approves her code."),
                    Talk:
                    [
                        new("Mismo prompt. Sigue diciendo que no ejecute los tests. El harness los ejecuta igualmente.", "Same prompt. It still says not to run the tests. The harness runs them anyway."),
                        new("El `⏸ … @maria-payments` sale de `CodeOwnerApprovalAgent`: Payments es de María según CODEOWNERS, y quien pide nunca aprueba.", "The `⏸ … @maria-payments` comes from `CodeOwnerApprovalAgent`: Payments belongs to María in CODEOWNERS, and the requester never approves."),
                    ],
                    Lines:
                    [
                        new("Aprueba quien es dueño del código, nunca quien pidió el cambio.", "The approver is whoever owns the code, never whoever asked for the change."),
                    ],
                    Do:
                    [
                        new("3c.reset", new("Vuelve a preparar el repo en `after-mechanical-fix` (y `dotnet build`).", "Reset the repo to `after-mechanical-fix` again (and `dotnet build`)."), CockpitAction.ResetAfterFix),
                        new("3c.start", new("Monta el agente del step-3: ahora con el evaluador.", "Start the step-3 agent: now with the evaluator."), CockpitAction.StartStep3),
                        new("3c.mode", new("Modo `execute`.", "`execute` mode."), CockpitAction.ModeExecute),
                        new("3c.prompt", new("El **mismo prompt** del muro → `🔎 Verificación del harness … 8 de 24 fallan` → vuelve al trabajo.", "The **same prompt** as the wall → `🔎 Harness verification … 8 of 24 fail` → it goes back to work.")),
                        new("3c.domain", new("Edita `RoutePlanner.cs` (Domain, no protegido) → aprobación normal → **apruebas tú**.", "It edits `RoutePlanner.cs` (Domain, not protected) → normal prompt → **you approve**.")),
                        new("3c.owner", new("Edita `FareCalculator.cs` (Payments) → `⏸ Pendiente de aprobación de @maria-payments` → abre la ventana de María.", "It edits `FareCalculator.cs` (Payments) → `⏸ Pending approval from @maria-payments` → open María's window."), CockpitAction.OpenOwner),
                        new("3c.refuse", new("En la ventana de María: decide como @david-lorenzo → **rechazado** (no es el propietario y es quien pide). Luego como @maria-payments con «Revisado: tarifa en km».",
                            "In María's window: decide as @david-lorenzo → **refused** (not the owner, and he's the requester). Then as @maria-payments with \"Reviewed: fare in km\".")),
                        new("3c.more", new("Cada `⏸` nuevo (normalmente 2: uno por fichero de Payments) → aprueba como María.", "Each new `⏸` (usually 2: one per Payments file) → approve as María.")),
                        new("3c.green", new("`✅ Verde verificado por el harness … Checkpoint … en upgrade/teide-geo-2.0` → git log: el commit del checkpoint.", "`✅ Green, verified by the harness … Checkpoint … on upgrade/teide-geo-2.0` → git log: the checkpoint commit."), CockpitAction.GitLog),
                        new("3c.reality", new("Opcional: Realidad `dotnet test` → todo verde, sin el agente. Cadena de auditoría verificada en la ventana de María.", "Optional: Reality `dotnet test` → all green, without the agent. Audit chain verified in María's window."), CockpitAction.Test),
                    ],
                    Prompts:
                    [
                        new(new("El mismo prompt", "The same prompt"),
                            new("Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.", "It already compiles. Close the upgrade: write UPGRADE_REPORT.md and that's it. Don't run the tests, CI will run them.")),
                        new(new("Sigue", "Continue"), new("Sigue", "Continue")),
                    ],
                    Stage: AgentStage.Step3),
            ],
            Snippets:
            [
                new("s3-evaluator", new("VerifiedCompletionEvaluator", "VerifiedCompletionEvaluator"), new("El que se escribía en directo: cinco ramas, ~30 líneas.", "The one typed live: five branches, ~30 lines.")),
                new("s3-wire", new("Cableado", "Wiring"), new("Una línea en LoopEvaluators.", "One line in LoopEvaluators.")),
                new("s3-factory", new("La factoría", "The factory"), new("Cuatro piezas, cuatro puntos de extensión.", "Four pieces, four extension points.")),
                new("s3-owner", new("CodeOwnerApprovalAgent", "CodeOwnerApprovalAgent"), new("Aparca lo protegido para su propietario.", "Parks protected edits for their owner.")),
                new("s3-state", new("RepoStateProvider", "RepoStateProvider"), new("La verdad del repo en cada ejecución.", "The repo's truth on every run.")),
                new("s3-budget", new("BudgetGuard", "BudgetGuard"), new("Tope de tokens por sesión.", "Token cap per session.")),
            ],
            WatchFor:
            [
                new("El agente edita Payments con `run_shell` (`sed -i`, `perl -pi`): se aparca igual para María. «El shell es opaco, así que el harness es conservador».", "The agent edits Payments via `run_shell` (`sed -i`, `perl -pi`): it still parks for María. \"The shell is opaque, so the harness is conservative.\""),
                new("NU1101 falso en la verificación → falta el `dotnet build` después del reset (los botones de reset ya lo hacen).", "A fake NU1101 in the verification → the `dotnet build` after the reset is missing (the reset buttons already do it)."),
                new("Vas tarde → quita el step-4 y lo opcional; nunca recortes 3a–3c.", "Running late → drop step 4 and the optional rows; never cut 3a–3c."),
            ],
            Optional: []),

        new StepContent(
            Number: 4,
            Title: new("Hosted Agent", "Hosted Agent"),
            Tagline: new("La misma factoría, desatendida, en Foundry", "The same factory, unattended, in Foundry"),
            Start: TimeSpan.FromMinutes(37),
            Minutes: 5,
            Goal: new(
                "La misma factoría, desatendida, en Foundry. El trabajo lanzado en T-60 lleva aparcado esperando a María; su cómputo se liberó y se reanuda en cómputo nuevo con `$HOME` intacto.",
                "The same factory, unattended, in Foundry. The job triggered at T-60 has been parked on María's approval; its compute was released, and it resumes on new compute with `$HOME` intact."),
            Parts:
            [
                new StepPart(
                    Id: "4",
                    Title: new("Aparcado, aprobado, en verde", "Parked, approved, green"),
                    Minutes: 5,
                    Goal: new("Mientras espera a una persona no cuesta cómputo.", "While waiting for a human it costs no compute."),
                    Talk:
                    [
                        new("La misma `UpgradeCaptainFactory` detrás de un host Invocations: `upgrade.requested`, `approval.decided`, `job.status`.", "The same `UpgradeCaptainFactory` behind an Invocations host: `upgrade.requested`, `approval.decided`, `job.status`."),
                        new("Aquí quien pide es `@dependabot`, no una persona. María aprueba igual.", "The requester here is `@dependabot`, not a person. María approves just the same."),
                        new("Escala a cero: mientras espera a una persona no cuesta cómputo.", "Scale to zero: while waiting for a human it costs no compute."),
                    ],
                    Lines:
                    [
                        new("En esta demo la identidad de María va en el payload porque aquí solo tengo una cuenta. En producción viene de `x-agent-user-id`.", "In this demo María's identity comes in the payload because I only have one account here. In production it comes from `x-agent-user-id`."),
                    ],
                    Do:
                    [
                        new("4.status", new("`job.status` → `waiting_approval`, APR-0001 para @maria-payments.", "`job.status` → `waiting_approval`, APR-0001 for @maria-payments."), CockpitAction.HostedStatus),
                        new("4.approve", new("Aprueba APR-0001 como @maria-payments con «Revisado: tarifa en km» → se reanuda en cómputo nuevo.", "Approve APR-0001 as @maria-payments with \"Reviewed: fare in km\" → it resumes on new compute.")),
                        new("4.watch", new("Vigila el estado → suele aparecer APR-0002 (`TourPackagePricing.cs`) → apruébalo.", "Watch the status → APR-0002 (`TourPackagePricing.cs`) usually appears → approve it."), CockpitAction.HostedWatch),
                        new("4.green", new("→ `green` (~1 min) con `reportPath`; el informe lista las dos aprobaciones.", "→ `green` (~1 min) with `reportPath`; the report lists both approvals.")),
                        new("4.insights", new("App Insights: la traza de la ejecución alojada.", "App Insights: the trace of the hosted run.")),
                    ],
                    Prompts: [],
                    Stage: null),
            ],
            Snippets:
            [
                new("s4-host", new("AgentHost", "AgentHost"), new("El host Invocations y la única línea de telemetría.", "The Invocations host and the only telemetry line.")),
                new("s4-options", new("ForHosted", "ForHosted"), new("La misma factoría, otras opciones.", "Same factory, different options.")),
                new("s4-handler", new("Eventos", "Events"), new("upgrade.requested · approval.decided · job.status", "upgrade.requested · approval.decided · job.status")),
            ],
            WatchFor:
            [
                new("Si pasas de las 38:00, salta al cierre y enseña la diapositiva de capturas.", "If you're past 38:00, skip to the close and show the screenshots slide."),
                new("Cada `job.status` despierta la sesión: no lo llames antes del step-4.", "Every `job.status` wakes the session: don't call it before step 4."),
            ],
            Optional: []),
    ];

    public static StepContent? Find(int number) => Steps.FirstOrDefault(s => s.Number == number);

    /// <summary>Las secciones del guion, para el reloj: (título, inicio, minutos).</summary>
    public static IReadOnlyList<(Bi Title, TimeSpan Start, int Minutes)> Agenda { get; } =
    [
        (new("Apertura", "Opening"), TimeSpan.Zero, 7),
        (new("1 · Harness", "1 · Harness"), TimeSpan.FromMinutes(7), 8),
        (new("2 · Approve", "2 · Approve"), TimeSpan.FromMinutes(15), 8),
        (new("3 · Tu harness", "3 · Your harness"), TimeSpan.FromMinutes(23), 14),
        (new("4 · Hosted", "4 · Hosted"), TimeSpan.FromMinutes(37), 5),
        (new("Cierre", "Close"), TimeSpan.FromMinutes(42), 8),
    ];
}
