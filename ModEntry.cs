// ModEntry.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Menus;
using TBoneHunter.LunchPail.Helpers;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// SMAPI entry point for Lunch Pail.
    /// Handles mod lifecycle, crafting recipe injection, save data,
    /// keybind input, and wiring of ConsumptionManager.
    /// </summary>
    public class ModEntry : Mod
    {
        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private Config _config = null!;
        private LunchPailData _data = null!;
        private ConsumptionManager _consumptionManager = null!;

        private const string DataKey        = "tbonehunter.LunchPail.data";
        private const string UnlockItemId   = "tbonehunter.LunchPail_Unlock";
        private const string RecipeId       = "tbonehunter.LunchPail_Recipe";
        private const string ObjectTexture  = "Mods/TBoneHunter.LunchPail/Objects";

        private const int HudDuration = 10000; // 10 seconds in ms

        // ----------------------------------------------------------------
        // SMAPI Entry
        // ----------------------------------------------------------------

        public override void Entry(IModHelper helper)
        {
            _config = helper.ReadConfig<Config>();
            _data   = new LunchPailData();

            _consumptionManager = new ConsumptionManager(
                Monitor,
                () => _config,
                () => _data);

            // AssetRequested must be registered in Entry() so it catches
            // assets loaded before GameLaunched fires.
            helper.Events.Content.AssetRequested += OnAssetRequested;

            helper.Events.GameLoop.GameLaunched   += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded     += OnSaveLoaded;
            helper.Events.GameLoop.Saving         += OnSaving;
            helper.Events.GameLoop.DayStarted     += OnDayStarted;
            helper.Events.GameLoop.DayEnding      += OnDayEnding;
            helper.Events.GameLoop.UpdateTicked   += OnUpdateTicked;
            helper.Events.Input.ButtonPressed     += OnButtonPressed;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;

            // Force reload of cached assets so our edits are applied
            // even if the game already loaded them before our handler fired.
            helper.GameContent.InvalidateCache("Data/Objects");
            helper.GameContent.InvalidateCache("Data/CraftingRecipes");

            Monitor.Log("Lunch Pail loaded.", LogLevel.Debug);
        }

        // ----------------------------------------------------------------
        // GameLaunched: GMCM registration only
        // ----------------------------------------------------------------

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            ConfigMenu.Register(
                Helper,
                ModManifest,
                () => _config,
                config =>
                {
                    _config = config;
                    Helper.WriteConfig(config);
                });
        }

        // ----------------------------------------------------------------
        // Asset injection: unlock item + crafting recipe
        // ----------------------------------------------------------------

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs args)
        {
            // Provide the custom object sprite sheet from Assets/lunchpail.png
            if (args.Name.IsEquivalentTo(ObjectTexture))
            {
                args.LoadFromModFile<Texture2D>("Assets/lunchpail.png", AssetLoadPriority.Medium);
            }

            if (args.Name.IsEquivalentTo("Data/Objects"))
            {
                args.Edit(asset =>
                {
                    var dict = asset.AsDictionary<string, ObjectData>().Data;
                    dict[UnlockItemId] = new ObjectData
                    {
                        Name        = UnlockItemId,
                        DisplayName = "Lunch Pail",
                        Description = "A handmade lunch pail. Use it to unlock auto-food management.",
                        Type        = "Basic",
                        Category    = StardewValley.Object.litterCategory,
                        Price       = 0,
                        Edibility   = -300,
                        // Point to our custom 16x16 sprite sheet; index 0 = first (only) sprite
                        Texture     = ObjectTexture,
                        SpriteIndex = 0
                    };
                    Monitor.Log("[LunchPail] Data/Objects edit fired. Unlock item registered.", LogLevel.Debug);
                });
            }

            if (args.Name.IsEquivalentTo("Data/CraftingRecipes"))
            {
                args.Edit(asset =>
                {
                    var dict = asset.AsDictionary<string, string>().Data;
                    dict[RecipeId] =
                        $"771 50 334 1/Home/{UnlockItemId}/false/Mining 1";
                    Monitor.Log($"[LunchPail] Data/CraftingRecipes edit fired. Recipe: 771 50 334 1/Home/{UnlockItemId}/false/Mining 1", LogLevel.Debug);
                });
            }
        }

        // ----------------------------------------------------------------
        // Save data
        // ----------------------------------------------------------------

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                _data = Helper.Data.ReadSaveData<LunchPailData>(DataKey)
                    ?? new LunchPailData();

                // If saved data has items in compartments (e.g. crash recovery,
                // or save during a day with virtual chest contents), return them
                // to inventory on load so we start clean.
                ReturnAllToInventoryOnLoad();
            }
            else
            {
                // Farmhands can't access host save data; use a fresh session-only instance.
                _data = new LunchPailData();
                Monitor.Log(
                    "[LunchPail] Running as farmhand — save data is session-only (not persisted).",
                    LogLevel.Debug);
            }

            DeriveHasLunchPail();
            _consumptionManager.ResetSession();
            Monitor.Log(
                $"[LunchPail] Save data loaded. HasLunchPail={_data.HasLunchPail}",
                LogLevel.Debug);

            GrantRecipeIfEligible();
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            Helper.Data.WriteSaveData(DataKey, _data);
            Monitor.Log("[LunchPail] Save data written.", LogLevel.Debug);
        }

        // ----------------------------------------------------------------
        // Day started
        // ----------------------------------------------------------------

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            DeriveHasLunchPail();
            _consumptionManager.ResetSession();
        }

        // ----------------------------------------------------------------
        // Day ending: return virtual chest items to inventory, then clear
        // ----------------------------------------------------------------

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!_data.HasLunchPail) return;

            ReturnVirtualChestItemsEndOfDay();

            _data.StaminaCompartment.Clear();
            _data.HealthCompartment.Clear();
            if (Context.IsMainPlayer)
                Helper.Data.WriteSaveData(DataKey, _data);
            Monitor.Log("[LunchPail] End of day: compartments cleared.", LogLevel.Debug);
        }

        // ----------------------------------------------------------------
        // Update tick
        // ----------------------------------------------------------------

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            _consumptionManager.OnUpdateTicked(e);
        }

        // ----------------------------------------------------------------
        // Keybind: open UI
        // ----------------------------------------------------------------

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!_data.HasLunchPail) return;
            if (Game1.activeClickableMenu != null) return;

            if (_config.OpenLunchPailKey.JustPressed())
            {
                Game1.activeClickableMenu = new LunchPailUI(
                    Monitor, _data, () => _config, _consumptionManager);
            }
        }

        // ----------------------------------------------------------------
        // Inventory changed: watch for unlock item being crafted/received
        // ----------------------------------------------------------------

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (_data.HasLunchPail) return;

            // Watch for the item being added (crafted or given).
            // Remove it immediately and activate — the act of crafting is the unlock.
            foreach (var item in e.Added)
            {
                if (item.QualifiedItemId == $"(O){UnlockItemId}")
                {
                    Game1.player.removeItemFromInventory(item);
                    ActivateLunchPail();
                    return;
                }
            }
        }

        // ----------------------------------------------------------------
        // Activation: set flag, save immediately, show dialogue
        // ----------------------------------------------------------------

        private void ActivateLunchPail()
        {
            _data.HasLunchPail = true;
            if (Context.IsMainPlayer)
                Helper.Data.WriteSaveData(DataKey, _data);

            Game1.activeClickableMenu = new DialogueBox(
                "You've assembled your Lunch Pail! Food in your inventory can now be " +
                "designated for auto-consumption. Press " +
                $"{_config.OpenLunchPailKey} to manage your Lunch Pail.");

            Monitor.Log("[LunchPail] Lunch Pail activated for player.", LogLevel.Info);
        }

        // ----------------------------------------------------------------
        // Grant recipe to players already at Mining Level 1 or above.
        // Safe to call repeatedly — checks before adding.
        // ----------------------------------------------------------------

        private void GrantRecipeIfEligible()
        {
            var player = Game1.player;

            if (player.MiningLevel < 1) return;
            if (player.craftingRecipes.ContainsKey(RecipeId)) return;

            player.craftingRecipes.Add(RecipeId, 0);
            Monitor.Log(
                $"[LunchPail] Granted recipe to player with Mining Level {player.MiningLevel}.",
                LogLevel.Debug);
        }

        // ----------------------------------------------------------------
        // Derive HasLunchPail from actual game state rather than a persisted flag.
        // Mining Level 0 → impossible to have crafted → false.
        // Mining Level 1+ → true only if the recipe has been crafted at least once.
        // ----------------------------------------------------------------

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

        // ----------------------------------------------------------------
        // Virtual chest → inventory return logic
        // ----------------------------------------------------------------

        /// <summary>
        /// Attempts to return all remaining virtual chest items from both
        /// compartments to the player's inventory at end of day.
        /// Items that fit are returned normally. Items that don't fit
        /// (backpack full, no matching stack, no free slot) are lost —
        /// the player is notified via a HUD message about the spoilage.
        /// </summary>
        private void ReturnVirtualChestItemsEndOfDay()
        {
            var player = Game1.player;
            var spoiled = new List<(string displayName, int quantity)>();

            Monitor.Log(
                $"[LunchPail] ReturnVirtualChestItemsEndOfDay: StaminaTags={_data.StaminaCompartment.Count} HealthTags={_data.HealthCompartment.Count} FreeSlots={player.Items.Count(i => i == null)}",
                LogLevel.Debug);

            ReturnCompartmentItems(player, _data.StaminaCompartment, spoiled);
            ReturnCompartmentItems(player, _data.HealthCompartment, spoiled);

            if (spoiled.Count > 0)
            {
                // Build a concise spoilage summary.
                // e.g., "3 Salad, 2 Parsnip"
                var parts = new List<string>();
                foreach (var (name, qty) in spoiled)
                    parts.Add($"{qty} {name}");
                string summary = string.Join(", ", parts);

                string message = spoiled.Count == 1
                    ? $"You had no room for your leftover {summary}. It spoiled overnight."
                    : $"You had no room for some Lunch Pail leftovers. Spoiled: {summary}.";

                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type)
                    { timeLeft = HudDuration });

                Monitor.Log(
                    $"[LunchPail] End of day spoilage: {summary}",
                    LogLevel.Info);
            }
        }

        /// <summary>
        /// Iterates through all tags in a compartment and attempts to return
        /// each one to the player's inventory. Anything that can't be returned
        /// is added to the spoiled list.
        /// </summary>
        private void ReturnCompartmentItems(
            Farmer player,
            List<LunchPailData.FoodTag> compartment,
            List<(string displayName, int quantity)> spoiled)
        {
            foreach (var tag in compartment)
            {
                if (tag.Stack <= 0) continue;

                int returned = TryReturnTagToInventory(player, tag);
                int lost = tag.Stack; // whatever remains after the return attempt

                if (lost > 0)
                {
                    spoiled.Add((tag.DisplayName, lost));
                    Monitor.Log(
                        $"[LunchPail] End of day: {lost} {tag.DisplayName} spoiled (no inventory space).",
                        LogLevel.Debug);
                }

                if (returned > 0)
                {
                    Monitor.Log(
                        $"[LunchPail] End of day: returned {returned} {tag.DisplayName} to inventory.",
                        LogLevel.Debug);
                }
            }
        }

        /// <summary>
        /// Attempts to return as many items as possible from a tag to the
        /// player's inventory. First tries to stack onto an existing matching
        /// item, then tries to create a new stack in a free slot.
        /// Returns the number of items successfully returned.
        /// Decrements tag.Stack for each item returned; any remaining
        /// in tag.Stack after this call could not be returned.
        /// </summary>
        private int TryReturnTagToInventory(Farmer player, LunchPailData.FoodTag tag)
        {
            int totalReturned = 0;

            // First: try to add to an existing matching stack in inventory
            var existing = FoodHelper.FindTaggedItemInInventory(player, tag);
            if (existing != null && tag.Stack > 0)
            {
                // Stardew doesn't enforce a per-slot stack cap for most items,
                // so we can add the full remaining amount to the existing stack.
                int toAdd = tag.Stack;
                existing.Stack += toAdd;
                tag.Stack -= toAdd;
                totalReturned += toAdd;
            }

            // Second: if there's still a remainder, try a free slot
            if (tag.Stack > 0)
            {
                // Check for a free inventory slot
                bool hasFreeSlot = false;
                for (int i = 0; i < player.Items.Count; i++)
                {
                    if (player.Items[i] == null)
                    {
                        hasFreeSlot = true;
                        break;
                    }
                }

                if (hasFreeSlot)
                {
                    var newItem = FoodHelper.CreateInventoryItem(tag, tag.Stack);
                    if (newItem != null)
                    {
                        player.addItemToInventory(newItem);
                        totalReturned += tag.Stack;
                        tag.Stack = 0;
                    }
                }
            }

            return totalReturned;
        }

        /// <summary>
        /// Safety net: if saved data somehow contains virtual chest items
        /// at load time (e.g., crash mid-day, or save file edited), attempt
        /// to return them to inventory so the player doesn't lose food silently.
        /// Any items that can't be returned are logged but not spoiled — the
        /// player just started their day and deserves a clean slate.
        /// </summary>
        private void ReturnAllToInventoryOnLoad()
        {
            var player = Game1.player;
            bool hadItems = false;

            foreach (var tag in _data.StaminaCompartment)
            {
                if (tag.Stack <= 0) continue;
                hadItems = true;
                int returned = TryReturnTagToInventory(player, tag);
                if (tag.Stack > 0)
                    Monitor.Log(
                        $"[LunchPail] Load recovery: could not return {tag.Stack} {tag.DisplayName} (no space). Items lost.",
                        LogLevel.Warn);
                else if (returned > 0)
                    Monitor.Log(
                        $"[LunchPail] Load recovery: returned {returned} {tag.DisplayName} to inventory.",
                        LogLevel.Debug);
            }

            foreach (var tag in _data.HealthCompartment)
            {
                if (tag.Stack <= 0) continue;
                hadItems = true;
                int returned = TryReturnTagToInventory(player, tag);
                if (tag.Stack > 0)
                    Monitor.Log(
                        $"[LunchPail] Load recovery: could not return {tag.Stack} {tag.DisplayName} (no space). Items lost.",
                        LogLevel.Warn);
                else if (returned > 0)
                    Monitor.Log(
                        $"[LunchPail] Load recovery: returned {returned} {tag.DisplayName} to inventory.",
                        LogLevel.Debug);
            }

            if (hadItems)
            {
                _data.StaminaCompartment.Clear();
                _data.HealthCompartment.Clear();
                Monitor.Log("[LunchPail] Load recovery: compartments cleared after returning items.", LogLevel.Debug);
            }
        }
    }
}
