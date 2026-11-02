# Upgrade Captain

**«Plan, execute, approve: el Agent Harness de Microsoft Agent Framework a fondo»**: demo de la sesión de
David Lorenzo López en **netcoreconf Madrid 2026** (23 de octubre).

Un aviso de seguridad marca `Teide.Geo < 2.0.0`. La 2.0 trae cambios incompatibles, el PR de Dependabot se queda
en rojo y nadie se hace cargo. **Upgrade Captain** lleva ese bump a verde:

1. hace inventario de los puntos de rotura
2. los arregla
3. compila y ejecuta los tests
4. pide la aprobación del **propietario del código** protegido
5. deja un informe

Está construido con el `HarnessAgent` de **Microsoft Agent Framework 1.24.0 (.NET)**. Las piezas propias van
enchufadas en sus puntos de extensión documentados: no es un bucle hecho desde cero.

## La charla, paso a paso

Cuatro pasos, cada uno una **tag de git** que compila y se ejecuta por sí sola. La guía detallada está en
[`docs/steps.md`](docs/steps.md) y el guion minuto a minuto en [`docs/demo-guide.md`](docs/demo-guide.md).

| Tag | Diapositiva | Qué añade | Integrado o a medida |
|---|---|---|---|
| `step-1` | Harness | `AsHarnessAgent`, 5 tools, historial persistido, compactación, OTel, modo plan por defecto y `TodoCompletionLoopEvaluator` | integrado |
| `step-2` | Approve | shell confinado con lista de denegación, acceso a ficheros y aprobaciones; skills en ficheros con script; Aspire Dashboard. CodeAct (`--surface codeact`) va en una diapositiva | integrado + paquetes preview |
| `step-3-pre` | Tu harness (el muro) | factoría + `CodeOwnerApprovalAgent`, `GitCheckpoints`, `RepoStateProvider`, `BudgetGuard`, **sin** el evaluador: aquí se ve el muro y se escribe el evaluador en directo | **a medida** |
| `step-3` | Tu harness | + `VerifiedCompletionEvaluator`: «terminado» = compila y pasan los tests, aunque el prompt diga lo contrario | **a medida** |
| `step-4` | Hosted Agent | la misma factoría detrás de un host Invocations en Foundry (opcional si falta tiempo) | hosting integrado + estado propio |

```bash
scripts/goto-step.sh 2        # checkout de step-2, repo de la demo limpio y build
scripts/diff-step.sh 2        # qué añade el paso 2 respecto al 1
```

Las tags de la primera versión de la charla (ocho pasos, `step-0`…`step-7`) siguen disponibles como `legacy/step-N`.

Si cambias algo en `main` (código compartido, tests, scripts o docs), reconstruye las tags con
`scripts/rebuild-steps.sh`: compila y prueba cada paso y solo entonces mueve las tags; la cadena anterior queda
como `previous/step-N`. Para cambiar el `Program.cs` del step-1 o del step-2, haz commit en una rama sacada de
esa tag y pásala con `--step1-from` / `--step2-from` (detalles en la cabecera del script).

## Puesta en marcha

Requisitos: macOS o Linux, .NET 10 SDK, git, Docker (para Aspire) y un proyecto de Microsoft Foundry
con un despliegue de modelo (`gpt-5.4` o `gpt-5.4-mini`).
Todo el código es .NET. La única excepción es CodeAct (step-2, `--surface codeact`): `LocalCodeActProvider` de Agent Framework ejecuta
el Python que escribe el modelo, así que ese paso necesita `python3` ≥ 3.11 (`scripts/setup-venv.sh`).

```bash
cp .env.example .env              # FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL, AZURE_TOKEN_CREDENTIALS=dev
az login
scripts/check-env.sh --model      # entorno + llamada mínima al modelo
scripts/pack-feed.sh              # empaqueta Teide.Geo 1.8.0 y 2.0.0 en demo/feed
scripts/reset-demo.sh             # crea demo-workspace/canarias-tours (el repo que arregla el agente)
scripts/setup-venv.sh             # solo para CodeAct (step-2): Python que ejecuta LocalCodeActProvider
dotnet run --project src/UpgradeCaptain.Console
```

Comprobaciones sin modelo:

```bash
scripts/verify-trap.sh                                  # la trampa del step-3 es determinista
dotnet test tests/UpgradeCaptain.Harness.Tests          # piezas a medida, solo con falsos
UC_INTEGRATION=1 dotnet test tests/UpgradeCaptain.Harness.Tests --filter Step3Beat   # el beat del step-3 con un modelo guionizado
```

## Estructura

```
src/UpgradeCaptain.Core       prompt, tools, rutas, telemetría, compactación · factoría desde el step-3
src/UpgradeCaptain.Harness    piezas a medida del step-3
src/UpgradeCaptain.Console    consola (Program.cs construye el agente a mano en los pasos 1 y 2)
src/UpgradeCaptain.Hosted     host Invocations del step-4
tests/                        tests xUnit con falsos (sin llamadas al modelo)
skills/                       nuget-upgrade y teide-geo-migration
demo/                         Teide.Geo 1.8.0/2.0.0, plantilla de Canarias.Tours, avisos, trabajos
vendor/                       consola de los samples del harness de Agent Framework (MIT, sin cambios)
deploy/                       Dockerfile y agent.manifest.yaml del Hosted Agent
scripts/                      todo lo que se ejecuta en el escenario y en los ensayos
docs/                         pasos, arquitectura, guion, planes B y registro de verificación
```

## Documentación

- [`docs/steps.md`](docs/steps.md): cómo crece el harness, paso a paso
- [`docs/architecture.md`](docs/architecture.md): arquitectura, flujo de aprobaciones y por qué el Hosted Agent mantiene el shell
- [`docs/run-of-show.md`](docs/run-of-show.md): guion minuto a minuto
- [`docs/fallbacks.md`](docs/fallbacks.md): planes B
- [`docs/verification-log.md`](docs/verification-log.md): qué se ha verificado, diferencias con el plan (D-xx) y pendientes
- [`PLAN.md`](PLAN.md): el plan original de la demo

## Aviso

`LocalShellExecutor` y `LocalCodeActProvider` ejecutan comandos y Python **en tu máquina**: no son un sandbox.
Todo está confinado a `demo-workspace/canarias-tours`. No los apuntes a otros repos ni a tu `$HOME`.

Código de ejemplo para una charla. La consola de `vendor/` es © Microsoft (licencia MIT).
