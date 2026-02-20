// ConsumptionManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using TBoneHunter.LunchPail.Helpers;
using SObject = StardewValley.Object;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// Monitors player health and stamina on each tick and triggers
    /// silent food consumption from the appropriate Lunch Pail compartment
    /// when thresholds are met. Handles fallback logic and HUD notifications.
    /// </summary>
    public class ConsumptionManager
    {
        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private readonly IMonitor _monitor;
        private readonly Func<Config> _config;
        private readonly Func<LunchPailData> _data;

        private bool _staminaFallbackNotified = false;
        private bool _healthFallbackNotified = false;

        // Cooldown counters for fallback reminder HUD messages.
        // Prevents HUD spam by throttling reminders to ~5 seconds.
        private int _staminaFallbackReminderCooldown = 0;
        private int _healthFallbackReminderCooldown = 0;
        private const int FallbackReminderInterval = 300; // ~5 seconds at 60 ticks/sec

        private const int TickInterval = 30; // twice per second

        // ----------------------------------------------------------------
        // Constructor
        // ----------------------------------------------------------------

        public ConsumptionManager(
            IMonitor monitor,
            Func<Config> config,
            Func<LunchPailData> data)
        {
            _monitor = monitor;
            _config = config;
            _data = data;
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Resets fallback notification flags and cooldowns.
        /// Call when the player loads or starts a new day so notifications
        /// fire fresh each session.
        /// </summary>
        public void ResetSession()
        {
            _staminaFallbackNotified = false;
            _healthFallbackNotified = false;
            _staminaFallbackReminderCooldown = 0;
            _healthFallbackReminderCooldown = 0;
        }

        /// <summary>
        /// Main tick handler. Hook this to UpdateTicked in ModEntry.
        /// Runs checks every TickInterval ticks.
        /// </summary>
        public void OnUpdateTicked(UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(TickInterval)) return;
            if (!Context.IsWorldReady) return;

            var player = Game1.player;
            var data = _data();
            var config = _config();

            if (!data.HasLunchPail) return;

            CheckStamina(player, data, config);
            CheckHealth(player, data, config);
        }

        // ----------------------------------------------------------------
        // Private: Stamina check
        // ----------------------------------------------------------------

        private void CheckStamina(Farmer player, LunchPailData data, Config config)
        {
            var foods = ResolveFoods(player, data.StaminaCompartment, byStamina: true, config);

            if (foods.Count > 0)
            {
                var next = foods.First();
                if (FoodHelper.ShouldTriggerStamina(next, player, config.StaminaOffset))
                {
                    FoodHelper.SilentConsume(next, player);
                    _monitor.Log($"[LunchPail] Auto-consumed {next.DisplayName} for stamina.", LogLevel.Trace);
                    CheckStaminaSupplyWarning(player, data);
                }

                // Reset fallback flag only when primary compartment has items,
                // regardless of whether the trigger fired this tick.
                _staminaFallbackNotified = false;
                _staminaFallbackReminderCooldown = 0;
                return;
            }

            // Stamina compartment empty — attempt fallback to health compartment
            var fallbackFoods = ResolveFoods(player, data.HealthCompartment, byStamina: true, config);
            if (fallbackFoods.Count == 0) return;

            var fallbackNext = fallbackFoods.First();
            if (!FoodHelper.ShouldTriggerStamina(fallbackNext, player, config.StaminaOffset)) return;

            if (!_staminaFallbackNotified)
            {
                ShowHUD("Lunch Pail: Stamina food depleted! Drawing from health compartment.", isError: true);
                _staminaFallbackNotified = true;
                _staminaFallbackReminderCooldown = 0;
            }
            else
            {
                _staminaFallbackReminderCooldown++;
                if (_staminaFallbackReminderCooldown >= FallbackReminderInterval)
                {
                    ShowHUD("Lunch Pail: Still drawing stamina food from health compartment!", isError: false);
                    _staminaFallbackReminderCooldown = 0;
                }
            }

            FoodHelper.SilentConsume(fallbackNext, player);
            _monitor.Log($"[LunchPail] Fallback stamina consume: {fallbackNext.DisplayName}.", LogLevel.Trace);
        }

        // ----------------------------------------------------------------
        // Private: Health check
        // ----------------------------------------------------------------

        private void CheckHealth(Farmer player, LunchPailData data, Config config)
        {
            var foods = ResolveFoods(player, data.HealthCompartment, byStamina: false, config);

            if (foods.Count > 0)
            {
                var next = foods.First();
                if (FoodHelper.ShouldTriggerHealth(next, player, config.HealthOffset))
                {
                    FoodHelper.SilentConsume(next, player);
                    _monitor.Log($"[LunchPail] Auto-consumed {next.DisplayName} for health.", LogLevel.Trace);
                    CheckHealthSupplyWarning(player, data);
                }

                // Reset fallback flag only when primary compartment has items,
                // regardless of whether the trigger fired this tick.
                _healthFallbackNotified = false;
                _healthFallbackReminderCooldown = 0;
                return;
            }

            // Health compartment empty — attempt fallback to stamina compartment
            var fallbackFoods = ResolveFoods(player, data.StaminaCompartment, byStamina: false, config);
            if (fallbackFoods.Count == 0) return;

            var fallbackNext = fallbackFoods.First();
            if (!FoodHelper.ShouldTriggerHealth(fallbackNext, player, config.HealthOffset)) return;

            if (!_healthFallbackNotified)
            {
                ShowHUD("Lunch Pail: Health food depleted! Drawing from stamina compartment.", isError: true);
                _healthFallbackNotified = true;
                _healthFallbackReminderCooldown = 0;
            }
            else
            {
                _healthFallbackReminderCooldown++;
                if (_healthFallbackReminderCooldown >= FallbackReminderInterval)
                {
                    ShowHUD("Lunch Pail: Still drawing health food from stamina compartment!", isError: false);
                    _healthFallbackReminderCooldown = 0;
                }
            }

            FoodHelper.SilentConsume(fallbackNext, player);
            _monitor.Log($"[LunchPail] Fallback health consume: {fallbackNext.DisplayName}.", LogLevel.Trace);
        }

        // ----------------------------------------------------------------
        // Private: Supply warnings
        // ----------------------------------------------------------------

        private void CheckStaminaSupplyWarning(Farmer player, LunchPailData data)
        {
            int remaining = CountRemainingTagged(player, data.StaminaCompartment);
            if (remaining == 1)
                ShowHUD("Lunch Pail: Stamina food supply is low!", isError: false);
            else if (remaining == 0)
                ShowHUD("Lunch Pail: Stamina compartment is empty!", isError: true);
        }

        private void CheckHealthSupplyWarning(Farmer player, LunchPailData data)
        {
            int remaining = CountRemainingTagged(player, data.HealthCompartment);
            if (remaining == 1)
                ShowHUD("Lunch Pail: Health food supply is low!", isError: false);
            else if (remaining == 0)
                ShowHUD("Lunch Pail: Health compartment is empty!", isError: true);
        }

        // ----------------------------------------------------------------
        // Private: Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Resolves tagged food items from the player's actual inventory,
        /// filtered to edible items, sorted per config.
        /// </summary>
        private List<SObject> ResolveFoods(
            Farmer player,
            List<LunchPailData.FoodTag> tags,
            bool byStamina,
            Config config)
        {
            var found = new List<SObject>();
            foreach (var tag in tags)
            {
                var item = FoodHelper.FindTaggedItemInInventory(player, tag);
                if (item != null && FoodHelper.IsEdible(item))
                    found.Add(item);
            }
            return FoodHelper.SortFoods(found, config.SortOrder, byStamina);
        }

        /// <summary>
        /// Counts how many distinct tagged item stacks from a compartment
        /// remain in inventory. Warning fires when only 1 stack type remains.
        /// </summary>
        private int CountRemainingTagged(Farmer player, List<LunchPailData.FoodTag> tags)
        {
            int count = 0;
            foreach (var tag in tags)
            {
                if (FoodHelper.FindTaggedItemInInventory(player, tag) != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Displays a HUD message. Error type shows red, standard shows yellow.
        /// </summary>
        private static void ShowHUD(string message, bool isError)
        {
            int messageType = isError
                ? HUDMessage.error_type
                : HUDMessage.achievement_type;

            Game1.addHUDMessage(new HUDMessage(message, messageType));
        }
    }
}
