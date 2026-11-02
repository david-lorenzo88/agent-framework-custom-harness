---
name: teide-geo-migration
description: Guía de migración de Teide.Geo 1.x a 2.0 con sus cuatro cambios incompatibles y recetas de arreglo. Úsala cuando pregunten qué cambia en Teide.Geo 2.0, al hacer inventario de puntos de rotura o al arreglar código que usa GeoPoint, GeoCalculator, ElevationClient o GeoFormat.
---

# Migrar de Teide.Geo 1.x a 2.0

Teide.Geo 2.0.0 corrige el aviso **TEIDE-2026-0042** (ReDoS en `GeoFormat.Parse`) y trae
**cuatro cambios incompatibles**. Tres rompen la compilación; **uno no**.

## Pasos

1. Lee `references/breaking-changes.md` (los cuatro cambios, con síntomas).
2. Haz inventario: busca en el código `GeoPoint`, `GeoCalculator.Distance`, `GetElevation`, `GeoFormat.ToDms`.
   Ignora las menciones dentro de comentarios: no son llamadas.
3. Aplica las recetas de `assets/fix-recipes.md`.
4. **Revisa a mano cada llamada a `GeoCalculator.Distance`**: en 2.0 devuelve METROS. Si el código
   espera kilómetros (precios por km, totales en km, umbrales en km) usa `DistanceUnit.Kilometers`.
5. Compila y pasa TODOS los tests. El cambio de unidades solo lo detectan los tests.

## Atención

- `Distance` en metros **compila sin errores**: es el cambio más peligroso.
- Las tarifas (`€/km`) y los totales de rutas son los sitios típicos donde se cuela.
