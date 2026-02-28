// Config.cs
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// GMCM-backed configuration for Lunch Pail.
    /// </summary>
    public class Config
    {
        // --- Target Fill (replaces legacy Offset) ---

        /// <summary>
        /// Stamina target fill as a percentage of max stamina (1-100).
        /// Auto-consumption triggers when eating the next food item would
        /// restore stamina to at least this percentage of full.
        /// Default 90 = eat when stamina is low enough that eating brings you back to ~90%.
        /// </summary>
        public int StaminaTargetFill { get; set; } = 90;

        /// <summary>
        /// Health target fill as a percentage of max health (1-100).
        /// Auto-consumption triggers when eating the next food item would
        /// restore health to at least this percentage of full.
        /// Default 90 = eat when health is low enough that eating brings you back to ~90%.
        /// </summary>
        public int HealthTargetFill { get; set; } = 90;

        // --- Floor Triggers ---

        /// <summary>
        /// When true, the Lunch Pail will always consume a stamina food
        /// if stamina drops to or below 25%, regardless of food value.
        /// </summary>
        public bool UseStaminaFloor { get; set; } = false;

        /// <summary>
        /// When true, the Lunch Pail will always consume a health food
        /// if health drops to or below 25%, regardless of food value.
        /// </summary>
        public bool UseHealthFloor { get; set; } = false;

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

        // --- Budget Adjustment ---

        /// <summary>
        /// When true, MaxServings for each budgeted item is automatically lowered
        /// to match the actual inventory stack when a deficit is detected mid-day.
        /// When false, a HUD alert fires instead and deficit rows are highlighted
        /// in red inside the Lunch Pail UI.
        /// </summary>
        public bool AutoAdjustBudget { get; set; } = true;

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
