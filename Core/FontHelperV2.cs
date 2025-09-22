using MoreLocales.Config;
using ReLogic.Content;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.GameContent;
using Terraria.Initializers;
using static ReLogic.Graphics.DynamicSpriteFont;

namespace MoreLocales.Core;

/// <summary>
/// Contains information for a child font of a specific <see cref="Asset{T}"/> of type <see cref="DynamicSpriteFont"/>
/// </summary>
/// <param name="Font">The child font</param>
/// <param name="OverrideParent">Whether or not this font should override the parent font's character if the parent font contains that character</param>
public readonly record struct ChildFont(Asset<DynamicSpriteFont> Font, Func<DynamicSpriteFont, bool> OverrideParent = null);
/// <summary>
/// Contains data to include fonts inside other fonts.
/// </summary>
/// <param name="children"></param>
public readonly struct ChildFontData(ChildFont[] children) : IEnumerable<ChildFont>
{
    /// <summary>
    /// Children fonts which can override the parent.
    /// </summary>
    public readonly ChildFont[] Children = children;
    /// <summary>
    /// Waits for the children assets to be loaded.
    /// </summary>
    public readonly void Nudge()
    {
        for (int i = 0; i < Children.Length; i++)
        {
            var child = Children[i];

            if (!child.Font.IsLoaded)
                child.Font.Wait();
        }
    }
    internal static ChildFontData Create(string[] fileNames, Func<DynamicSpriteFont, bool>[] overrideConds)
    {
        if (fileNames.Length != overrideConds.Length)
            throw new ArgumentException("FileNames and OverrideConds params must be the same length");

        ChildFont[] children = new ChildFont[fileNames.Length];

        for (int i = 0; i < children.Length; i++)
        {
            children[i] = new(AssetHelper.UnsafeRequestSpriteFont($"Assets\\Fonts\\{fileNames[i]}"), overrideConds[i]);
        }

        return new ChildFontData(children);
    }
    /// <inheritdoc/>
    public IEnumerator<ChildFont> GetEnumerator()
    {
        return ((IEnumerable<ChildFont>)Children).GetEnumerator();
    }
    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return Children.GetEnumerator();
    }
}
internal class FontHelperV2
{
    /// <summary>
    /// I'm DEAD my guy<para/>
    /// (Returns the correct children for a given font in the worst way possible.)
    /// </summary>
    public static ChildFontData GetChildren(DynamicSpriteFont source)
    {
        if (source == FontAssets.ItemStack.Value)
            return ItemStackChildren;
        else if (source == FontAssets.MouseText.Value)
            return MouseTextChildren;
        else if (source == FontAssets.DeathText.Value)
            return DeathTextChildren;
        else if (source == FontAssets.CombatText[0].Value)
            return CombatTextChildren;
        else if (source == FontAssets.CombatText[1].Value)
            return CritTextChildren;
        return default;
    }

    public static ChildFontData ItemStackChildren;
    public static ChildFontData MouseTextChildren;
    public static ChildFontData DeathTextChildren;
    public static ChildFontData CombatTextChildren;
    public static ChildFontData CritTextChildren;

    private const string itemStackName = "ItemStack-";
    private const string mouseTextName = "MouseText-";
    private const string deathTextName = "DeathText-";
    private const string combatTextName = "CombatText-";
    private const string critTextName = "CritText-";

    public static LocalizedFont ForcedFont => ClientSideConfig.Instance.ForcedFont;

    public delegate bool IsCharacterSupported_orig(DynamicSpriteFont self, char character);
    public delegate SpriteCharacterData GetCharacterData_orig(DynamicSpriteFont self, char character);
    public delegate float get_CharacterSpacing_orig(DynamicSpriteFont self);

    private static MethodInfo isCharSupported;
    private static MethodInfo getCharData;
    private static MethodInfo internalDraw;
    private static MethodInfo measureString;
    private static MethodInfo getCharSpacing;

    private static FieldInfo charSpacing;

    public static Asset<DynamicSpriteFont> VanillaItemStack = GetVanilla("Fonts/Item_Stack");
    public static Asset<DynamicSpriteFont> VanillaMouseText = GetVanilla("Fonts/Mouse_Text");
    public static Asset<DynamicSpriteFont> VanillaDeathText = GetVanilla("Fonts/Death_Text");
    public static Asset<DynamicSpriteFont> VanillaCombatText = GetVanilla("Fonts/Combat_Text");
    public static Asset<DynamicSpriteFont> VanillaCritText = GetVanilla("Fonts/Combat_Crit");
    public static HashSet<DynamicSpriteFont> VanillaFonts =
    [
        VanillaItemStack.Value,
        VanillaMouseText.Value,
        VanillaDeathText.Value,
        VanillaCombatText.Value,
        VanillaCritText.Value,
    ];
    public static bool CharDataInlined { get; set; } = false;

    private static Asset<DynamicSpriteFont> GetVanilla(string name) => AssetInitializer.LoadAsset<DynamicSpriteFont>(name, AssetRequestMode.ImmediateLoad);
    /// <summary>
    /// I'm sorry.
    /// </summary>
    private static DynamicSpriteFont _currentlyDrawnFont = null;

