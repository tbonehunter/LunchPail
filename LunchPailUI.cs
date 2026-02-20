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

        private const int PanelWidth = 200;
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
        private List<SObject> _healthFoods = new();

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

            _staminaFoods = _data.StaminaCompartment
                .Select(tag => FoodHelper.FindTaggedItemInInventory(player, tag))
                .Where(obj => obj != null)
                .Cast<SObject>()
                .ToList();

            _healthFoods = _data.HealthCompartment
                .Select(tag => FoodHelper.FindTaggedItemInInventory(player, tag))
                .Where(obj => obj != null)
                .Cast<SObject>()
                .ToList();

            var taggedKeys = _data.StaminaCompartment
                .Concat(_data.HealthCompartment)
                .Select(t => t.ItemId + "_" + t.Quality)
                .ToHashSet();

            _unassignedFoods = player.Items
                .OfType<SObject>()
                .Where(obj => FoodHelper.IsEdible(obj)
                    && !taggedKeys.Contains(obj.QualifiedItemId + "_" + obj.Quality))
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
            DrawItemList(b, _staminaFoods, staminaPanel, showX: true);
            DrawUnassignedList(b, _unassignedFoods, centerPanel);
            DrawItemList(b, _healthFoods, healthPanel, showX: true);

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

            // Stamina panel: X button to unassign
            var staminaPanel = GetPanelBounds(0);
            int staminaXIndex = GetXButtonIndexAtPoint(x, y, staminaPanel, _staminaFoods.Count);
            if (staminaXIndex >= 0)
            {
                UnassignFood(_staminaFoods[staminaXIndex], isStamina: true);
                return;
            }

            // Health panel: X button to unassign
            var healthPanel = GetPanelBounds(2);
            int healthXIndex = GetXButtonIndexAtPoint(x, y, healthPanel, _healthFoods.Count);
            if (healthXIndex >= 0)
            {
                UnassignFood(_healthFoods[healthXIndex], isStamina: false);
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
            var responses = new Response[]
            {
                new Response("stamina", "Add to Stamina compartment"),
                new Response("health",  "Add to Health compartment"),
                new Response("cancel",  "Cancel")
            };

            Game1.currentLocation.createQuestionDialogue(
                $"Assign {food.DisplayName} to which compartment?",
                responses,
                (Farmer _, string which) =>
                {
                    if (which == "stamina") AssignFood(food, isStamina: true);
                    else if (which == "health") AssignFood(food, isStamina: false);
                    Game1.activeClickableMenu = this;
                    RefreshLists();
                });
        }

        private void AssignFood(SObject food, bool isStamina)
        {
            var tag = new LunchPailData.FoodTag
            {
                ItemId = food.QualifiedItemId,
                Quality = food.Quality,
                DisplayName = food.DisplayName
            };

            if (isStamina)
                _data.StaminaCompartment.Add(tag);
            else
                _data.HealthCompartment.Add(tag);

            _monitor.Log(
                $"[LunchPail] Assigned {food.DisplayName} to {(isStamina ? "stamina" : "health")} compartment.",
                LogLevel.Trace);
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

        private void DrawItemList(SpriteBatch b, List<SObject> items, Rectangle panel, bool showX)
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
