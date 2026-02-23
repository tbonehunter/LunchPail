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
            CheckDeficits(player, data, config);
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
        /// Returns true when the given tag's item is currently in a deficit state
        /// (actual inventory below combined remaining budget). Used by LunchPailUI
        /// to highlight deficit rows in alert-only mode.
        /// </summary>
        public bool IsInDeficit(LunchPailData.FoodTag tag) =>
            _deficitNotified.Contains(GetTagKey(tag));

        /// <summary>
        /// Returns a stable string key for a FoodTag, used to key the consumed-today dictionary.
        /// </summary>
        private static string GetTagKey(LunchPailData.FoodTag tag) =>
            tag.ItemId + "_" + tag.Quality;

        /// <summary>
        /// Checks all tagged items across both compartments. When a real inventory
        /// stack has fallen below the combined remaining budget, either auto-adjusts
        /// MaxServings (AutoAdjustBudget=true) or fires a HUD alert and marks the
        /// item as in-deficit for UI highlighting (AutoAdjustBudget=false).
        /// </summary>
        private void CheckDeficits(Farmer player, LunchPailData data, Config config)
        {
            // Aggregate remaining budget per unique item key across both compartments.
            // Keep per-compartment tag lists so auto-adjust can reduce them independently.
            var byKey = new Dictionary<string, (
                int totalRemaining,
                LunchPailData.FoodTag sampleTag,
                List<LunchPailData.FoodTag> staminaTags,
                List<LunchPailData.FoodTag> healthTags)>();

            foreach (bool isStamina in new[] { true, false })
            {
                var compartment = isStamina ? data.StaminaCompartment : data.HealthCompartment;
                foreach (var tag in compartment)
                {
                    if (tag.MaxServings == int.MaxValue) continue;

                    var key      = GetTagKey(tag);
                    int consumed  = _consumedToday.GetValueOrDefault(key);
                    int remaining = tag.MaxServings - consumed;
                    if (remaining <= 0) continue;

                    if (byKey.TryGetValue(key, out var existing))
                    {
                        if (isStamina) existing.staminaTags.Add(tag);
                        else           existing.healthTags.Add(tag);
                        byKey[key] = (existing.totalRemaining + remaining,
                            existing.sampleTag, existing.staminaTags, existing.healthTags);
                    }
                    else
                    {
                        var stList = new List<LunchPailData.FoodTag>();
                        var hlList = new List<LunchPailData.FoodTag>();
                        if (isStamina) stList.Add(tag); else hlList.Add(tag);
                        byKey[key] = (remaining, tag, stList, hlList);
                    }
                }
            }

            foreach (var kvp in byKey)
            {
                var key            = kvp.Key;
                int totalRemaining = kvp.Value.totalRemaining;
                var sampleTag      = kvp.Value.sampleTag;
                var staminaTags    = kvp.Value.staminaTags;
                var healthTags     = kvp.Value.healthTags;

                var item        = FoodHelper.FindTaggedItemInInventory(player, sampleTag);
                int actualStack = item?.Stack ?? 0;

                _monitor.Log(
                    $"[LunchPail][Deficit] {sampleTag.DisplayName} key={key}: totalRemaining={totalRemaining} actualStack={actualStack} deficit={actualStack < totalRemaining}.",
                    LogLevel.Trace);

                if (actualStack < totalRemaining)
                {
                    if (config.AutoAdjustBudget)
                    {
                        // Distribute shortage: health absorbs 50% (floor), stamina absorbs the rest
                        // (stamina naturally takes the extra 1 on odd shortages — health is protected).
                        int shortage         = totalRemaining - actualStack;
                        int healthReduction  = shortage / 2;
                        int staminaReduction = shortage - healthReduction;

                        ApplyBudgetReduction(healthTags,  healthReduction,  data.HealthCompartment);
                        ApplyBudgetReduction(staminaTags, staminaReduction, data.StaminaCompartment);

                        // Fire HUD once per occurrence; re-arms on next day (ResetSession clears the set).
                        if (!_deficitNotified.Contains(key))
                        {
                            ShowHUD(
                                $"Your {sampleTag.DisplayName} Lunch Pail budget has been adjusted to match your available supply.",
                                isError: false);
                            _deficitNotified.Add(key);
                            _monitor.Log(
                                $"[LunchPail] Auto-adjusted budget: {sampleTag.DisplayName} shortage={shortage} (healthReduction={healthReduction}, staminaReduction={staminaReduction}).",
                                LogLevel.Trace);
                        }
                    }
                    else
                    {
                        // Alert-only: HUD fires once; UI will highlight the row red via IsInDeficit().
                        if (!_deficitNotified.Contains(key))
                        {
                            ShowHUD(
                                $"Your {sampleTag.DisplayName} supply has dropped below your Lunch Pail budget.",
                                isError: false);
                            _deficitNotified.Add(key);
                            _monitor.Log(
                                $"[LunchPail] Deficit alert fired: {sampleTag.DisplayName} stack={actualStack} totalRemainingBudget={totalRemaining}.",
                                LogLevel.Trace);
                        }
                    }
                }
                else
                {
                    // Deficit resolved — clear flag so next drop fires a fresh alert.
                    if (_deficitNotified.Remove(key))
                        _monitor.Log(
                            $"[LunchPail][Deficit] {sampleTag.DisplayName} key={key}: deficit cleared (stack={actualStack} >= totalRemaining={totalRemaining}).",
                            LogLevel.Trace);
                }
            }
        }

        /// <summary>
        /// Reduces MaxServings on the given tags by the specified total amount,
        /// consuming from each tag sequentially and clamping to consumed-today
        /// so the budget never drops below what has already been auto-eaten.
        /// </summary>
        private void ApplyBudgetReduction(
            List<LunchPailData.FoodTag> tags, int reduction,
            List<LunchPailData.FoodTag> compartment)
        {
            var toRemove = new List<LunchPailData.FoodTag>();
            foreach (var tag in tags)
            {
                if (reduction <= 0) break;
                int consumed         = _consumedToday.GetValueOrDefault(GetTagKey(tag));
                int currentRemaining = tag.MaxServings - consumed;
                int canReduce        = Math.Max(0, Math.Min(currentRemaining, reduction));
                tag.MaxServings     -= canReduce;
                reduction           -= canReduce;
                // A zeroed tag is useless and causes ghost rows in the UI — remove it.
                if (tag.MaxServings <= 0)
                    toRemove.Add(tag);
            }
            foreach (var tag in toRemove)
                compartment.Remove(tag);
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
