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

        // Per-day consumption counters keyed by "ItemId_Quality".
        // Incremented on each auto-consume; cleared each morning by ResetSession.
        private readonly Dictionary<string, int> _consumedToday = new();

        // Tags for which a deficit HUD alert has already fired today.
        // Cleared each morning by ResetSession alongside the other flags.
        private readonly HashSet<string> _deficitNotified = new();

        // Notification flags — each fires once per day (reset by ResetSession).
        private bool _staminaLowNotified       = false;
        private bool _staminaExhaustedNotified = false;
        private bool _healthLowNotified        = false;
        private bool _healthExhaustedNotified  = false;
        private bool _allLowNotified           = false;  // fallback compartment running low
        private bool _allExhaustedNotified     = false;  // both compartments empty

        private const int HudDuration  = 10000; // 10 seconds in ms
        private const int TickInterval = 30;    // twice per second

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
            _consumedToday.Clear();
            _deficitNotified.Clear();

            _staminaLowNotified       = false;
            _staminaExhaustedNotified = false;
            _healthLowNotified        = false;
            _healthExhaustedNotified  = false;
            _allLowNotified           = false;
            _allExhaustedNotified     = false;
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
            CheckDeficits(player, data);
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
                    RecordConsumptionByItem(next, data.StaminaCompartment);
                    _monitor.Log($"[LunchPail] Auto-consumed {next.DisplayName} for stamina.", LogLevel.Trace);

                    // Warn once if stamina supply is now low
                    if (!_staminaLowNotified &&
                        CountRemainingServings(player, data.StaminaCompartment) <= config.LowSupplyThreshold)
                    {
                        ShowHUD("Your stamina food supply is low.", isError: false);
                        _staminaLowNotified = true;
                    }
                }
                return;
            }

            // Primary stamina compartment empty — attempt fallback to health compartment
            var fallbackFoods = ResolveFoods(player, data.HealthCompartment, byStamina: true, config);

            if (fallbackFoods.Count == 0)
            {
                // Both compartments empty
                if (!_allExhaustedNotified)
                {
                    ShowHUD("Your food supply is exhausted.", isError: true);
                    _allExhaustedNotified = true;
                }
                return;
            }

            var fallbackNext = fallbackFoods.First();
            if (!FoodHelper.ShouldTriggerStamina(fallbackNext, player, config.StaminaOffset)) return;

            // Notify once that stamina compartment is exhausted and fallback is active
            if (!_staminaExhaustedNotified)
            {
                ShowHUD("Your stamina supply is exhausted, now consuming from the health supply.", isError: true);
                _staminaExhaustedNotified = true;
            }

            FoodHelper.SilentConsume(fallbackNext, player);
            RecordConsumptionByItem(fallbackNext, data.HealthCompartment);
            _monitor.Log($"[LunchPail] Fallback stamina consume: {fallbackNext.DisplayName}.", LogLevel.Trace);

            // Warn once if the fallback compartment is also running low
            if (!_allLowNotified &&
                CountRemainingServings(player, data.HealthCompartment) <= config.LowSupplyThreshold)
            {
                ShowHUD("Your food supply will soon run out.", isError: false);
                _allLowNotified = true;
            }
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
                    RecordConsumptionByItem(next, data.HealthCompartment);
                    _monitor.Log($"[LunchPail] Auto-consumed {next.DisplayName} for health.", LogLevel.Trace);

                    // Warn once if health supply is now low
                    if (!_healthLowNotified &&
                        CountRemainingServings(player, data.HealthCompartment) <= config.LowSupplyThreshold)
                    {
                        ShowHUD("Your health food supply is low.", isError: false);
                        _healthLowNotified = true;
                    }
                }
                return;
            }

            // Primary health compartment empty — attempt fallback to stamina compartment
            var fallbackFoods = ResolveFoods(player, data.StaminaCompartment, byStamina: false, config);

            if (fallbackFoods.Count == 0)
            {
                // Both compartments empty
                if (!_allExhaustedNotified)
                {
                    ShowHUD("Your food supply is exhausted.", isError: true);
                    _allExhaustedNotified = true;
                }
                return;
            }

            var fallbackNext = fallbackFoods.First();
            if (!FoodHelper.ShouldTriggerHealth(fallbackNext, player, config.HealthOffset)) return;

            // Notify once that health compartment is exhausted and fallback is active
            if (!_healthExhaustedNotified)
            {
                ShowHUD("Your health supply is exhausted, now consuming from the stamina supply.", isError: true);
                _healthExhaustedNotified = true;
            }

            FoodHelper.SilentConsume(fallbackNext, player);
            RecordConsumptionByItem(fallbackNext, data.StaminaCompartment);
            _monitor.Log($"[LunchPail] Fallback health consume: {fallbackNext.DisplayName}.", LogLevel.Trace);

            // Warn once if the fallback compartment is also running low
            if (!_allLowNotified &&
                CountRemainingServings(player, data.StaminaCompartment) <= config.LowSupplyThreshold)
            {
                ShowHUD("Your food supply will soon run out.", isError: false);
                _allLowNotified = true;
            }
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
                // Skip this tag if today's consumption budget has been reached.
                if (IsBudgetExhausted(tag)) continue;

                var item = FoodHelper.FindTaggedItemInInventory(player, tag);
                if (item != null && FoodHelper.IsEdible(item))
                    found.Add(item);
            }
            return FoodHelper.SortFoods(found, config.SortOrder, byStamina);
        }

        /// <summary>
        /// Returns how many times the given tag has been auto-consumed today.
        /// Used by LunchPailUI to flag rows where actual inventory has fallen
        /// below the remaining daily budget.
        /// </summary>
        public int GetConsumedToday(LunchPailData.FoodTag tag) =>
            _consumedToday.GetValueOrDefault(GetTagKey(tag));

        /// <summary>
        /// Returns a stable string key for a FoodTag, used to key the consumed-today dictionary.
        /// </summary>
        private static string GetTagKey(LunchPailData.FoodTag tag) =>
            tag.ItemId + "_" + tag.Quality;

        /// <summary>
        /// Checks all tagged items across both compartments and fires a one-time-per-day
        /// HUD alert for any whose real inventory stack has fallen below the remaining
        /// daily budget (i.e. the player removed items mid-day that the pail was counting on).
        /// </summary>
        private void CheckDeficits(Farmer player, LunchPailData data)
        {
            CheckCompartmentDeficits(player, data.StaminaCompartment);
            CheckCompartmentDeficits(player, data.HealthCompartment);
        }

        private void CheckCompartmentDeficits(Farmer player, List<LunchPailData.FoodTag> compartment)
        {
            foreach (var tag in compartment)
            {
                var key = GetTagKey(tag);

                // Unlimited tags have no budget to fall below.
                if (tag.MaxServings == int.MaxValue)
                {
                    _monitor.Log($"[LunchPail][Deficit] {tag.DisplayName} key={key}: MaxServings=unlimited, skipping.", LogLevel.Trace);
                    continue;
                }

                int consumed  = _consumedToday.GetValueOrDefault(key);
                int remaining = tag.MaxServings - consumed;

                if (remaining <= 0)
                {
                    _monitor.Log($"[LunchPail][Deficit] {tag.DisplayName} key={key}: budget exhausted (consumed={consumed} MaxServings={tag.MaxServings}), skipping.", LogLevel.Trace);
                    // Budget fully used — clear any deficit flag so it can re-arm next time.
                    _deficitNotified.Remove(key);
                    continue;
                }

                var item        = FoodHelper.FindTaggedItemInInventory(player, tag);
                int actualStack = item?.Stack ?? 0;

                _monitor.Log($"[LunchPail][Deficit] {tag.DisplayName} key={key}: MaxServings={tag.MaxServings} consumed={consumed} remaining={remaining} actualStack={actualStack} deficit={actualStack < remaining}.", LogLevel.Trace);

                if (actualStack < remaining)
                {
                    if (!_deficitNotified.Contains(key))
                    {
                        ShowHUD(
                            $"Your {tag.DisplayName} supply has dropped below your Lunch Pail budget.",
                            isError: false);
                        _deficitNotified.Add(key);
                        _monitor.Log(
                            $"[LunchPail] Deficit alert fired: {tag.DisplayName} stack={actualStack} remaining budget={remaining}.",
                            LogLevel.Trace);
                    }
                }
                else
                {
                    // Deficit resolved (stack recovered) — clear flag so another drop will re-alert.
                    if (_deficitNotified.Remove(key))
                        _monitor.Log($"[LunchPail][Deficit] {tag.DisplayName} key={key}: deficit cleared (stack={actualStack} >= remaining={remaining}).", LogLevel.Trace);
                }
            }
        }

        /// <summary>
        /// Returns true if the tag's daily consumption budget is fully used.
        /// Tags with MaxServings == int.MaxValue are never exhausted.
        /// </summary>
        private bool IsBudgetExhausted(LunchPailData.FoodTag tag)
        {
            if (tag.MaxServings == int.MaxValue) return false;
            return _consumedToday.GetValueOrDefault(GetTagKey(tag)) >= tag.MaxServings;
        }

        /// <summary>
        /// Finds the tag in <paramref name="compartment"/> matching <paramref name="item"/>
        /// and increments its consumed-today counter.
        /// </summary>
        private void RecordConsumptionByItem(SObject item, List<LunchPailData.FoodTag> compartment)
        {
            var tag = compartment.FirstOrDefault(t =>
                t.ItemId == item.QualifiedItemId && t.Quality == item.Quality);
            if (tag == null || tag.MaxServings == int.MaxValue) return;

            var key = GetTagKey(tag);
            _consumedToday[key] = _consumedToday.GetValueOrDefault(key) + 1;
        }

        /// <summary>
        /// Counts the total number of servings (sum of stack sizes) of all
        /// tagged items from a compartment that remain in the player's inventory.
        /// </summary>
        private int CountRemainingServings(Farmer player, List<LunchPailData.FoodTag> tags)
        {
            int total = 0;
            foreach (var tag in tags)
            {
                var item = FoodHelper.FindTaggedItemInInventory(player, tag);
                if (item != null)
                    total += item.Stack;
            }
            return total;
        }

        /// <summary>
        /// Displays a HUD message with a 10-second duration.
        /// Error type shows red, standard shows yellow.
        /// </summary>
        private static void ShowHUD(string message, bool isError)
        {
            int messageType = isError
                ? HUDMessage.error_type
                : HUDMessage.achievement_type;

            Game1.addHUDMessage(new HUDMessage(message, messageType) { timeLeft = HudDuration });
        }
    }
}
