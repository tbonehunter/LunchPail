// LunchPailData.cs
using System.Collections.Generic;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// Persistent mod data for the Lunch Pail system.
    /// Stored in the player's save data via SMAPI's data API.
    /// </summary>
    public class LunchPailData
    {
        /// <summary>
        /// Whether the player has crafted and activated the Lunch Pail.
        /// </summary>
        public bool HasLunchPail { get; set; } = false;

        /// <summary>
        /// Food items designated for the stamina compartment.
        /// Tracked by item ID and quality rather than inventory slot
        /// to survive inventory reorganization.
        /// </summary>
        public List<FoodTag> StaminaCompartment { get; set; } = new();

        /// <summary>
        /// Food items designated for the health compartment.
        /// Tracked by item ID and quality rather than inventory slot
        /// to survive inventory reorganization.
        /// </summary>
        public List<FoodTag> HealthCompartment { get; set; } = new();

        /// <summary>
        /// Identifies a tagged food item in a Lunch Pail compartment.
        /// Tracks item by qualified ID and quality rather than inventory slot
        /// to survive inventory reorganization.
        /// </summary>
        public class FoodTag
        {
            /// <summary>
            /// The qualified item ID (e.g., "(O)196" for Salad).
            /// </summary>
            public string ItemId { get; set; } = string.Empty;

            /// <summary>
            /// Item quality: 0=normal, 1=silver, 2=gold, 4=iridium.
            /// </summary>
            public int Quality { get; set; } = 0;

            /// <summary>
            /// Display name cached for UI purposes.
            /// </summary>
            public string DisplayName { get; set; } = string.Empty;

            /// <summary>
            /// Maximum number of times the pail may auto-consume this item per day.
            /// Defaults to int.MaxValue (unlimited) to preserve behaviour for existing saves.
            /// Resets via ConsumptionManager.ResetSession each morning.
            /// </summary>
            public int MaxServings { get; set; } = int.MaxValue;

            /// <summary>
            /// For preserved-ingredient items (jelly, pickle, wine, roe, etc.) this holds
            /// the ingredient's unqualified item ID (e.g. "638" for cherry jelly).
            /// Null for all normal food items that have no preserved ingredient.
            /// Captured at assignment time from SObject.preservedParentSheetIndex.
            /// Backwards-compatible: old saves without this field deserialise to null.
            /// </summary>
            public string? PreservedItemId { get; set; } = null;
        }
    }
}
