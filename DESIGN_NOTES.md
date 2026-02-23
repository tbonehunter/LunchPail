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
