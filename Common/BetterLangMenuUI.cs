using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria.UI.Gamepad;

namespace MoreLocales.Common;

/// <summary>
/// MoreLocales' UI. Generally you don't want to mess with this.
/// </summary>
public class BetterLangMenuUI : UIState, IHaveBackButtonCommand
{
    // i was testing different variables from screen dimensions, i'm not that lazy to not want to write Main ok¿¿
    /// <inheritdoc/>
    public UIState PreviousUIState { get; set; }
    internal BackButton backButton;
    private Vector2 _previousResolution;
    internal enum Arrow
    {
        Left = -1,
        None = 0,
        Right = 1
    }
    internal static Arrow hoveredArrow;
    internal static bool hoveredArrowWasPressed;
    /// <inheritdoc/>
    public override void OnInitialize()
    {
        backButton = new(70f, 50f);
        Append(backButton);
    }
    private void RecalculateButtonPosition(Vector2 newRes)
    {
        if (_previousResolution == newRes)
            return;

        _previousResolution = newRes;

        float screenMiddle = newRes.X * 0.5f;

        Vector2 backButtonDimensions = new(backButton.Width.Pixels, backButton.Height.Pixels);
        float halfX = backButtonDimensions.X * 0.5f;

        backButton.Left.Set(screenMiddle - halfX, 0f);
        backButton.Top.Set(newRes.Y - backButtonDimensions.Y - 30f, 0f);

        Recalculate();
    }
    /// <inheritdoc/>
    public override void Draw(SpriteBatch spriteBatch)
    {
        Vector2 newRes = new(Main.screenWidth, Main.screenHeight);

        var render = BetterLangMenuV2.FinalRender;
        render.Request();
        if (render.IsReady)
        {
            Main.LocalPlayer.controlHook = false;

            Texture2D tex = render._target;
            Vector2 offset = new(0f, Main.gameMenu ? 50f : 0f);
            Vector2 drawCenter = newRes * 0.5f + offset;
            Vector2 innerSize = tex.Size();

            Rectangle centered = Utils.CenteredRectangle(drawCenter, innerSize);

            Rectangle centeredBig = centered;
            centeredBig.Inflate(BetterLangMenuV2.PaddingXTotal, BetterLangMenuV2.PaddingYTotal);

            UIHelper.DrawAdjustableBox(spriteBatch, BetterLangMenuV2._panelTexture.Value, centeredBig, Color.Gray);

            if (centeredBig.Contains(Main.mouseX, Main.mouseY))
                Main.LocalPlayer.mouseInterface = true;

            spriteBatch.End(out var spriteBatchData);
            spriteBatchData.SortMode = SpriteSortMode.Immediate;
            spriteBatch.Begin(spriteBatchData);

            LangMenuV2.sideFadeShader.Apply(tex, 20f);
            spriteBatch.Draw(tex, drawCenter, null, Color.White, 0f, innerSize * 0.5f, 1f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatchData.SortMode = SpriteSortMode.Deferred;
            spriteBatch.Begin(spriteBatchData);

            BetterLangMenuV2.HandleInteractions(in centered);

            HandleArrows(in spriteBatch, in centeredBig);
        }

        RecalculateButtonPosition(newRes);
        base.Draw(spriteBatch);

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
    }
    internal static bool leftArrowAvailable = true;
    internal static bool rightArrowAvailable = true;
    internal static float leftArrowProg = 1f;
    internal static float rightArrowProg = 1f;
    internal static void HandleArrows(in SpriteBatch sb, in Rectangle baseUI)
    {
        var arrowAsset = BetterLangMenuV2._arrowButtons;
        if (!arrowAsset.IsLoaded)
            return;

        Texture2D arrowTexture = arrowAsset.Value;

        int singleArrowHeight = arrowTexture.Height / 2;

        float changeProgSpeed = 0.2f;
        float targetOpac = 0.4f;

        Vector2 origin = new(arrowTexture.Width, singleArrowHeight * 0.5f);
        Rectangle frame = new(0, 0, arrowTexture.Width, singleArrowHeight - 1);
        Vector2 drawPos = new(baseUI.X, baseUI.Y + baseUI.Height / 2);
        int mouseX = Main.mouseX;
        int mouseY = Main.mouseY;

        Color c = Color.White;

        if (HandleSingleArrow(drawPos.X - frame.Width, drawPos.X, Arrow.Left) && leftArrowAvailable)
        {
            frame.Y += singleArrowHeight;
        }

        leftArrowProg = MathHelper.Lerp(leftArrowProg, leftArrowAvailable ? 1f : 0f, changeProgSpeed);
        float finalFactor = MathHelper.Lerp(targetOpac, 1f, leftArrowProg);
        float scale = 1f - (1f - finalFactor) * 0.3f;

        sb.Draw(arrowTexture, drawPos, frame, c * finalFactor, 0f, origin, scale, SpriteEffects.FlipHorizontally, 0f);

        frame.Y = 0;
        drawPos.X += baseUI.Width;
        origin.X = 0;

        if (HandleSingleArrow(drawPos.X, drawPos.X + frame.Width, Arrow.Right) && rightArrowAvailable)
        {
            frame.Y += singleArrowHeight;
        }

        rightArrowProg = MathHelper.Lerp(rightArrowProg, rightArrowAvailable ? 1f : 0f, changeProgSpeed);
        finalFactor = MathHelper.Lerp(targetOpac, 1f, rightArrowProg);
        scale = 1f - (1f - finalFactor) * 0.3f;

        sb.Draw(arrowTexture, drawPos, frame, c * finalFactor, 0f, origin, scale, SpriteEffects.None, 0f);

        bool HandleSingleArrow(float lefternmost, float righternmost, Arrow arrow)
        {
            float prog = Utils.GetLerpValue(lefternmost, righternmost, mouseX, false);
            if (arrow == Arrow.Right)
                prog = 1f - prog;
            float grow = singleArrowHeight * 0.5f * prog;
            if (prog >= 0f && prog <= 1f && MathF.Abs(mouseY - drawPos.Y) < grow)
            {
                Main.LocalPlayer.mouseInterface = true;
                hoveredArrow = arrow;
                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    hoveredArrowWasPressed = true;
                }
                return true;
            }
            return false;
        }
    }
    void IHaveBackButtonCommand.HandleBackButtonUsage()
    {
        BetterLangMenuV2.currentPage = 0;
        BetterLangMenuV2.currentPageVisual = 0f;

        SoundEngine.PlaySound(in SoundID.MenuClose);

        if (backButton != null)
        {
            backButton.grow = false;
            backButton.extraScale = 0f;
        }

        if (Main.gameMenu)
        {
            Main.MenuUI.SetState(null);
            Main.menuMode = MenuID.Settings;
        }
        else
        {
            IngameFancyUI.Close();
        }
    }
}
internal class BackButton : UIElement
{
    private IHaveBackButtonCommand DoBackAction => Parent as IHaveBackButtonCommand;
    public bool grow = false;
    public float extraScale = 0f;
    public BackButton(float width, float height)
    {
        Width.Set(width, 0f);
        Height.Set(height, 0f);
        OnMouseOver += Hovered;
        OnMouseOut += Unhovered;
        OnLeftClick += Clicked;
        OnUpdate += Upd;
    }

