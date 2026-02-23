// ModEntry.cs
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Menus;

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
            _data = Helper.Data.ReadSaveData<LunchPailData>(DataKey)
                ?? new LunchPailData();

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
        // Day ending: clear compartments so the pail is empty for the next morning
        // ----------------------------------------------------------------

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            _data.StaminaCompartment.Clear();
            _data.HealthCompartment.Clear();
            Helper.Data.WriteSaveData(DataKey, _data);
            Monitor.Log("[LunchPail] Compartments cleared for end of day.", LogLevel.Debug);
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
    }
}