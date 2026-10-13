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

Cada paso es una **tag de git** que compila y se ejecuta por sí sola. La guía detallada está en [`docs/steps.md`](docs/steps.md).

| Tag | Diapositiva | Qué añade | Integrado o a medida |
|---|---|---|---|
| `step-0` | Chat básico | `ChatClientAgent` sin tools ni sesión | integrado |
| `step-1` | Harness | `AsHarnessAgent`, 5 tools, historial persistido, compactación, OTel | integrado |
| `step-2` | Plan / execute | modo plan por defecto, `TodoCompletionLoopEvaluator` | integrado |
| `step-3` | Approve | shell confinado con lista de denegación, acceso a ficheros, aprobaciones | integrado + paquete preview |
| `step-4` | CodeAct | `LocalCodeActProvider`, `--surface direct\|codeact` | paquete preview |
| `step-5` | Skills y OTel | skills en ficheros con script, Aspire Dashboard | integrado |
| `step-6` | Tu harness | factoría + `VerifiedCompletionEvaluator`, `CodeOwnerApprovalAgent`, `GitCheckpoints`, `RepoStateProvider`, `BudgetGuard` | **a medida** |
| `step-7` | Hosted Agent | la misma factoría detrás de un host Invocations en Foundry | hosting integrado + estado propio |

```bash
scripts/goto-step.sh 3        # checkout de step-3, repo de la demo limpio y build
scripts/diff-step.sh 3        # qué añade el paso 3 respecto al 2
```

## Puesta en marcha

Requisitos: macOS o Linux, .NET 10 SDK, git, Docker (para Aspire) y un proyecto de Microsoft Foundry
con un despliegue de modelo (`gpt-5.4` o `gpt-5.4-mini`).
Todo el código es .NET. La única excepción es CodeAct (step-4): `LocalCodeActProvider` de Agent Framework ejecuta
el Python que escribe el modelo, así que ese paso necesita `python3` ≥ 3.11 (`scripts/setup-venv.sh`).

```bash
cp .env.example .env              # FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL, AZURE_TOKEN_CREDENTIALS=dev
az login
scripts/check-env.sh --model      # entorno + llamada mínima al modelo
scripts/pack-feed.sh              # empaqueta Teide.Geo 1.8.0 y 2.0.0 en demo/feed
scripts/reset-demo.sh             # crea demo-workspace/canarias-tours (el repo que arregla el agente)
scripts/setup-venv.sh             # solo para CodeAct (step-4): Python que ejecuta LocalCodeActProvider
dotnet run --project src/UpgradeCaptain.Console
```

Comprobaciones sin modelo:

```bash
scripts/verify-trap.sh                                  # la trampa del step-6 es determinista
dotnet test tests/UpgradeCaptain.Harness.Tests          # piezas a medida, solo con falsos
UC_INTEGRATION=1 dotnet test tests/UpgradeCaptain.Harness.Tests --filter Step6Beat   # el beat del step-6 con un modelo guionizado
```

## Estructura

```
src/UpgradeCaptain.Core       prompt, tools, rutas, telemetría, compactación · factoría desde el step-6
src/UpgradeCaptain.Harness    piezas a medida del step-6
src/UpgradeCaptain.Console    consola (Program.cs se escribe en directo en los pasos 0–5)
src/UpgradeCaptain.Hosted     host Invocations del step-7
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
