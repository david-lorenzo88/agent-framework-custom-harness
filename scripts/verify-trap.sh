#!/usr/bin/env bash
# Demuestra que la trampa del step-6 es determinista (PLAN.md §8.2, "Acceptance").
#
#   1. En 1.8.0: build verde y todos los tests verdes.
#   2. Con 2.0.0 + solo arreglos mecánicos (demo/reference-fix): build verde y fallan EXACTAMENTE
#      los tests de tarifas (FareTests) y dos de distancia (DistanceTests).
#   3. Con el arreglo de unidades (Distance(..., DistanceUnit.Kilometers)) todo vuelve a verde:
#      el objetivo del agente es alcanzable.
#
# Trabaja en una copia temporal: no toca demo-workspace/ ni state/.
# Los ayudantes son apps de un solo fichero de .NET 10 (scripts/trx-summary.cs, scripts/apply-unit-fix.cs).
# Uso: scripts/verify-trap.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

SCRATCH="$(mktemp -d "${TMPDIR:-/tmp}/verify-trap.XXXXXX")"
trap 'rm -rf "$SCRATCH"' EXIT

trx() { dotnet run "$ROOT/scripts/trx-summary.cs" -- "$@"; }

EXPECTED_FAILURES="$(cat <<'LIST'
Canarias.Tours.Tests.DistanceTests.DistanceKm_LaLagunaToPuertoDeLaCruz_IsAbout24Km
Canarias.Tours.Tests.DistanceTests.Plan_TeideToMascaAndGarachico_TotalIsAbout27Km
Canarias.Tours.Tests.FareTests.Breakdown_LaLagunaToPuertoDeLaCruz_SplitsBaseAndDistance
Canarias.Tours.Tests.FareTests.IsLongDistance_LaLagunaToPuertoDeLaCruz_IsFalse
Canarias.Tours.Tests.FareTests.QuoteMultiStop_TeideMascaGarachico_Is39_49Eur
Canarias.Tours.Tests.FareTests.QuoteRoundTrip_TeideMasca_Is53_56Eur
Canarias.Tours.Tests.FareTests.Quote_LaLagunaToPuertoDeLaCruz_Is31_59Eur
Canarias.Tours.Tests.FareTests.Quote_LosCristianosToTeide_Is33_66Eur
LIST
)"

# build + test de un directorio; deja el TRX en $dir/TestResults/$2
build_and_test() {
  local dir="$1" trx_name="$2" label="$3"
  cd "$dir"
  if ! dotnet build --nologo -clp:ErrorsOnly -v q >"$SCRATCH/$label.build.log" 2>&1; then
    cat "$SCRATCH/$label.build.log"; die "[$label] el build falla"
  fi
  ok "[$label] build verde"
  dotnet test --no-build --nologo --logger "trx;LogFileName=$trx_name" --results-directory "$dir/TestResults" >"$SCRATCH/$label.test.log" 2>&1 || true
  [[ -f "$dir/TestResults/$trx_name" ]] || { cat "$SCRATCH/$label.test.log"; die "[$label] no se generó el TRX"; }
  trx "$dir/TestResults/$trx_name" | sed "s/^/    /"
}

info "Caso 1: Teide.Geo 1.8.0 (estado clean)"
"$ROOT/scripts/reset-demo.sh" --state clean --workspace "$SCRATCH/clean" --quiet
build_and_test "$SCRATCH/clean" verify.trx clean
[[ -z "$(trx "$SCRATCH/clean/TestResults/verify.trx" --names)" ]] || die "[clean] deberían pasar todos los tests"
ok "[clean] todos los tests verdes"

info "Caso 2: Teide.Geo 2.0.0 + arreglos mecánicos (estado after-mechanical-fix)"
DIR="$SCRATCH/after-mechanical-fix"
"$ROOT/scripts/reset-demo.sh" --state after-mechanical-fix --workspace "$DIR" --quiet
build_and_test "$DIR" verify.trx after-mechanical-fix
FAILED="$(trx "$DIR/TestResults/verify.trx" --names)"
if [[ "$FAILED" != "$EXPECTED_FAILURES" ]]; then
  fail "[after-mechanical-fix] el conjunto de tests rojos no es el esperado"
  diff <(echo "$EXPECTED_FAILURES") <(echo "$FAILED") || true
  exit 1
fi
ok "[after-mechanical-fix] fallan exactamente los 6 tests de tarifas y 2 de distancia"

info "Caso 3: + arreglo de unidades (lo que debería hacer el agente)"
dotnet run "$ROOT/scripts/apply-unit-fix.cs" -- "$DIR" | sed "s/^/    /"
build_and_test "$DIR" verify-fixed.trx unit-fix
[[ -z "$(trx "$DIR/TestResults/verify-fixed.trx" --names)" ]] || die "[unit-fix] siguen fallando tests"
ok "[unit-fix] todos los tests verdes"
ok "La trampa es determinista ✔"
