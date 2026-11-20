---
name: nuget-upgrade
description: The company playbook for upgrading NuGet packages in repos with central package management. ALWAYS use it when you are going to change a package version, prepare the branch or the commit of an upgrade, or write the final report.
---

# Upgrading a NuGet package (Canarias.Tours playbook)

## Rules

1. **Central management only.** Versions live in `Directory.Packages.props`. Never put `Version=`
   in a `.csproj` and never use `dotnet add package`.
2. **Never prereleases.** Stable versions only (no `-preview`, `-beta`, `-rc`...). The script rejects them.
3. **Branch:** `upgrade/<package-in-lowercase-with-dashes>-<major.minor>`.
   Example: Teide.Geo 2.0.0 → `upgrade/teide-geo-2.0`.
4. **Commit:** `chore(deps): <Package> <from> → <to> (<advisory if any>)`.
   Example: `chore(deps): Teide.Geo 1.8.0 → 2.0.0 (TEIDE-2026-0042)`.
5. **Never publish:** no `git push` and no `dotnet nuget push`. CI and people take care of that.
6. **Done = green.** The upgrade isn't done until `dotnet build` and `dotnet test` pass.

## Changing the version: `scripts/bump_package.cs`

A .NET 10 single-file app (it runs with `dotnet run`). It edits `Directory.Packages.props`
deterministically: it only changes the package's `Version` attribute.

- Arguments: a **JSON array of three strings**, in this order:
  1. path of `Directory.Packages.props`, relative to the repo root (or absolute)
  2. package id
  3. new version
- Example call:

  ```
  run_skill_script(
    skillName = "nuget-upgrade",
    scriptName = "scripts/bump_package.cs",
    arguments = ["Directory.Packages.props", "Teide.Geo", "2.0.0"])
  ```

- Returns JSON: `{"package": ..., "from": ..., "to": ..., "changed": true|false}`.
- Fails with a non-zero exit code if the package isn't in the file or the version is a prerelease.

## Final report (`UPGRADE_REPORT.md` at the repo root)

```markdown
# Upgrade of <Package> <from> → <to>

- Reason: <security advisory or reason>
- Branch: upgrade/<package>-<major.minor>

## Breaking changes and how they were resolved
| Change | Files | Fix |
|---|---|---|

## Verification
- dotnet build: ✅/❌
- dotnet test: N passed, M failed
- Remaining vulnerable packages: none / list

## Approvals
- <protected path> approved by <owner> (<approval id>)
```
