using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace MoreLocales.Common
{
    internal class InGameLanguageButtonUI : UIState
    {
        internal InGameLanguageButton button;
        internal static InGameLanguageButtonUI Instance;
        private static Point _lastDimensions;
        public InGameLanguageButtonUI()
        {
            Append(button = new());
            button.Width.Set(36f, 0f);
            button.Height.Set(36f, 0f);
        }
        public override void Update(GameTime gameTime)
        {
            //
            PlaceButtonCorrectly();
            Recalculate();
            //

            TryRecalculate();
            base.Update(gameTime);
        }
        public override void Draw(SpriteBatch spriteBatch)
        {
            DrawChildren(spriteBatch);
        }
        private void TryRecalculate()
        {
            Point dimensions = new(Main.screenWidth, Main.screenHeight);
            if (dimensions != _lastDimensions)
            {
                PlaceButtonCorrectly();
                Recalculate();
            }
            _lastDimensions = dimensions;
        }
        private void PlaceButtonCorrectly()
        {
            float dim = button.Height.Pixels;
            button.Top.Set(Main.screenHeight - dim - 12, 0f);
            button.Left.Set(Main.screenWidth - 230 - dim, 0f);
        }
    }
    internal class InGameLanguageButton : UIElement
    {
        private static Asset<Texture2D> _buttonGraphic;
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            bool hovered = ContainsPoint(Main.MouseScreen);
            _buttonGraphic ??= MoreLocales.Instance.Assets.Request<Texture2D>("Assets/YouAreMoreLocales");

            Rectangle frame = _buttonGraphic.Frame(1, 2);

            if (hovered)
            {
                frame.Y += frame.Height;
                UICommon.TooltipMouseText(Lang.menu[103].Value);
                Main.LocalPlayer.mouseInterface = true;
            }

            spriteBatch.Draw(_buttonGraphic.Value, GetDimensions().Center(), frame, Color.White, 0f, frame.Size() * 0.5f, 1f, SpriteEffects.None, 0f);

        }
        public override void LeftClick(UIMouseEvent evt)
        {
            if (!Main.playerInventory)
                return;

            SoundEngine.PlaySound(in SoundID.MenuOpen);

            IngameFancyUI.OpenUIState(MoreLocalesSystem.betterLangMenu);
        }
    }
}
