# DESIGN_NOTES.md
# Lunch Pail — Feature Design Session Notes
**Date:** February 23, 2026

---

## Session Context

All code was reviewed and confirmed working correctly. No bugs or functional changes were needed. The sole topic of this session was a planned **new feature**: partial-stack food assignment to the Lunch Pail.

---

## Problem Statement

Currently, assigning food to the Lunch Pail designates the **entire stack** of that item+quality combination. There is no way to say "I want the pail to use only 20 of my 40 parsnips." The player must give up the whole stack to the pail.

The desired improvement: allow the player to specify **how many servings** from a given stack to budget for auto-consumption.

---

## How Current Designation Works (Analysis)

- `FoodTag` in `LunchPailData.cs` stores: `ItemId`, `Quality`, `DisplayName` — **no quantity field.**
- The tag is a type-level marker: "any item of this ID and quality belongs to this compartment."
- `FindTaggedItemInInventory` finds the first matching stack at consumption time.
- `SilentConsume` decrements the stack by 1 each call.
- The entire matching stack is treated as the supply; the pail eats from it until it's gone.
- In the UI, tagged items are excluded from the "Unassigned" center panel — so once tagged, the whole stack is visually committed.

---

## Options Considered

### Option A — Physical Stack Split
At assignment time, split the stack into a "tagged" portion and a remainder using inventory manipulation.
- **Pro:** Conceptually simple, no new tracking fields.
- **Con:** Stardew auto-merges identical stacks (same ID + quality), so the split silently collapses. Rejected.

### Option B — `MaxServings` field on `FoodTag` ✅ SELECTED
Add a `MaxServings` integer to `FoodTag`, set at assignment time via a quantity prompt. `ConsumptionManager` tracks how many times it has consumed from each tag per day and stops when the budget is reached.
- **Pro:** Clean, additive, no inventory manipulation, no risk of item loss.
- **Con:** A consumption budget, not a hard inventory reservation (see below).

### Option C — Per-Day Serving Cap
Add a `DailyLimit` field — the pail auto-consumes at most N of this food per day, then stops. Resets on day start.
- **Pro:** Very simple reset logic; aligns with existing daily session model.
- **Con:** Answers a slightly different question (daily rate limiting) rather than protecting a portion of the stack long-term.

---

## Important Limitation of Option B (Acknowledged and Accepted)

`MaxServings` controls only what the **pail's auto-consumption logic** does. It is a *consumption budget*, not an *inventory reservation*.

- The mod has no ability to prevent the player from manually shipping, selling, cooking with, or otherwise using the "reserved" portion of the stack.
- Stardew provides no SMAPI hook or vanilla mechanism to "lock" part of a stack from player interaction.
- A true hard reservation would require a hidden chest or fake item wrapper — a major architectural change with significant fragility risk.

**Decision:** Stay with the existing tag-based structure. The chest approach would be a near-total rewrite of core mod architecture and is deferred indefinitely. The `MaxServings` budget approach cleanly solves the stated goal ("stop the pail from eating my whole stock") without overreaching.

---

## UI Adjustment Method — Path 2 (Click Assigned Item)

Three paths were considered for how to adjust `MaxServings` after initial assignment:

| Path | Description |
|------|-------------|
| Path 1 | Unassign and reassign — no new UI, but friction-heavy |
| Path 2 ✅ | Click assigned item → dialogue: Adjust amount / Unassign / Cancel |
| Path 3 | Separate edit button per row alongside X button — layout too tight at 48px row height |

**Selected: Path 2.** Clicking an already-assigned item in the side panels opens a contextual dialogue. This replaces the current "click does nothing" dead zone on assigned items and consolidates edit + remove into one interaction point.

---

## Agreed Implementation Plan

1. **`LunchPailData.cs`** — Add `MaxServings` (int) to `FoodTag`. Default: `int.MaxValue` (unlimited) to preserve behavior for existing saves.
2. **`ConsumptionManager.cs`** — Track runtime consumed count per tag (dictionary keyed by tag, reset in `ResetSession` / each day start). Stop consuming a tag when count reaches `MaxServings`.
3. **`LunchPailUI.cs` — Assignment flow** — After compartment is selected, insert a quantity prompt ("how many?") before the tag is created.
4. **`LunchPailUI.cs` — Assigned item click (Path 2)** — Clicking an item in the stamina or health panel opens: "Adjust serving amount / Unassign / Cancel."
5. **UI display** — Show serving budget on each assigned item row (e.g., "20 / 40" remaining vs. budgeted), or at minimum in the hover tooltip.

---

## Pre-Implementation Git State

Before any feature work begins, the repo was cleaned up:

| Commit | Message |
|--------|---------|
| `e9d2222` | chore: update Nexus update key, adjust csproj output path and exclusions, add large icon |
| `387c744` | chore: add LunchPail.code-workspace |

Repo is on `master`, fully clean. A feature branch will be created before any implementation begins.

---

## Next Step

Create feature branch (e.g., `feature/maxservings-partial-stack`) and begin implementation per the plan above.

---

## Implementation Session — February 23, 2026

