using Microsoft.Xna.Framework.Input;
using MoreLocales.Common;
using MoreLocales.Core.Inflections;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace MoreLocales.Core;

/// <summary>
/// Contains stuff I could only do in a ModSystem.
/// </summary>
public class MoreLocalesSystem : ModSystem
{
    //private static bool testOverlap = false;
    /// <summary>
    /// The menu ID (<see cref="MenuID"/>) of the improved language menu added by MoreLocales, 'LANGS'
    /// </summary>
    public const int betterLangMenuID = 74592; //LANGS
    /// <summary>
    /// The instance of the language menu UI.
    /// </summary>
    public static BetterLangMenuUI betterLangMenu = new();
    /// <summary>
    /// The user interface used to display the in-game language menu button.
    /// </summary>
    public static UserInterface ingameLangMenuButtonUI;
    /// <inheritdoc/>
    public override void Load()
    {
        IL_Main.DrawMenu += GoToBetterLangMenuInstead;
        //On_Main.DrawInterface += On_Main_DrawInterface;
    }
    // the docs for OnModLoad are wrong: it's called if all content is autoloaded specifically for the mod it's called on, not all mods.
    // so we use SetStaticDefaults instead
    /// <inheritdoc/>
    public override void SetStaticDefaults()
    {
        MoreLocalesAPI._canRegister = false;
        // also, create the arrays for UI
        BetterLangMenuV2.InitArrays();
    }
    /// <inheritdoc/>
    public override void OnLocalizationsLoaded()
    {
        if (!MoreLocalesSets._didFirstLoad)
        {
            MoreLocalesAPI.InitModLocalizationFlags();
            LPlusFile.Initialize();
        }
        LPlusFile.UpdateCurrent();
        LPlusFile.Current?.SetupNameOverrides();
        MoreLocalesSets.ReloadedLocalizations();
        LPlusFile.Current?.SetupInflectionAndFormOverrides();
        LangFeaturesPlus.boundDirectPluralizeTextCache.Clear();
    }
    private static void GoToBetterLangMenuInstead(ILContext il)
    {
        Mod mod = MoreLocales.Instance;
        try
        {
            var c = new ILCursor(il);

            if (!c.TryGotoNext(i => i.MatchLdcI4(1213), i => i.MatchStsfld<Main>("menuMode")))
            {
                mod.Logger.Warn("GoToBetterLangMenuInstead: Couldn't find instruction for attempt to switch to lang menu");
                return;
            }

            c.Next.Operand = betterLangMenuID;

            Type inter = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.UI.Interface");

            if (!c.TryGotoNext(MoveType.After, i => i.MatchCall(inter.GetMethod("ModLoaderMenus", BindingFlags.NonPublic | BindingFlags.Static))))
            {
                mod.Logger.Warn("GoToBetterLangMenuInstead: Couldn't find instruction for attempt to enter modded menus");
                return;
            }

            c.EmitDelegate(TryEnterBetterLangMenu);

        }
        catch
        {
            MonoModHooks.DumpIL(mod, il);
        }
    }
    private static void TryEnterBetterLangMenu()
    {
        if (Main.menuMode != betterLangMenuID)
            return;

        Main.MenuUI.SetState(betterLangMenu);
        Main.menuMode = MenuID.FancyUI;
    }
    /// <inheritdoc/>
    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (InGameLanguageButtonUI.Instance is null)
        {
            var state = InGameLanguageButtonUI.Instance = new();
            var ui = ingameLangMenuButtonUI = new();

            ui.SetState(state);
        }

