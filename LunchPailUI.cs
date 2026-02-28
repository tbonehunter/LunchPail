// LunchPailUI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using TBoneHunter.LunchPail.Helpers;
using SObject = StardewValley.Object;

namespace TBoneHunter.LunchPail
{
    /// <summary>
    /// Three-panel UI for managing Lunch Pail food assignments.
    /// Left panel:   Stamina compartment (blue tint).
    /// Center panel: Unassigned inventory food (neutral).
    /// Right panel:  Health compartment (red tint).
    ///
    /// Click a center item to assign it to a compartment via dialogue.
    /// Click the X button on an assigned item to return it to unassigned.
    /// Hover any item for a tooltip showing name, stamina restore, and health restore.
    /// </summary>
    public class LunchPailUI : IClickableMenu
    {
        // ----------------------------------------------------------------
        // Layout constants
        // ----------------------------------------------------------------

        private const int PanelWidth = 300;
        private const int PanelHeight = 400;
        private const int ItemHeight = 48;
        private const int ItemIconSize = 32;
        private const int Padding = 12;
        private const int HeaderHeight = 60;
        private const int TotalWidth = PanelWidth * 3 + Padding * 4;
        private const int TotalHeight = PanelHeight + HeaderHeight;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private readonly IMonitor _monitor;
        private readonly LunchPailData _data;
        private readonly Func<Config> _config;
        private readonly ConsumptionManager _consumptionManager;

        private List<SObject> _unassignedFoods = new();
        private List<SObject> _staminaFoods = new();
        private List<LunchPailData.FoodTag> _staminaTags = new();
        private List<SObject> _healthFoods = new();
        private List<LunchPailData.FoodTag> _healthTags = new();

        private string? _tooltipText = null;

        // Set to true in exitThisMenu so receiveLeftClick can bail out
        // immediately if the base class triggers a close mid-handler.
        private bool _isClosing = false;

        // Throttles draw-time logging to the first draw only per UI open.
        private bool _hasLoggedDraw = false;

        // Counter for periodic inventory refresh while the UI is open.
        // Keeps displayed quantities in sync with auto-consumption and
        // manual eating without relying on stale SObject references.
        private int _refreshTick = 0;
        private const int UIRefreshInterval = 30; // match ConsumptionManager cadence

        // ----------------------------------------------------------------
        // Constructor
        // ----------------------------------------------------------------

        public LunchPailUI(IMonitor monitor, LunchPailData data, Func<Config> config,
            ConsumptionManager consumptionManager)
            : base(
                x: (Game1.uiViewport.Width - TotalWidth) / 2,
                y: (Game1.uiViewport.Height - TotalHeight) / 2,
                width: TotalWidth,
                height: TotalHeight,
                showUpperRightCloseButton: true)
        {
            _monitor = monitor;
            _data = data;
            _config = config;
            _consumptionManager = consumptionManager;

            // Clear the location's afterQuestion delegate when this menu closes
            // for any reason (X button, ESC, outside click). Without this, closing
            // mid-dialogue-flow leaves the farmer movement-blocked indefinitely.
            exitFunction = () =>
            {
                Game1.currentLocation.afterQuestion = null;
                _isClosing = true;
            };

            RefreshLists();
        }

        // ----------------------------------------------------------------
        // Refresh resolved item lists from inventory + tags
        // ----------------------------------------------------------------