    private void Upd(UIElement affectedElement)
    {
        if (grow && extraScale < 1f)
            extraScale = Math.Min(extraScale + 0.1f, 1f);
        else if (!grow && extraScale > 0f)
            extraScale = Math.Max(extraScale - 0.1f, 0f);

        if (grow && !ContainsPoint(Main.MouseScreen))
            grow = false;
    }

    private void Unhovered(UIMouseEvent evt, UIElement listeningElement)
    {
        grow = false;
    }

    private void Hovered(UIMouseEvent evt, UIElement listeningElement)
    {
        SoundEngine.PlaySound(in SoundID.MenuTick);
        grow = true;
    }

    private void Clicked(UIMouseEvent evt, UIElement listeningElement) => DoBackAction?.HandleBackButtonUsage();

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        string text = Lang.menu[5].Value;
        DynamicSpriteFont font = FontAssets.DeathText.Value;
        float finalScale = 0.75f + (extraScale * 0.3f);
        Vector2 center = GetDimensions().Center();
        Vector2 textSize = font.MeasureString(text) * finalScale;
        Color finalColor = MiscHelper.LerpMany(extraScale, [Color.Gray, Color.White, Color.Gold]);
        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, center - (textSize * 0.5f), finalColor, 0f, Vector2.Zero, new Vector2(finalScale));
    }
}