        layers.Insert(
            layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text")),
            new LegacyGameInterfaceLayer("MoreLocales: In-Game Language Menu Button", () =>
        {
            if (Main.playerInventory)
                InGameLanguageButtonUI.Instance.Draw(Main.spriteBatch);
            return true;
        }, InterfaceScaleType.UI));
    }
    /// <inheritdoc/>
    public override void UpdateUI(GameTime gameTime)
    {
        if (Main.ingameOptionsWindow || Main.InGameUI.IsVisible)
            return;
        if (ingameLangMenuButtonUI?.CurrentState != null)
            ingameLangMenuButtonUI.Update(gameTime);
    }
    #region DEBUGGING
    private static void On_Main_DrawInterface(On_Main.orig_DrawInterface orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);

        string desiredFont = "MoreLocales/Assets/Fonts/MouseText-TH";
        if (!ModContent.HasAsset(desiredFont))
        {
            Main.NewText("Asset not found");
            return;
        }

        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Main.UIScaleMatrix);

        Asset<DynamicSpriteFont> testFont = ModContent.Request<DynamicSpriteFont>(desiredFont, AssetRequestMode.ImmediateLoad);

        Vector2 padding = new(128f);
        float yBetween = 32f;
        float xBetween = 559f;

        SpriteBatch sb = Main.spriteBatch;
        DynamicSpriteFont testVanilla = FontAssets.CombatText[1].Value;

        for (int i = 0; i < 4; i++)
        {
            string testString = i switch
            {
                0 => "abc01234",
                1 => "áêç",
                2 => "бгд",
                3 => "เกี๊ยว",
                _ => ""
            };

            for (int j = 0; j < 2; j++)
            {
                DynamicSpriteFont font = j == 0 ? testVanilla : testFont.Value;
                sb.DrawString(font, testString, padding + new Vector2(j == 0 ? 0 : false ? 0 : xBetween, i * yBetween), Color.White);
            }
        }

        Main.spriteBatch.End();
    }

    /// <inheritdoc/>
    public override void PostUpdateDusts()
    {
        // if (Main.keyState.IsKeyDown(Keys.F) && !Main.oldKeyState.IsKeyDown(Keys.F))
        {
            /*
            if (TextHelper.TryAddDiacritic('a', SpecialPatternCharacter.Ogonek, out char c))
                Main.NewText(c);
            */
            /*
            if (LPlusFile.Current != null)
            {
                InflectableWord w = new("Galán");
                if (w.EnsureExists(InflectionData.MascPl))
                    Main.NewText(w.Get(InflectionData.MascPl));
                if (w.EnsureExists(InflectionData.FemPl))
                    Main.NewText(w.Get(InflectionData.FemPl));
            }
            */
            /*
            if (LPlusFile.Current != null)
            {
                ref var infl = ref LPlusFile.Current.Inflections;
                ref var it = ref LPlusFile.Current.Items;
                ref var pre = ref LPlusFile.Current.Prefixes;
                Main.NewText(string.Join<InflectionPattern>('|', infl.WordRecognizers));
                foreach (var kvp in infl.InflectionRecognizers)
                {
                    Main.NewText($"{kvp.Key}::{string.Join<InflectionPattern>('=', kvp.Value)}");
                    for (int i = 0; i < kvp.Value.Length; i++)
                    {
                        var pat = kvp.Value[i];
                        if (pat.Exceptions != null && pat.Exceptions.Length != 0)
                        {
                            Main.NewText(string.Join('Y', pat.Exceptions));
                        }
                    }
                }
                MoreLocalesSets.CachedInflectionData[Main.LocalPlayer.HeldItem.type].Data.Deconstruct(out InflectionData inflection, out int paradigm);
                inflection.Deconstruct(out GrammaticalGender gender, out GrammaticalNumber number);
                Main.NewText($"{gender}{number}:{paradigm}");
            }
            */
            /*
            if (LPlusFile.Current != null)
            {
                string test = LPlusFile.Current.Inflections.ExtractFunctionalWord("Gafas de protección");
                Main.NewText(test ?? "null");
                InflectableWord w = new(test);
                Main.NewText(w.BaseData.Inflection);
            }
            */
            /*
            foreach (var thing in LPlusFile.Current.Inflections.InflectionRecognizers)
            {
                Main.NewText($"{thing.Key}:{string.Join<InflectionPattern>(" | ", thing.Value)}");
            }
            */
            /*
            Item held = Main.LocalPlayer.HeldItem;
            if (held != null && !held.IsAir && held.type > ItemID.None)
            {
                
                if (held.prefix > 0)
                {
                    var prefix = MoreLocalesSets.Prefixes[held.prefix];
                    if (prefix.AlternateForms != null)
                    {
                        prefix.AlternateForms.Clear();
                        InflectionData inflectTo = InflectionData.FemSg;
                        if (!prefix.EnsureExists(inflectTo))
                            Main.NewText($"Inflecting {prefix.BaseForm} for {inflectTo} is impossible.");
                        else
                            Main.NewText(prefix.Get(inflectTo));
                        // InflectableWord w = new(Lang.prefix[held.prefix].Value);
                    }
                }
                
                MoreLocalesSets.CachedInflectionData[held.type].Data.Inflection.Deconstruct(out GrammaticalGender g, out GrammaticalNumber n);
                Main.NewText($"{g}:{n}");
            }
            */

            /*
            var set = MoreLocalesSets.Prefixes;
            for (int i = 0; i < set.Length; i++)
            {
                var s = set[i];
                string str = s.Get();
                if (str != null)
                    Main.NewText(str);
            }
            */
            /*
            foreach (var thing in LPlusFile._lplusFiles)
            {
                Main.NewText(thing.Key);
                foreach (var otherThing in thing.Value)
                {
                    Main.NewText(otherThing.Key);
                    foreach (var othererThing in otherThing.Value)
                    {
                        Main.NewText(othererThing);
                    }
                }
            }
            */

            /*
            LPlusFile f = LPlusFile.Current;
            if (f != null)
            {
                Main.NewText(f.Source);
            }
            */

            /*
            if (InflectionPattern.TryParse("-anos", out var result))
            {
                result.TryReplace("Republicanos", "hehnihtemon", out var rarasult);
                Main.NewText(rarasult.ToString());
            }
            */
            /*
            for (int i = 0; i < MoreLocalesAPI.extraCulturesV2.Length; i++)
            {
                var culture = MoreLocalesAPI.extraCulturesV2[i];
                Main.NewText($"{culture.Name}, {culture.Mod?.Name ?? "null"}, {culture.FunctionalOwner}");
            }
            */
            /*
            Main.NewText(Language.Exists("Mods.MoreLocales.VanillaData.InflectionData.Prefixes.Large"));
            Main.NewText(LangUtils.CategoryExists("Mods.MoreLocales.VanillaData.InflectionData.Prefixes.Large"));
            */
            //Main.NewText(LangUtils.)
            /*
            foreach (string category in LangUtils.Categories)
                Main.NewText(category);
            */
            //MoreLocalesSets.ReloadedLocalizations();
            /*
            foreach (var item in (typeof(LangFeaturesPlus).GetField("GenderNames", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as string[]))
            {
                Main.NewText(item);
            }
            */
            /*
            MoreLocalesSets.ReloadedLocalizations();
            if (!MoreLocalesSets._didFirstLoad || LangUtils.FilesWillBeReloadedDueToCommentsChange)
                Main.NewText($"CAN'T {Main.rand.NextDouble()}");
            */
            /*
            foreach (var key in LanguageManager.Instance._categoryGroupedKeys.Keys)
                Console.WriteLine(key);
            */
            //Main.NewText(LanguageManager.Instance._categoryGroupedKeys.ContainsKey("Mods.MoreLocales.VanillaData.InflectionData"));
        }
    }
    #endregion
}
