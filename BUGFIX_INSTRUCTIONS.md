# Bug Fix: HasLunchPail Derived From Game State

## Problem

`HasLunchPail` is currently a persisted flag set by `OnInventoryChanged` catching the crafted unlock item. This flag can be corrupted during SMAPI's startup/loading sequence because:

1. `OnInventoryChanged` lacks a `Context.IsWorldReady` guard
2. Other mods (Expanded Starter Package) fire inventory events during loading
3. `OnDayEnding` fires during game transitions and writes stale `_data` to save
4. `_data` persists in memory across game loads within one SMAPI session (Entry() only runs once)

The result: `HasLunchPail` can become `true` on a brand new save at Mining Level 0.

## Root Cause (Architectural)

The real problem is that `HasLunchPail` is treated as the **source of truth** when it should be **derived from game state**. The game already knows the player's mining level and whether they've crafted a recipe. The mod should read that, not maintain its own fragile flag.

## Fix: Derive HasLunchPail from Game State

### Step 1: Add a new private method to ModEntry.cs

```csharp
/// <summary>
/// Derives HasLunchPail from actual game state rather than a persisted flag.
/// Mining Level 0 → impossible to have crafted → false.
/// Mining Level 1+ → true only if the recipe has been crafted at least once.
/// </summary>
private void DeriveHasLunchPail()
{
    var player = Game1.player;
    if (player.MiningLevel < 1)
    {
        _data.HasLunchPail = false;
        return;
    }

    _data.HasLunchPail =
        player.craftingRecipes.TryGetValue(RecipeId, out int timesCrafted)
        && timesCrafted > 0;
}
```

### Step 2: Call DeriveHasLunchPail in OnSaveLoaded

In `OnSaveLoaded`, call `DeriveHasLunchPail()` AFTER loading save data but BEFORE logging. This overwrites whatever the save file says with the actual game state:

```csharp
private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
{
    _data = Helper.Data.ReadSaveData<LunchPailData>(DataKey)
        ?? new LunchPailData();

    DeriveHasLunchPail();          // ← ADD THIS LINE

    _consumptionManager.ResetSession();
    Monitor.Log(
        $"[LunchPail] Save data loaded. HasLunchPail={_data.HasLunchPail}",
        LogLevel.Debug);

    GrantRecipeIfEligible();
}
```

### Step 3: Call DeriveHasLunchPail in OnDayStarted

```csharp
private void OnDayStarted(object? sender, DayStartedEventArgs e)
{
    DeriveHasLunchPail();          // ← ADD THIS LINE
    _consumptionManager.ResetSession();
}
```

### Step 4: Add world-ready guard to OnInventoryChanged

Even though `OnInventoryChanged` is no longer the authority for activation, keep it for the instant activation dialogue when the player crafts mid-session. But add the guard so it never fires during loading:

```csharp
private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
{
    if (!Context.IsWorldReady) return;   // ← ADD THIS LINE
    if (_data.HasLunchPail) return;
    // ... rest unchanged
}
```

### Step 5 (optional): Add world-ready guard to OnDayEnding

`OnDayEnding` writes `_data` to save. The logs show it fires during startup transitions. Adding a guard prevents it from writing stale data from a previous game session:

```csharp
private void OnDayEnding(object? sender, DayEndingEventArgs e)
{
    if (!Context.IsWorldReady) return;   // ← ADD THIS LINE (optional safety)
    _data.StaminaCompartment.Clear();
    _data.HealthCompartment.Clear();
    Helper.Data.WriteSaveData(DataKey, _data);
    Monitor.Log("[LunchPail] Compartments cleared for end of day.", LogLevel.Debug);
}
```

## What NOT to Change

- `ActivateLunchPail()` — keep it as-is. It still provides the mid-session dialogue and immediate save write when the player crafts. `DeriveHasLunchPail` will simply confirm the same thing on next load/day-start.
- `LunchPailData.HasLunchPail` — keep the property. It's still useful as a quick runtime check. It's just no longer the *authority*; game state is.
- `OnAssetRequested` — no changes needed. The recipe's `Mining 1` condition is already correct.
- `GrantRecipeIfEligible()` — no changes needed.

## Summary of All Changes

| File | Change |
|------|--------|
| `ModEntry.cs` | Add `DeriveHasLunchPail()` method |
| `ModEntry.cs` | Call it in `OnSaveLoaded` after data load |
| `ModEntry.cs` | Call it in `OnDayStarted` before ResetSession |
| `ModEntry.cs` | Add `if (!Context.IsWorldReady) return;` to `OnInventoryChanged` |
| `ModEntry.cs` | (Optional) Add `if (!Context.IsWorldReady) return;` to `OnDayEnding` |

No other files are affected. This is a ModEntry.cs-only fix.
---

# Tracking: v1.0.0 Save Compatibility

## Background

`FoodTag.MaxServings` was introduced in v1.1.0. Any save created or last written by v1.0.0 will have `MaxServings` absent from the serialized JSON, so SMAPI's data API will deserialize it as `0` — but because the property default is `int.MaxValue`, **a missing field deserializes to `int.MaxValue` (unlimited)**, not 0. This is actually the safe fallback: the item will auto-consume without restriction, matching 1.0.0 behaviour.

## Observed symptom (February 25, 2026)

On first load after upgrading, a lunch pail that had not been emptied the previous night displayed no quantities (Bug 1 — `MaxServings == int.MaxValue` → label was bare name). After a single open-and-close of the serving-count dialog, quantities appeared correctly. This is **consistent with a 1.0.0 save** since all tags would carry `MaxServings = int.MaxValue`.

## Resolution status

- **Bug 1** (no quantity displayed for unlimited tags) is **completely fixed** in `bugfix/quantity-display-and-deficit-reduction` — unlimited tags now show the live stack count. Confirmed by log testing on February 25, 2026.
- **Bug 2** (deficit reduction discarded when item in single compartment) is **completely fixed** — full shortage now correctly falls to whichever compartment holds the item. Confirmed by log testing on February 25, 2026.
- The behaviour change (unlimited → explicit MaxServings) only takes effect after the player opens the Edit dialog for each item and saves a serving count. Existing unlimited tags remain fully functional in the meantime.

## Items to watch

- [ ] **PROVISIONAL** — Nightly compartment-clear (`OnDayEnding`) may not be firing correctly for saves created on v1.0.0. Observed once (February 25, 2026): lunch pail was not empty at start of day on a save that may have been last written by v1.0.0. Could not be confirmed definitively. Monitor on saves known to have been created and saved entirely on v1.1.1+.
- [ ] If a 1.0.0 → 1.1.x save is tested deliberately, verify that auto-consumption still fires for unlimited tags (regression check against `IsBudgetExhausted` which short-circuits on `int.MaxValue`).
- [ ] Confirm whether the nightly compartment-clear correctly re-serializes tags with `MaxServings` set, so the value survives a save/load cycle.