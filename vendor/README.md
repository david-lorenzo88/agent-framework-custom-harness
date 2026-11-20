# vendor/ · consola del harness de Microsoft Agent Framework

Copia literal de los proyectos de consola compartidos por los samples del harness de Agent Framework.
Los usamos para la UX de la demo (streaming, aprobaciones en consola, `/todos`, `/mode`, `/session-export`).

| Campo | Valor |
|---|---|
| Origen | https://github.com/microsoft/agent-framework |
| Ruta | `dotnet/samples/02-agents/Harness/{Harness_Shared_Console, Harness_Shared_Console_OpenAI, ConsoleReactiveFramework, ConsoleReactiveComponents}` |
| Tag | `dotnet-1.24.0` |
| Commit | `245d4284a5018b7f446b5eac4d3add1fa5547229` |
| Licencia | MIT, © Microsoft Corporation (se conservan las cabeceras de cada fichero) |

## Cambios locales

Solo los imprescindibles para compilar fuera del repo de Agent Framework:

1. `Harness_Shared_Console/Harness_Shared_Console.csproj`: el `ProjectReference` a `src/Microsoft.Agents.AI`
   pasa a `PackageReference Include="Microsoft.Agents.AI"` (versión fijada en `Directory.Packages.props`, 1.24.0).
2. `vendor/Directory.Build.props` (nuevo): suprime `MAAI001` en estos proyectos, igual que hacía
   `dotnet/samples/Directory.Build.props` en la tag. Así no hay que tocar los `.cs` vendorizados.

3. `Commands/ModeCommandHandler.cs` y `Observers/PlanningOutputObserver.cs`: `SetModeAsync(…)` pasa a
   `SetModeAsync(…, disableNotification: true)` (una línea en cada fichero, marcada con `// upgrade-captain`).
   El aviso que inyecta `AgentModeProvider` al cambiar de modo desde fuera («[Mode changed: … You must now
   adjust your behavior …]») lo bloquea el filtro de contenido de Azure OpenAI (HTTP 400 `content_filter`,
   «Parameter: prompt»). Sin él no se pierde nada: las instrucciones del proveedor ya dicen en cada llamada en
   qué modo está el agente. Ver D-31 en `docs/verification-log.md`.

Ningún otro fichero `.cs` se ha modificado.

> Los scripts de las skills no usan el `SubprocessScriptRunner` del sample Claw (lanza Python): los ejecuta
> `src/UpgradeCaptain.Core/DotnetScriptRunner.cs` con `dotnet run script.cs`.

## Cómo actualizar

```bash
git -C .ref/af fetch --depth 1 origin tag dotnet-<versión>
git -C .ref/af checkout dotnet-<versión>
rsync -a --delete --exclude bin --exclude obj --exclude '*.csproj' \
  .ref/af/dotnet/samples/02-agents/Harness/Harness_Shared_Console/ vendor/Harness_Shared_Console/
# …repite para las otras tres carpetas y revisa los .csproj a mano
```
