---
name: teide-geo-migration
description: Migration guide from Teide.Geo 1.x to 2.0 with its four breaking changes and fix recipes. Use it when asked what changes in Teide.Geo 2.0, when taking inventory of breaking points, or when fixing code that uses GeoPoint, GeoCalculator, ElevationClient or GeoFormat.
---

# Migrating from Teide.Geo 1.x to 2.0

Teide.Geo 2.0.0 fixes advisory **TEIDE-2026-0042** (ReDoS in `GeoFormat.Parse`) and brings
**four breaking changes**. Three of them break the build; **one doesn't**.

## Steps

1. Read `references/breaking-changes.md` (the four changes, with their symptoms).
2. Take inventory: search the code for `GeoPoint`, `GeoCalculator.Distance`, `GetElevation`, `GeoFormat.ToDms`.
   Ignore mentions inside comments: they are not calls.
3. Apply the recipes in `assets/fix-recipes.md`.
4. **Review every call to `GeoCalculator.Distance` by hand**: in 2.0 it returns METERS. If the code
   expects kilometers (prices per km, totals in km, thresholds in km) use `DistanceUnit.Kilometers`.
5. Build and pass ALL the tests. Only the tests catch the change of units.

## Watch out

- `Distance` in meters **compiles without errors**: it is the most dangerous change.
- Fares (`€/km`) and route totals are the typical places where it slips through.
