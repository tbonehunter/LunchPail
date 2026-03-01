// ConfigMenu.cs
using System;
using StardewModdingAPI;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// Registers Lunch Pail settings with Generic Mod Config Menu.
    /// Separated from ModEntry to keep registration logic self-contained.
    /// GMCM is optional — if not installed the mod runs with default settings.
    /// </summary>
    public static class ConfigMenu
    {
        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Attempts to register with GMCM if it is loaded.
        /// Call from ModEntry.OnGameLaunched.
        /// </summary>
        public static void Register(
            IModHelper helper,
            IManifest manifest,
            Func<Config> getConfig,
            Action<Config> saveConfig)
        {
            var gmcm = helper.ModRegistry
                .GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

            if (gmcm is null)
                return;

            var t = helper.Translation;

            gmcm.Register(
                mod: manifest,
                reset: () => saveConfig(new Config()),
                save: () => saveConfig(getConfig())
            );

            // ----------------------------------------------------------------
            // Stamina section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => t.Get("config.section.stamina")
            );

            gmcm.AddNumberOption(
                mod: manifest,
                name: () => t.Get("config.staminaTargetFill.name"),
                tooltip: () => t.Get("config.staminaTargetFill.tooltip"),
                getValue: () => getConfig().StaminaTargetFill,
                setValue: val => getConfig().StaminaTargetFill = val,
                min: 1,
                max: 100,
                interval: 1
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => t.Get("config.useStaminaFloor.name"),
                tooltip: () => t.Get("config.useStaminaFloor.tooltip"),
                getValue: () => getConfig().UseStaminaFloor,
                setValue: val => getConfig().UseStaminaFloor = val
            );

            // ----------------------------------------------------------------
            // Health section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => t.Get("config.section.health")
            );

            gmcm.AddNumberOption(
                mod: manifest,
                name: () => t.Get("config.healthTargetFill.name"),
                tooltip: () => t.Get("config.healthTargetFill.tooltip"),
                getValue: () => getConfig().HealthTargetFill,
                setValue: val => getConfig().HealthTargetFill = val,
                min: 1,
                max: 100,
                interval: 1
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => t.Get("config.useHealthFloor.name"),
                tooltip: () => t.Get("config.useHealthFloor.tooltip"),
                getValue: () => getConfig().UseHealthFloor,
                setValue: val => getConfig().UseHealthFloor = val
            );

            // ----------------------------------------------------------------
            // Food selection section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => t.Get("config.section.foodSelection")
            );

            gmcm.AddTextOption(
                mod: manifest,
                name: () => t.Get("config.sortOrder.name"),
                tooltip: () => t.Get("config.sortOrder.tooltip"),
                getValue: () => getConfig().SortOrder.ToString(),
                setValue: val => getConfig().SortOrder = Enum.Parse<FoodSortOrder>(val),
                allowedValues: new[] { "LowestFirst", "HighestFirst", "Random" },
                formatAllowedValue: val => val switch
                {
                    "LowestFirst"  => t.Get("config.sortOrder.lowestFirst"),
                    "HighestFirst" => t.Get("config.sortOrder.highestFirst"),
                    "Random"       => t.Get("config.sortOrder.random"),
                    _              => val
                }
            );

            // ----------------------------------------------------------------
            // Budget Adjustment section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => t.Get("config.section.budgetAdjustment")
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => t.Get("config.autoAdjustBudget.name"),
                tooltip: () => t.Get("config.autoAdjustBudget.tooltip"),
                getValue: () => getConfig().AutoAdjustBudget,
                setValue: val => getConfig().AutoAdjustBudget = val
            );

            // ----------------------------------------------------------------
            // Controls section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => t.Get("config.section.controls")
            );

            gmcm.AddKeybindList(
                mod: manifest,
                name: () => t.Get("config.openLunchPailKey.name"),
                tooltip: () => t.Get("config.openLunchPailKey.tooltip"),
                getValue: () => getConfig().OpenLunchPailKey,
                setValue: val => getConfig().OpenLunchPailKey = val
            );
        }
    }
}
