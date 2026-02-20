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
                name: () => "Stamina Offset (%)",
                tooltip: () =>
                    "How far below the food's stamina restore value (as % of max stamina) " +
                    "your stamina must drop before auto-consumption triggers.\n" +
                    "0 = eat as soon as you'd benefit from the full restore.\n" +
                    "Higher values wait until you're deeper in the deficit.\n\n" +
                    "WARNING: Values above 50% may leave you vulnerable to a death strike " +
                    "before the Lunch Pail can respond. Not recommended in deeper mine levels.",
                getValue: () => getConfig().StaminaOffset,
                setValue: val => getConfig().StaminaOffset = val,
                min: 0,
                max: 100,
                interval: 1
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
                name: () => "Health Offset (%)",
                tooltip: () =>
                    "How far below the food's health restore value (as % of max health) " +
                    "your health must drop before auto-consumption triggers.\n" +
                    "0 = eat as soon as you'd benefit from the full restore.\n" +
                    "Higher values wait until you're deeper in the deficit.\n\n" +
                    "WARNING: Values above 50% may leave you vulnerable to a death strike " +
                    "before the Lunch Pail can respond. Not recommended in deeper mine levels.",
                getValue: () => getConfig().HealthOffset,
                setValue: val => getConfig().HealthOffset = val,
                min: 0,
                max: 100,
                interval: 1
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
