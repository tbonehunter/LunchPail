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

        private const int PanelWidth = 260; // widened 30% for label legibility
        private const int PanelHeight = 400;
        private const int ItemHeight = 48;
        private const int ItemIconSize = 32;
        private const int Padding = 12;
        private const int XButtonSize = 16;
        private const int HeaderHeight = 60;
        private const int TotalWidth = PanelWidth * 3 + Padding * 4;
        private const int TotalHeight = PanelHeight + HeaderHeight;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private readonly IMonitor _monitor;
        private readonly LunchPailData _data;
        private readonly Func<Config> _config;

        private List<SObject> _unassignedFoods = new();
        private List<SObject> _staminaFoods = new();
        private List<LunchPailData.FoodTag> _staminaTags = new();
        private List<SObject> _healthFoods = new();
        private List<LunchPailData.FoodTag> _healthTags = new();

        private string? _tooltipText = null;

        // ----------------------------------------------------------------
        // Constructor
        // ----------------------------------------------------------------

        public LunchPailUI(IMonitor monitor, LunchPailData data, Func<Config> config)
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

            RefreshLists();
        }

        // ----------------------------------------------------------------
        // Refresh resolved item lists from inventory + tags
        // ----------------------------------------------------------------

        private void RefreshLists()
        {
            var player = Game1.player;

            // Build parallel (tag, SObject) pairs so the draw layer can access MaxServings
            // for each resolved item without a second lookup.
            var staminaPairs = _data.StaminaCompartment
                .Select(tag => (tag, item: FoodHelper.FindTaggedItemInInventory(player, tag)))
                .Where(p => p.item != null)
                .ToList();
            _staminaFoods = staminaPairs.Select(p => p.item!).ToList();
            _staminaTags  = staminaPairs.Select(p => p.tag).ToList();

            var healthPairs = _data.HealthCompartment
                .Select(tag => (tag, item: FoodHelper.FindTaggedItemInInventory(player, tag)))
                .Where(p => p.item != null)
                .ToList();
            _healthFoods = healthPairs.Select(p => p.item!).ToList();
            _healthTags  = healthPairs.Select(p => p.tag).ToList();

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
            DrawItemList(b, _staminaFoods, _staminaTags, staminaPanel, showX: true);
            DrawUnassignedList(b, _unassignedFoods, centerPanel);
            DrawItemList(b, _healthFoods, _healthTags, healthPanel, showX: true);

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
            base.receiveLeftClick(x, y, playSound);

            // Center panel: assign food to a compartment
            var centerPanel = GetPanelBounds(1);
            int centerIndex = GetItemIndexAtPoint(x, y, centerPanel, _unassignedFoods.Count);
            if (centerIndex >= 0)
            {
                ShowAssignmentPrompt(_unassignedFoods[centerIndex]);
                return;
            }

            // Stamina panel: X button takes priority over item body click
            var staminaPanel = GetPanelBounds(0);
            int staminaXIndex = GetXButtonIndexAtPoint(x, y, staminaPanel, _staminaFoods.Count);
            if (staminaXIndex >= 0)
            {
                UnassignFood(_staminaFoods[staminaXIndex], isStamina: true);
                return;
            }

            // Stamina panel: item body click — open adjust/unassign prompt
            int staminaIndex = GetItemIndexAtPoint(x, y, staminaPanel, _staminaFoods.Count);
            if (staminaIndex >= 0)
            {
                ShowEditPrompt(_staminaFoods[staminaIndex], _staminaTags[staminaIndex], isStamina: true);
                return;
            }

            // Health panel: X button takes priority over item body click
            var healthPanel = GetPanelBounds(2);
            int healthXIndex = GetXButtonIndexAtPoint(x, y, healthPanel, _healthFoods.Count);
            if (healthXIndex >= 0)
            {
                UnassignFood(_healthFoods[healthXIndex], isStamina: false);
                return;
            }

            // Health panel: item body click — open adjust/unassign prompt
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
                    if (isEdit && existingTag != null)
                    {
                        existingTag.MaxServings = number;
                        _monitor.Log(
                            $"[LunchPail] Adjusted {food.DisplayName} serving budget to {number}.",
                            LogLevel.Trace);
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
                $"[LunchPail] Assigned {food.DisplayName} to {(isStamina ? "stamina" : "health")} compartment.",
                LogLevel.Trace);
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
            Rectangle panel, bool showX)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var tag  = tags[i];
                int itemY = panel.Y + 40 + i * ItemHeight;

                item.drawInMenu(b,
                    new Vector2(panel.X + Padding, itemY),
                    0.75f, 1f, 0.9f,
                    StackDrawType.Draw,
                    Color.White, false);

                // Show the serving budget alongside the name when a specific limit is set.
                string label = tag.MaxServings == int.MaxValue
                    ? item.DisplayName
                    : $"{item.DisplayName} (×{tag.MaxServings})";

                Utility.drawTextWithShadow(b, label,
                    Game1.smallFont,
                    new Vector2(panel.X + Padding + ItemIconSize + 4, itemY + 8),
                    Color.White);

                if (showX)
                {
                    b.Draw(Game1.mouseCursors,
                        GetXButtonRect(panel, i),
                        new Rectangle(337, 494, 12, 12),
                        Color.White);
                }
            }
        }

        private void DrawUnassignedList(SpriteBatch b, List<SObject> items, Rectangle panel)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int itemY = panel.Y + 40 + i * ItemHeight;

                item.drawInMenu(b,
                    new Vector2(panel.X + Padding, itemY),
                    0.75f, 1f, 0.9f,
                    StackDrawType.Draw,
                    Color.White, false);

                Utility.drawTextWithShadow(b, item.DisplayName,
                    Game1.smallFont,
                    new Vector2(panel.X + Padding + ItemIconSize + 4, itemY + 8),
                    Color.White);
            }
        }

        private Rectangle GetXButtonRect(Rectangle panel, int index)
        {
            int itemY = panel.Y + 40 + index * ItemHeight;
            return new Rectangle(
                panel.X + PanelWidth - XButtonSize - Padding,
                itemY + (ItemHeight - XButtonSize) / 2,
                XButtonSize,
                XButtonSize);
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

        private int GetXButtonIndexAtPoint(int x, int y, Rectangle panel, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (GetXButtonRect(panel, i).Contains(x, y))
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