        private void RefreshLists(bool isPeriodicUpdate = false)
        {
            var player = Game1.player;

            // Only reset the draw-log flag for intentional refreshes (UI open,
            // assignment/edit/unassign). Periodic background refreshes must not
            // reset it or the draw log would re-fire every 30 ticks.
            if (!isPeriodicUpdate)
                _hasLoggedDraw = false;

            // Build parallel (tag, SObject) pairs so the draw layer can access MaxServings
            // for each resolved item without a second lookup.
            var staminaPairs = _data.StaminaCompartment
                .Select(tag => (tag, item: FoodHelper.FindTaggedItemInInventory(player, tag)))
                .ToList();

            // Log tags that have no matching inventory item (silent drops).
            foreach (var p in staminaPairs.Where(p => p.item == null))
                if (!isPeriodicUpdate)
                    _monitor.Log(
                        $"[LunchPailUI][RefreshLists] Stamina tag not found in inventory: {p.tag.DisplayName} id={p.tag.ItemId} quality={p.tag.Quality} MaxServings={p.tag.MaxServings}",
                        LogLevel.Debug);

            staminaPairs = staminaPairs.Where(p => p.item != null).ToList();
            _staminaFoods = staminaPairs.Select(p => p.item!).ToList();
            _staminaTags  = staminaPairs.Select(p => p.tag).ToList();

            if (!isPeriodicUpdate)
            {
                foreach (var (tag, item) in staminaPairs.Select(p => (p.tag, p.item!)))
                {
                    int displayCount = tag.MaxServings == int.MaxValue ? item.Stack : Math.Min(tag.MaxServings, item.Stack);
                    string maxStr = tag.MaxServings == int.MaxValue ? "unlimited" : tag.MaxServings.ToString();
                    _monitor.Log(
                        $"[LunchPailUI][RefreshLists] Stamina: {tag.DisplayName} MaxServings={maxStr} Stack={item.Stack} -> label=\u00d7{displayCount}",
                        LogLevel.Debug);
                }
            }

            var healthPairs = _data.HealthCompartment
                .Select(tag => (tag, item: FoodHelper.FindTaggedItemInInventory(player, tag)))
                .ToList();

            foreach (var p in healthPairs.Where(p => p.item == null))
                if (!isPeriodicUpdate)
                    _monitor.Log(
                        $"[LunchPailUI][RefreshLists] Health tag not found in inventory: {p.tag.DisplayName} id={p.tag.ItemId} quality={p.tag.Quality} MaxServings={p.tag.MaxServings}",
                        LogLevel.Debug);

            healthPairs = healthPairs.Where(p => p.item != null).ToList();
            _healthFoods = healthPairs.Select(p => p.item!).ToList();
            _healthTags  = healthPairs.Select(p => p.tag).ToList();

            if (!isPeriodicUpdate)
            {
                foreach (var (tag, item) in healthPairs.Select(p => (p.tag, p.item!)))
                {
                    int displayCount = tag.MaxServings == int.MaxValue ? item.Stack : Math.Min(tag.MaxServings, item.Stack);
                    string maxStr = tag.MaxServings == int.MaxValue ? "unlimited" : tag.MaxServings.ToString();
                    _monitor.Log(
                        $"[LunchPailUI][RefreshLists] Health:   {tag.DisplayName} MaxServings={maxStr} Stack={item.Stack} -> label=\u00d7{displayCount}",
                        LogLevel.Debug);
                }
            }

            // An item only leaves the center panel when every serving in the stack is
            // already budgeted across both compartments combined.
            _unassignedFoods = player.Items
                .OfType<SObject>()
                .Where(obj =>
                {
                    if (!FoodHelper.IsEdible(obj)) return false;
                    int staminaBudget = GetCompartmentBudget(obj, _data.StaminaCompartment);
                    int healthBudget  = GetCompartmentBudget(obj, _data.HealthCompartment);
                    return staminaBudget + healthBudget < obj.Stack;
                })
                .ToList();
        }

        // ----------------------------------------------------------------
        // Update
        // ----------------------------------------------------------------

        /// <summary>
        /// Periodically re-queries inventory item references so that quantities
        /// auto-consumed or manually eaten while the UI is open are reflected
        /// immediately without waiting for a full user interaction.
        /// </summary>
        public override void update(GameTime time)
        {
            base.update(time);
            if (++_refreshTick >= UIRefreshInterval)
            {
                _refreshTick = 0;
                RefreshLists(isPeriodicUpdate: true);
            }
        }

        // ----------------------------------------------------------------
        // Draw
        // ----------------------------------------------------------------

