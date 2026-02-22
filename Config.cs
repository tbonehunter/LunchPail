// Config.cs
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// GMCM-backed configuration for Lunch Pail.
    /// Offset percentages define how far below the food's replenishment
    /// value (as a percentage of max) the relevant stat must drop before
    /// auto-consumption triggers.
    /// </summary>
    public class Config
    {
        // --- Trigger Offsets ---

        /// <summary>
        /// Stamina trigger offset as a percentage of max stamina (0-100).
        /// 0 = eat as soon as you'd benefit from the full restore value.
        /// Higher values wait until you're deeper into the deficit.
        /// </summary>
        public int StaminaOffset { get; set; } = 10;

        /// <summary>
        /// Health trigger offset as a percentage of max health (0-100).
        /// 0 = eat as soon as you'd benefit from the full restore value.
        /// Higher values wait until you're deeper into the deficit.
        /// </summary>
        public int HealthOffset { get; set; } = 10;

        // --- Food Selection Order ---

        /// <summary>
        /// Sort order for food consumption within each compartment.
        /// LowestFirst = conserve best food for later.
        /// HighestFirst = maximize immediate restore value.
        /// Random = shuffle each time.
        /// </summary>
        public FoodSortOrder SortOrder { get; set; } = FoodSortOrder.LowestFirst;

        // --- Supply Warning Threshold ---

        /// <summary>
        /// Number of servings remaining in a compartment at which a low-supply
        /// warning is shown. Edit directly in config.json if desired.
        /// </summary>
        public int LowSupplyThreshold { get; set; } = 3;

        // --- Keybind ---

        /// <summary>
        /// Keybind to open the Lunch Pail UI.
        /// </summary>
        public KeybindList OpenLunchPailKey { get; set; } = new KeybindList(SButton.L);
    }

    /// <summary>
    /// Defines the order in which food is consumed from a compartment.
    /// </summary>
    public enum FoodSortOrder
    {
        LowestFirst,
        HighestFirst,
        Random
    }
}
