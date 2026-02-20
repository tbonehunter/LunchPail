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
        }
    }
}
