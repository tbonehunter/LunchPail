// FoodHelper.cs
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using SObject = StardewValley.Object;

namespace TBoneHunter.LunchPail.Helpers
{
    /// <summary>
    /// Utility methods for evaluating, sorting, and selecting food items
    /// for the Lunch Pail auto-consumption system.
    /// </summary>
    public static class FoodHelper
    {
        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true if the given item is edible (can restore health or stamina).
        /// </summary>
        public static bool IsEdible(Item item)
        {
            return item is SObject obj && obj.Edibility > 0;
        }

        /// <summary>
        /// Returns the stamina (energy) value this item will restore,
        /// respecting item quality via the game's own calculation.
        /// </summary>
        public static float GetStaminaRestore(SObject obj)
        {
            return obj.staminaRecoveredOnConsumption();
        }

        /// <summary>
        /// Returns the health value this item will restore,
        /// respecting item quality via the game's own calculation.
        /// </summary>
        public static float GetHealthRestore(SObject obj)
        {
            return obj.healthRecoveredOnConsumption();
        }

        /// <summary>
        /// Returns the stamina restore value as a percentage of the
        /// player's current maximum stamina.
        /// </summary>
        public static float GetStaminaRestorePercent(SObject obj, Farmer player)
        {
            float max = player.MaxStamina;
            if (max <= 0) return 0f;
            return GetStaminaRestore(obj) / max * 100f;
        }

        /// <summary>
        /// Returns the health restore value as a percentage of the
        /// player's current maximum health.
        /// </summary>
        public static float GetHealthRestorePercent(SObject obj, Farmer player)
        {
            float max = player.maxHealth;
            if (max <= 0) return 0f;
            return GetHealthRestore(obj) / max * 100f;
        }

        /// <summary>
        /// Returns the player's current stamina as a percentage of maximum.
        /// </summary>
        public static float GetCurrentStaminaPercent(Farmer player)
        {
            float max = player.MaxStamina;
            if (max <= 0) return 0f;
            return player.Stamina / max * 100f;
        }

        /// <summary>
        /// Returns the player's current health as a percentage of maximum.
        /// </summary>
        public static float GetCurrentHealthPercent(Farmer player)
        {
            float max = player.maxHealth;
            if (max <= 0) return 0f;
            return (float)player.health / max * 100f;
        }

        /// <summary>
        /// Returns true if the player's stamina has dropped to or below
        /// the trigger threshold for the given food item and offset setting.
        /// Trigger point = 100% - food's restore% - offset%
        /// At offset 0, fires exactly when eating the food would fill
        /// the player back to full. Higher offsets wait deeper into the deficit.
        /// </summary>
        public static bool ShouldTriggerStamina(SObject obj, Farmer player, int offsetPercent)
        {
            float restorePercent = GetStaminaRestorePercent(obj, player);
            float triggerPoint = Math.Max(0f, 100f - restorePercent - offsetPercent);
            return GetCurrentStaminaPercent(player) <= triggerPoint;
        }

        /// <summary>
        /// Returns true if the player's health has dropped to or below
        /// the trigger threshold for the given food item and offset setting.
        /// Trigger point = 100% - food's restore% - offset%
        /// At offset 0, fires exactly when eating the food would fill
        /// the player back to full. Higher offsets wait deeper into the deficit.
        /// </summary>
        public static bool ShouldTriggerHealth(SObject obj, Farmer player, int offsetPercent)
        {
            float restorePercent = GetHealthRestorePercent(obj, player);
            float triggerPoint = Math.Max(0f, 100f - restorePercent - offsetPercent);
            return GetCurrentHealthPercent(player) <= triggerPoint;
        }

        /// <summary>
        /// Sorts a list of inventory items according to the configured
        /// sort order, using the relevant stat for the compartment.
        /// </summary>
        public static List<SObject> SortFoods(
            List<SObject> foods,
            FoodSortOrder sortOrder,
            bool byStamina)
        {
            return sortOrder switch
            {
                FoodSortOrder.HighestFirst => byStamina
                    ? foods.OrderByDescending(f => GetStaminaRestore(f)).ToList()
                    : foods.OrderByDescending(f => GetHealthRestore(f)).ToList(),

                FoodSortOrder.Random => foods
                    .OrderBy(_ => Game1.random.Next()).ToList(),

                // LowestFirst is the default
                _ => byStamina
                    ? foods.OrderBy(f => GetStaminaRestore(f)).ToList()
                    : foods.OrderBy(f => GetHealthRestore(f)).ToList(),
            };
        }

        /// <summary>
        /// Finds the first item in the player's inventory that matches
        /// the given FoodTag (item ID and quality).
        /// Returns null if not found.
        /// </summary>
        public static SObject? FindTaggedItemInInventory(
            Farmer player,
            LunchPailData.FoodTag tag)
        {
            foreach (var item in player.Items)
            {
                if (item is SObject obj
                    && obj.QualifiedItemId == tag.ItemId
                    && obj.Quality == tag.Quality)
                {
                    return obj;
                }
            }
            return null;
        }

        /// <summary>
        /// Silently consumes one unit of the given item from the player's
        /// inventory, applying health, stamina, and any food buffs directly
        /// without triggering the eating animation or cutscene.
        /// Buffs (speed, luck, mining, etc.) are applied to preserve full
        /// food value — e.g. Spicy Eel's speed buff is not silently dropped.
        /// </summary>
        public static void SilentConsume(SObject obj, Farmer player)
        {
            float stamina = GetStaminaRestore(obj);
            int health = (int)GetHealthRestore(obj);

            player.Stamina = System.Math.Min(
                player.Stamina + stamina,
                player.MaxStamina);

            player.health = System.Math.Min(
                player.health + health,
                player.maxHealth);

            // Apply food buffs (speed, luck, mining, etc.)
            // Mirrors what the game does internally when eating,
            // without triggering the animation.
            var buffs = obj.GetFoodOrDrinkBuffs();
            if (buffs != null)
            {
                foreach (var buff in buffs)
                {
                    player.applyBuff(buff);
                }
            }

            // Reduce stack or remove item entirely
            obj.Stack--;
            if (obj.Stack <= 0)
                player.removeItemFromInventory(obj);
        }
    }
}
