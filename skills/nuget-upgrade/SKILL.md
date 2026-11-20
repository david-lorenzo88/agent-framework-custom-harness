---
name: nuget-upgrade
description: Playbook de la empresa para actualizar paquetes NuGet en repos con gestión central de paquetes. Úsala SIEMPRE que vayas a cambiar la versión de un paquete, preparar la rama o el commit de una actualización, o escribir el informe final.
---

# Actualizar un paquete NuGet (playbook de Canarias.Tours)

## Reglas

1. **Solo gestión central.** Las versiones viven en `Directory.Packages.props`. Nunca pongas `Version=`
   en un `.csproj` ni uses `dotnet add package`.
2. **Nunca prereleases.** Solo versiones estables (sin `-preview`, `-beta`, `-rc`...). El script las rechaza.
3. **Rama:** `upgrade/<paquete-en-minúsculas-con-guiones>-<mayor.menor>`.
   Ejemplo: Teide.Geo 2.0.0 → `upgrade/teide-geo-2.0`.
4. **Commit:** `chore(deps): <Paquete> <desde> → <hasta> (<aviso si lo hay>)`.
   Ejemplo: `chore(deps): Teide.Geo 1.8.0 → 2.0.0 (TEIDE-2026-0042)`.
5. **Nunca publiques:** ni `git push` ni `dotnet nuget push`. CI y las personas se encargan.
6. **Terminado = verde.** La actualización no está terminada hasta que `dotnet build` y `dotnet test` pasan.

## Cambiar la versión: `scripts/bump_package.cs`

App de un solo fichero de .NET 10 (se ejecuta con `dotnet run`). Edita `Directory.Packages.props` de forma
determinista: solo cambia el atributo `Version` del paquete.

- Argumentos: un **array JSON de tres cadenas**, en este orden:
  1. ruta de `Directory.Packages.props`, relativa a la raíz del repo (o absoluta)
  2. id del paquete
  3. versión nueva
- Ejemplo de llamada:

  ```
  run_skill_script(
    skillName = "nuget-upgrade",
    scriptName = "scripts/bump_package.cs",
    arguments = ["Directory.Packages.props", "Teide.Geo", "2.0.0"])
  ```

- Devuelve JSON: `{"package": ..., "from": ..., "to": ..., "changed": true|false}`.
- Falla con código distinto de 0 si el paquete no está en el fichero o la versión es una prerelease.

## Informe final (`UPGRADE_REPORT.md` en la raíz del repo)

```markdown
# Actualización de <Paquete> <desde> → <hasta>

- Motivo: <aviso de seguridad o razón>
- Rama: upgrade/<paquete>-<mayor.menor>

## Cambios incompatibles y cómo se resolvieron
| Cambio | Ficheros | Arreglo |
|---|---|---|

## Verificación
- dotnet build: ✅/❌
- dotnet test: N superados, M fallidos
- Paquetes vulnerables restantes: ninguno / lista

## Aprobaciones
- <ruta protegida> aprobada por <propietario> (<id de aprobación>)
```