        public override void draw(SpriteBatch b)
        {
            // Darken background
            b.Draw(Game1.fadeToBlackRect,
                new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
                Color.Black * 0.5f);

            // Window background
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                xPositionOnScreen, yPositionOnScreen, width, height,
                Color.White, 1f, false);

            // Panel bounds
            var staminaPanel = GetPanelBounds(0);
            var centerPanel = GetPanelBounds(1);
            var healthPanel = GetPanelBounds(2);

            // Panel backgrounds and headers
            DrawPanel(b, staminaPanel, Color.SteelBlue * 0.3f, "Stamina");
            DrawPanel(b, centerPanel, Color.Gray * 0.2f, "Inventory");
            DrawPanel(b, healthPanel, Color.Crimson * 0.3f, "Health");

            // Item lists
            DrawItemList(b, _staminaFoods, _staminaTags, staminaPanel);
            DrawUnassignedList(b, _unassignedFoods, centerPanel);
            DrawItemList(b, _healthFoods, _healthTags, healthPanel);

            // Log all three panels once per UI open, after every panel has been drawn.
            if (!_hasLoggedDraw)
            {
                LogPanelDraw("Stamina", _staminaFoods, _staminaTags);
                LogPanelDraw("Health",  _healthFoods,  _healthTags);
                _hasLoggedDraw = true;
            }

            // Close button
            base.draw(b);

            // Tooltip drawn last so it renders on top
            if (_tooltipText != null)
                drawToolTip(b, _tooltipText, string.Empty, null);

            drawMouse(b);
        }

        // ----------------------------------------------------------------
        // Input
        // ----------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            _monitor.Log($"[LunchPailUI] receiveLeftClick ({x},{y})", LogLevel.Trace);

            // Detect close button click before base handles it so we can guard
            // against continuing to run on a closing menu.
            bool closeClicked = upperRightCloseButton != null
                && upperRightCloseButton.containsPoint(x, y);

            base.receiveLeftClick(x, y, playSound);

            if (closeClicked)
            {
                _monitor.Log("[LunchPailUI] Close button clicked, exiting.", LogLevel.Trace);
                _isClosing = true;
                return;
            }

            if (_isClosing)
            {
                _monitor.Log("[LunchPailUI] receiveLeftClick: menu is closing, skipping.", LogLevel.Trace);
                return;
            }

            // Center panel: assign food to a compartment
            var centerPanel = GetPanelBounds(1);
            int centerIndex = GetItemIndexAtPoint(x, y, centerPanel, _unassignedFoods.Count);
            if (centerIndex >= 0)
            {
                ShowAssignmentPrompt(_unassignedFoods[centerIndex]);
                return;
            }

            // Stamina panel: item click — open adjust/unassign prompt
            var staminaPanel = GetPanelBounds(0);
            int staminaIndex = GetItemIndexAtPoint(x, y, staminaPanel, _staminaFoods.Count);
            if (staminaIndex >= 0)
            {
                ShowEditPrompt(_staminaFoods[staminaIndex], _staminaTags[staminaIndex], isStamina: true);
                return;
            }

