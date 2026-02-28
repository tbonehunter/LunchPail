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
                text: () => "Stamina Compartment"
            );

            gmcm.AddNumberOption(
                mod: manifest,
                name: () => "Stamina Target Fill (%)",
                tooltip: () =>
                    "Auto-consume stamina food when eating it would restore your stamina\n" +
                    "to at least this percentage of full.\n" +
                    "Example: 90 means eat when stamina is low enough that the food\n" +
                    "would bring you back to 90% or more of full.\n\n" +
                    "Note: if the food's restore value exceeds this target, the trigger\n" +
                    "point becomes 0% (never fires from this setting alone).\n" +
                    "Enable 'Use Base Stamina Level' below to protect against that case.",
                getValue: () => getConfig().StaminaTargetFill,
                setValue: val => getConfig().StaminaTargetFill = val,
                min: 1,
                max: 100,
                interval: 1
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => "Use Base Stamina Level",
                tooltip: () =>
                    "Never lets your Stamina drop below 25% without consuming food,\n" +
                    "to avoid exhaustion from sudden depletion.\n" +
                    "This fires regardless of the food's restore value and overrides\n" +
                    "the Target Fill setting when stamina is critically low.",
                getValue: () => getConfig().UseStaminaFloor,
                setValue: val => getConfig().UseStaminaFloor = val
            );

            // ----------------------------------------------------------------
            // Health section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => "Health Compartment"
            );

            gmcm.AddNumberOption(
                mod: manifest,
                name: () => "Health Target Fill (%)",
                tooltip: () =>
                    "Auto-consume health food when eating it would restore your health\n" +
                    "to at least this percentage of full.\n" +
                    "Example: 90 means eat when health is low enough that the food\n" +
                    "would bring you back to 90% or more of full.\n\n" +
                    "Note: if the food's restore value exceeds this target, the trigger\n" +
                    "point becomes 0% (never fires from this setting alone).\n" +
                    "Enable 'Use Base Health Level' below to protect against that case.",
                getValue: () => getConfig().HealthTargetFill,
                setValue: val => getConfig().HealthTargetFill = val,
                min: 1,
                max: 100,
                interval: 1
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => "Use Base Health Level",
                tooltip: () =>
                    "Never lets your Health drop below 25% without consuming food,\n" +
                    "to avoid death from sudden depletion.\n" +
                    "This fires regardless of the food's restore value and overrides\n" +
                    "the Target Fill setting when health is critically low.",
                getValue: () => getConfig().UseHealthFloor,
                setValue: val => getConfig().UseHealthFloor = val
            );

            // ----------------------------------------------------------------
            // Food selection section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => "Food Selection"
            );

            gmcm.AddTextOption(
                mod: manifest,
                name: () => "Consumption Order",
                tooltip: () =>
                    "Determines which food is consumed first within each compartment.\n" +
                    "Lowest First: conserves best food for emergencies (recommended).\n" +
                    "Highest First: maximizes immediate restore value.\n" +
                    "Random: shuffles each time.",
                getValue: () => getConfig().SortOrder.ToString(),
                setValue: val => getConfig().SortOrder = Enum.Parse<FoodSortOrder>(val),
                allowedValues: new[] { "LowestFirst", "HighestFirst", "Random" },
                formatAllowedValue: val => val switch
                {
                    "LowestFirst"  => "Lowest First",
                    "HighestFirst" => "Highest First",
                    "Random"       => "Random",
                    _              => val
                }
            );

            // ----------------------------------------------------------------
            // Budget Adjustment section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => "Budget Adjustment"
            );

            gmcm.AddBoolOption(
                mod: manifest,
                name: () => "Auto-adjust daily budget",
                tooltip: () =>
                    "When enabled, the serving budget for each assigned food is automatically\n" +
                    "reduced to match your actual inventory if supply has dropped mid-day.\n" +
                    "A HUD message will confirm the adjustment was made.\n\n" +
                    "When disabled, only a HUD alert fires and deficit rows are highlighted\n" +
                    "in red inside the Lunch Pail UI so you can adjust manually.",
                getValue: () => getConfig().AutoAdjustBudget,
                setValue: val => getConfig().AutoAdjustBudget = val
            );

            // ----------------------------------------------------------------
            // Controls section
            // ----------------------------------------------------------------

            gmcm.AddSectionTitle(
                mod: manifest,
                text: () => "Controls"
            );

            gmcm.AddKeybindList(
                mod: manifest,
                name: () => "Open Lunch Pail",
                tooltip: () => "Keybind to open the Lunch Pail management UI.",
                getValue: () => getConfig().OpenLunchPailKey,
                setValue: val => getConfig().OpenLunchPailKey = val
            );
        }
    }
}