    public static void DoLoad()
    {
        static bool japaneseActive(DynamicSpriteFont parent) => CultureHelper.CustomCultureActive(CultureNamePlus.Japanese);
        static bool koreanActive(DynamicSpriteFont parent) => CultureHelper.CustomCultureActive(CultureNamePlus.Korean);
        static bool isVanillaFont(DynamicSpriteFont parent) => VanillaFonts.Contains(parent);

        /*
        foreach (var file in MoreLocales.Instance.File.files.Keys)
        {
            MoreLocales.Instance.Logger.Warn(file);
        }
        */

        Func<DynamicSpriteFont, bool>[] commonOverrideConds =
        [
            japaneseActive,
            koreanActive,
            null,
            isVanillaFont
        ];

        string[] itemStackChildrenNames =
        [
            $"{itemStackName}JP",
            $"{itemStackName}KR",
            $"{itemStackName}TH",
            $"{itemStackName}EXT"
        ];
        string[] mouseTextChildrenNames =
        [
            $"{mouseTextName}JP",
            $"{mouseTextName}KR",
            $"{mouseTextName}TH",
            $"{mouseTextName}EXT"
        ];
        string[] deathTextChildrenNames =
        [
            $"{deathTextName}JP",
            $"{deathTextName}KR",
            $"{deathTextName}TH",
            $"{deathTextName}EXT"
        ];
        string[] combatTextChildrenNames =
        [
            $"{combatTextName}JP",
            $"{combatTextName}KR",
            $"{combatTextName}TH",
            $"{mouseTextName}EXT" // yes this is on purpose
        ];
        string[] critTextChildrenNames =
        [
            $"{critTextName}JP",
            $"{critTextName}KR",
            $"{critTextName}TH",
            $"{critTextName}EXT"
        ];

        ItemStackChildren = ChildFontData.Create(itemStackChildrenNames, commonOverrideConds);
        MouseTextChildren = ChildFontData.Create(mouseTextChildrenNames, commonOverrideConds);
        DeathTextChildren = ChildFontData.Create(deathTextChildrenNames, commonOverrideConds);
        CombatTextChildren = ChildFontData.Create(combatTextChildrenNames, commonOverrideConds);
        CritTextChildren = ChildFontData.Create(critTextChildrenNames, commonOverrideConds);

        Type dsf = typeof(DynamicSpriteFont);

        charSpacing = dsf.GetField("_characterSpacing", BindingFlags.Instance | BindingFlags.NonPublic);

        isCharSupported = dsf.GetMethod("IsCharacterSupported");
        getCharData = dsf.GetMethod("GetCharacterData", BindingFlags.Instance | BindingFlags.NonPublic);
        internalDraw = dsf.GetMethod("InternalDraw", BindingFlags.Instance | BindingFlags.NonPublic);
        measureString = dsf.GetMethod("MeasureString");
        getCharSpacing = dsf.GetMethod("get_CharacterSpacing");

        MonoModHooks.Add(isCharSupported, OnIsCharacterSupported);
        MonoModHooks.Add(getCharData, OnGetCharacterData);
        MonoModHooks.Modify(internalDraw, EditInternalDraw);
        MonoModHooks.Modify(measureString, EditMeasureString);
        MonoModHooks.Add(getCharSpacing, OnGetCharacterSpacing);
    }
    private static bool OnIsCharacterSupported(IsCharacterSupported_orig orig, DynamicSpriteFont self, char character)
    {
        if (character != '\n' && character != '\r')
            return self._spriteCharacters.ContainsKey(character) || GetChildren(self).Any(c => c.Font.Value._spriteCharacters.ContainsKey(character));
        return true;
    }
    private static SpriteCharacterData OnGetCharacterData(GetCharacterData_orig orig, DynamicSpriteFont self, char character)
    {
        ChildFont[] children = GetChildren(self).Children;

        if (self._spriteCharacters.TryGetValue(character, out var value))
        {
            if (children is not null)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    ChildFont c = children[i];

                    if (c.OverrideParent is null || !c.OverrideParent(self))
                        continue;

                    Asset<DynamicSpriteFont> font = c.Font;

                    if (!font.IsLoaded)
                        font.Wait();

                    if (font.Value._spriteCharacters.TryGetValue(character, out var overriddenValue))
                    {
                        _currentlyDrawnFont = font.Value;
                        return overriddenValue;
                    }
                }
            }
            _currentlyDrawnFont = self;
            return value;
        }
        else if (children is not null)
        {
            for (int i = 0; i < children.Length; i++)
            {
                ChildFont c = children[i];

                Asset<DynamicSpriteFont> font = c.Font;

                if (!font.IsLoaded)
                    font.Wait();

                if (font.Value._spriteCharacters.TryGetValue(character, out var extendedValue))
                {
                    _currentlyDrawnFont = font.Value;
                    return extendedValue;
                }
            }
        }

        _currentlyDrawnFont = self;
        return self._defaultCharacterData;
    }
    private static void EditInternalDraw(ILContext il)
    {
        var c = new ILCursor(il);

        if (!c.TryFindNext(out _, i => i.MatchCall(getCharData)))
        {
            CharDataInlined = true;
            return;
        }
    }
    private static void EditMeasureString(ILContext il)
    {
        Mod mod = MoreLocales.Instance;
        var c = new ILCursor(il);

        if (c.TryFindNext(out _, i => i.MatchCall(getCharSpacing)))
            return;

        mod.Logger.Info("EditMeasureString: get_CharacterSpacing was inlined. Appropriate edits will take place.");

        if (!c.TryGotoNext(i => i.MatchLdfld(charSpacing)))
        {
            mod.Logger.Warn("EditMeasureString: Couldn't find access to _characterSpacing");
            return;
        }

        c.EmitPop();
        c.EmitLdsfld(typeof(FontHelperV2).GetField("_currentlyDrawnFont"));
    }
    private static float OnGetCharacterSpacing(get_CharacterSpacing_orig orig, DynamicSpriteFont self) => _currentlyDrawnFont._characterSpacing;
}