            // Health panel: item click — open adjust/unassign prompt
            var healthPanel = GetPanelBounds(2);
            int healthIndex = GetItemIndexAtPoint(x, y, healthPanel, _healthFoods.Count);
            if (healthIndex >= 0)
            {
                ShowEditPrompt(_healthFoods[healthIndex], _healthTags[healthIndex], isStamina: false);
            }
        }

        public override void performHoverAction(int x, int y)
        {
            _tooltipText = null;
            CheckHoverPanel(x, y, GetPanelBounds(0), _staminaFoods);
            CheckHoverPanel(x, y, GetPanelBounds(1), _unassignedFoods);
            CheckHoverPanel(x, y, GetPanelBounds(2), _healthFoods);
        }

        // ----------------------------------------------------------------
        // Assignment logic
        // ----------------------------------------------------------------

        private void ShowAssignmentPrompt(SObject food)
        {
            int staminaBudget = GetCompartmentBudget(food, _data.StaminaCompartment);
            int healthBudget  = GetCompartmentBudget(food, _data.HealthCompartment);

            // Only offer compartments that don't already own a tag for this item.
            bool canStamina = staminaBudget == 0;
            bool canHealth  = healthBudget  == 0;

            var responseList = new List<Response>();
            if (canStamina) responseList.Add(new Response("stamina", "Add to Stamina compartment"));
            if (canHealth)  responseList.Add(new Response("health",  "Add to Health compartment"));
            responseList.Add(new Response("cancel", "Cancel"));

            Game1.currentLocation.createQuestionDialogue(
                $"Assign {food.DisplayName} to which compartment?",
                responseList.ToArray(),
                (Farmer _, string which) =>
                {
                    _monitor.Log($"[LunchPailUI] Assignment dialogue response: {which}", LogLevel.Trace);
                    if (which == "cancel")
                    {
                        Game1.activeClickableMenu = this;
                        return;
                    }
                    bool toStamina    = which == "stamina";
                    int otherBudget   = toStamina ? healthBudget : staminaBudget;
                    ShowServingCountPrompt(food, isStamina: toStamina,
                        isEdit: false, existingTag: null, alreadyBudgeted: otherBudget);
                });
        }

        /// <summary>
        /// Opens a NumberSelectionMenu so the player can choose how many servings
        /// to budget for this item.  Used for both initial assignment and editing.
        /// </summary>
        /// <param name="alreadyBudgeted">
        /// Servings already claimed by the *other* compartment for this item.
        /// The max offered to the player is (stack - alreadyBudgeted).
        /// </param>
        private void ShowServingCountPrompt(
            SObject food, bool isStamina, bool isEdit, LunchPailData.FoodTag? existingTag,
            int alreadyBudgeted = 0)
        {
            int currentStack = food.Stack;
            // Available headroom = stack minus what the other compartment already claimed.
            int available = Math.Max(1, currentStack - alreadyBudgeted);

            // Pre-fill: for edits use the smaller of the saved budget and available headroom;
            // for new assignments (or unlimited legacy tags) default to all available headroom.
            int defaultValue = (isEdit && existingTag != null)
                ? Math.Min(
                    existingTag.MaxServings == int.MaxValue ? available : existingTag.MaxServings,
                    available)
                : available;

            Game1.activeClickableMenu = new NumberSelectionMenu(
                $"How many {food.DisplayName} for your Lunch Pail today?",
                (number, price, who) =>
                {
                    _monitor.Log($"[LunchPailUI] Serving count selected: {number} for {food.DisplayName} (edit={isEdit})", LogLevel.Trace);
                    if (isEdit && existingTag != null)
                    {
                        int oldMax = existingTag.MaxServings;
                        existingTag.MaxServings = number;
                        _monitor.Log(
                            $"[LunchPailUI][Edit] {food.DisplayName} serving budget changed: {(oldMax == int.MaxValue ? "unlimited" : oldMax.ToString())} -> {number} | CurrentStack={food.Stack}",
                            LogLevel.Debug);
                    }
                    else
                    {
                        AssignFood(food, isStamina, maxServings: number);
                    }
                    Game1.activeClickableMenu = this;
                    RefreshLists();
                },
                price: -1,
                minValue: 1,
                maxValue: available,
                defaultNumber: defaultValue);
        }

        /// <summary>
        /// Opens a contextual dialogue for an already-assigned item, letting the
        /// player adjust the serving budget or unassign the item entirely.
        /// </summary>
        private void ShowEditPrompt(SObject food, LunchPailData.FoodTag tag, bool isStamina)
        {
            var responses = new Response[]
            {
                new Response("adjust",   "Adjust serving amount"),
                new Response("unassign", "Unassign"),
                new Response("cancel",   "Cancel")
            };

            string compartment = isStamina ? "Stamina" : "Health";
            Game1.currentLocation.createQuestionDialogue(
                $"{food.DisplayName} is assigned to the {compartment} compartment.",
                responses,
                (Farmer _, string which) =>
                {
                    _monitor.Log($"[LunchPailUI] Edit dialogue response: {which} for {food.DisplayName}", LogLevel.Trace);
                    if (which == "adjust")
                    {
                        // Max for this tag = stack minus what the OTHER compartment has claimed.
                        var otherCompartment = isStamina ? _data.HealthCompartment : _data.StaminaCompartment;
                        int otherBudget = GetCompartmentBudget(food, otherCompartment);
                        ShowServingCountPrompt(food, isStamina, isEdit: true, existingTag: tag,
                            alreadyBudgeted: otherBudget);
                    }
                    else if (which == "unassign")
                    {
                        UnassignFood(food, isStamina);
                        Game1.activeClickableMenu = this;
                    }
                    else
                    {
                        Game1.activeClickableMenu = this;
                    }
                });
        }

        private void AssignFood(SObject food, bool isStamina, int maxServings = int.MaxValue)
        {
            var tag = new LunchPailData.FoodTag
            {
                ItemId = food.QualifiedItemId,
                Quality = food.Quality,
                DisplayName = food.DisplayName,
                MaxServings = maxServings
            };

            if (isStamina)
                _data.StaminaCompartment.Add(tag);
            else
                _data.HealthCompartment.Add(tag);

            _monitor.Log(
                $"[LunchPailUI][Assign] {food.DisplayName} -> {(isStamina ? "Stamina" : "Health")} compartment | MaxServings={(maxServings == int.MaxValue ? "unlimited" : maxServings.ToString())} | CurrentStack={food.Stack}",
                LogLevel.Debug);
        }

        /// <summary>
        /// Returns the number of servings of <paramref name="food"/> already budgeted
        /// in <paramref name="compartment"/>. An unlimited tag (MaxServings == int.MaxValue)
        /// is treated as claiming the entire stack, so nothing is left for the other side.
        /// Returns 0 when no tag exists for this item.
        /// </summary>
        private static int GetCompartmentBudget(
            SObject food, List<LunchPailData.FoodTag> compartment)
        {
            var tag = compartment.FirstOrDefault(t =>
                t.ItemId == food.QualifiedItemId && t.Quality == food.Quality);
            if (tag == null) return 0;
            return tag.MaxServings == int.MaxValue
                ? food.Stack
                : Math.Min(tag.MaxServings, food.Stack);
        }

        private void UnassignFood(SObject food, bool isStamina)
        {
            var compartment = isStamina ? _data.StaminaCompartment : _data.HealthCompartment;
            var tag = compartment.FirstOrDefault(t =>
                t.ItemId == food.QualifiedItemId && t.Quality == food.Quality);

            if (tag != null)
            {
                compartment.Remove(tag);
                _monitor.Log(
                    $"[LunchPail] Unassigned {food.DisplayName} from {(isStamina ? "stamina" : "health")} compartment.",
                    LogLevel.Trace);
                RefreshLists();
            }
        }

        // ----------------------------------------------------------------
        // Drawing helpers
        // ----------------------------------------------------------------

        private Rectangle GetPanelBounds(int column)
        {
            int panelX = xPositionOnScreen + Padding + column * (PanelWidth + Padding);
            int panelY = yPositionOnScreen + HeaderHeight;
            return new Rectangle(panelX, panelY, PanelWidth, PanelHeight);
        }

        private void DrawPanel(SpriteBatch b, Rectangle bounds, Color tint, string title)
        {
            b.Draw(Game1.fadeToBlackRect, bounds, tint);
            Utility.drawTextWithShadow(b, title,
                Game1.smallFont,
                new Vector2(bounds.X + Padding, bounds.Y + Padding),
                Color.White);
        }

        /// <param name="tags">
        /// Parallel tag list matching <paramref name="items"/>; used to render the serving budget.
        /// </param>
        private void DrawItemList(
            SpriteBatch b, List<SObject> items, List<LunchPailData.FoodTag> tags,
            Rectangle panel)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var tag  = tags[i];
                int itemY = panel.Y + 40 + i * ItemHeight;

                // Hide the native stack count — we display the budget number in the label instead.
                item.drawInMenu(b,
                    new Vector2(panel.X + Padding, itemY),
                    0.75f, 1f, 0.9f,
                    StackDrawType.Hide,
                    Color.White, false);

                // Check whether actual inventory has fallen below the remaining daily budget.
                // IsInDeficit uses the same aggregated cross-compartment logic as CheckDeficits.
                bool inDeficit = _consumptionManager.IsInDeficit(tag);

                // In alert-only mode, draw a faint red tint behind the row to flag the deficit.
                if (inDeficit && !_config().AutoAdjustBudget)
                {
                    b.Draw(Game1.fadeToBlackRect,
                        new Rectangle(panel.X, itemY, PanelWidth, ItemHeight),
                        Color.Red * 0.25f);
                }

                // Always show a quantity alongside the name.
                // When MaxServings is unlimited, the effective quantity is the full live stack.
                // When explicit, clamp to the live stack in case inventory shrank mid-day.
                int displayCount = tag.MaxServings == int.MaxValue
                    ? item.Stack
                    : Math.Min(tag.MaxServings, item.Stack);
                string label = $"{item.DisplayName} (×{displayCount})";
                Color labelColor = (inDeficit && !_config().AutoAdjustBudget) ? Color.Red : Color.White;

                Utility.drawTextWithShadow(b, label,
                    Game1.smallFont,
                    new Vector2(panel.X + Padding + ItemIconSize + 4, itemY + 8),
                    labelColor);
            }
        }

        /// <summary>
        /// Logs the rendered label for every item in one panel. Called from draw()
        /// once per UI open after all panels have been drawn, so all three panels
        /// are captured before the guard flag is set.
        /// </summary>
        private void LogPanelDraw(string panelName, List<SObject> items, List<LunchPailData.FoodTag> tags)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var tag  = tags[i];
                int displayCount = tag.MaxServings == int.MaxValue
                    ? item.Stack
                    : Math.Min(tag.MaxServings, item.Stack);
                _monitor.Log(
                    $"[LunchPailUI][Draw][{panelName}] Rendered: '{item.DisplayName} (×{displayCount})' | MaxServings={(tag.MaxServings == int.MaxValue ? "unlimited" : tag.MaxServings.ToString())} Stack={item.Stack}",
                    LogLevel.Debug);
            }
        }

        private void DrawUnassignedList(SpriteBatch b, List<SObject> items, Rectangle panel)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int itemY = panel.Y + 40 + i * ItemHeight;

                // Suppress the native count; we render the available-to-assign number ourselves.
                item.drawInMenu(b,
                    new Vector2(panel.X + Padding, itemY),
                    0.75f, 1f, 0.9f,
                    StackDrawType.Hide,
                    Color.White, false);

                // Available = full stack minus what both compartments have already claimed.
                int claimed = GetCompartmentBudget(item, _data.StaminaCompartment)
                            + GetCompartmentBudget(item, _data.HealthCompartment);
                int available = item.Stack - claimed;
                string label = $"{item.DisplayName} ({available})";

                Utility.drawTextWithShadow(b, label,
                    Game1.smallFont,
                    new Vector2(panel.X + Padding + ItemIconSize + 4, itemY + 8),
                    Color.White);
            }
        }

        private int GetItemIndexAtPoint(int x, int y, Rectangle panel, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int itemY = panel.Y + 40 + i * ItemHeight;
                if (new Rectangle(panel.X, itemY, PanelWidth, ItemHeight).Contains(x, y))
                    return i;
            }
            return -1;
        }

        private void CheckHoverPanel(int x, int y, Rectangle panel, List<SObject> items)
        {
            int index = GetItemIndexAtPoint(x, y, panel, items.Count);
            if (index < 0) return;

            var item = items[index];
            float stamina = FoodHelper.GetStaminaRestore(item);
            float health = FoodHelper.GetHealthRestore(item);
            _tooltipText = $"{item.DisplayName}\nStamina: +{stamina:0}\nHealth: +{health:0}";
        }
    }
}