### Decisions Finalized Before Coding

**Quantity input method**
`NumberSelectionMenu` (Stardew's native stack-split widget) was chosen. It supports both slider and keyboard input, accepts a min/max range, and requires no custom UI work. The slider concern (imprecision on touch) was noted but the widget also accepts typed numbers, making it suitable for all input methods.

**Upper bound for serving count**
Capped at the item's **current inventory stack size**, not 999. Rationale: allowing a number larger than what the player holds leads to a silent failure mode — the budget counter never triggers the low-supply warning even when actual inventory hits zero. Tying the max to the real stack prevents that class of confusion.

**MaxServings reset cadence**
Per-day cap, cleared each morning via `ResetSession`. Aligns with the "pack your lunch daily" mental model that matches expected player behavior.

**Row display**
A single number is sufficient: the item name is shown as `Name (×N)` when a budget is set. Showing the live inventory count alongside the budget was explicitly rejected — the player can check their inventory directly, and it would add visual noise without meaningful benefit. Items with `MaxServings == int.MaxValue` (unlimited / legacy) render identically to before.

**Edit flow pre-fill**
When a player opens the quantity selector on an already-assigned item, the default value is `min(MaxServings, currentStack)`. This keeps the selector internally consistent (the default never exceeds the max) and quietly adjusts when stock has shrunk since the budget was last set. Legacy unlimited tags pre-fill at the current stack count.

**Deficit detection (inventory drops below budget mid-day)**
The mod is not blind to live inventory — `ConsumptionManager` already reads `item.Stack` every tick. A deficit alert (fires when the real stack falls below the remaining daily budget) was discussed and determined to be technically straightforward. Decision: **deferred to a separate feature** to keep this feature's scope focused. Will revisit after gameplay testing.

### Implementation Summary

Branch: `feature/maxservings-partial-stack`  
Commit: `1fdbc11`

| File | Changes |
|------|---------|
| `LunchPailData.cs` | `MaxServings` (int, default `int.MaxValue`) added to `FoodTag` |
| `ConsumptionManager.cs` | `_consumedToday` dictionary; `IsBudgetExhausted`; `RecordConsumptionByItem`; `ResetSession` clears dictionary |
| `LunchPailUI.cs` | Serving count prompt after compartment selection; edit/unassign prompt on assigned-item click (Path 2); `(×N)` budget label on assigned rows; parallel `_staminaTags`/`_healthTags` lists |

---

## Bug Investigation — `HasLunchPail=True` on New Game / Mining Level 0 — February 23, 2026

### Reported Symptom

After rebuilding v1.1.0, starting a brand new game (Mining Level 0, no save history) resulted in the Lunch Pail being immediately active on the first morning. The log showed `HasLunchPail=True` being read from save data even though no crafting had ever taken place. This behavior was NOT present in v1.0.0.

### Testing Methodology (Four Tests)

The player ran four controlled tests and captured log output in `Lunch Pail Test.txt`:

| Test | Version | Scenario | Result |
|------|---------|----------|--------|
| 1 | 1.0.0 | New game | `HasLunchPail=False` — correct |
| 2 | 1.1.0 | New game | First load: `False`; subsequent same-session load: `True` — **bug** |
| 3 | 1.1.0 | Load of saved new game (no pail crafted) | First load: `False`; subsequent same-session load: `True` — **bug** |
| 4 | 1.1.0 | Load of the save that first exhibited the bug | `HasLunchPail=True` — **bug persisted across saves** |

Key observation: in v1.1.0, `OnDayEnding` fires during the startup/loading sequence — a new behavior introduced this session. This appears before the first `SaveLoaded`, indicating the engine is triggering end-of-day cleanup as part of the new-day transition during startup.

### Player's Stated Resolution Goal

The player specified the correct conceptual fix: the mod should check whether the Lunch Pail has been crafted in this save; if yes, `HasLunchPail=True`; if no, check Mining Level to determine whether the recipe should be available in the crafting menu. The pail must never self-activate without a crafting event.

### Root Cause Analysis

The `OnInventoryChanged` event handler in `ModEntry.cs` was the sole event handler **missing** the `if (!Context.IsWorldReady) return;` guard that all other handlers carry. Every other handler — `OnUpdateTicked`, `OnButtonPressed` — correctly refuses to act during the game's startup/loading sequence.

During startup, Expanded Starter Package (and possibly other mods) fire inventory-change events while injecting items into the starter chest. Because `OnInventoryChanged` had no world-ready guard, it ran during this phase, matched the unlock item condition, called `ActivateLunchPail()`, and wrote `HasLunchPail=True` to save data — before the world was in a playable state. The second `SaveLoaded` within the same session then read that incorrectly written `True`.

### Proposed Fix

Add `if (!Context.IsWorldReady) return;` as the first line of `OnInventoryChanged`, before the `HasLunchPail` early-exit check. This is a one-line addition consistent with every other event handler in the file. It does not change behavior during normal gameplay; it only prevents the handler from acting during the startup/loading phase.

**Status:** Identified and agreed upon. Implementation pending.
