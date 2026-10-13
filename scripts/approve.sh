#!/usr/bin/env bash
# La "terminal de María" (step-6): decide aprobaciones aparcadas por el CodeOwnerApprovalAgent.
#
#   scripts/approve.sh APR-0001 --as @maria-payments [--reason "Revisado: tarifa en km"]
#   scripts/approve.sh next --as @maria-payments [--reason …]     (la pendiente más antigua)
#   scripts/approve.sh --reject APR-0001 --as @maria-payments --reason "No sin ticket"
#   scripts/approve.sh --list
#   scripts/approve.sh --audit
#
# Si la consola de David está esperando esa aprobación, la decisión la retoma esa consola;
# si no (por ejemplo tras run --job), esta misma terminal reanuda la ejecución.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

DLL="$ROOT/src/UpgradeCaptain.Console/bin/Debug/net10.0/upgrade-captain.dll"
[[ -f "$DLL" ]] || dotnet build "$ROOT/src/UpgradeCaptain.Console" --nologo -v q -clp:ErrorsOnly >/dev/null

run() { (cd "$ROOT" && dotnet "$DLL" "$@"); }

case "${1:-}" in
  --list)   run approvals list ;;
  --audit)  run approvals audit ;;
  --reject) shift; id="$1"; shift; run approvals reject "$id" "$@" ;;
  APR-*|next) id="$1"; shift; run approvals approve "$id" "$@" ;;
  *)        sed -n '2,12p' "$0"; exit 1 ;;
esac
