using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.Localization;

namespace MoreLocales.Common;

internal class DefaultUIAdjustments : ILoadable
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_descriptionText")]
    public static extern ref UIText GetHubUIText(UIWorkshopHub instance);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_isWrapped")]
    public static extern ref bool GetIsWrapped(UIText instance);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_visibleText")]
    public static extern ref string GetVisibleText(UIText instance);
    void ILoadable.Load(Mod mod)
    {
        IL_UIWorkshopHub.OnInitialize += InitializeWorkshopHub;
        IL_UICharacterCreation.BuildPage += InitializeCharCreationUI;
    }
    private static readonly HashSet<CultureNamePlus> bigWorkshopHub = [CultureNamePlus.Indonesian, CultureNamePlus.Thai, CultureNamePlus.Danish];
    private static readonly HashSet<CultureNamePlus> bigCharCreationUI = [CultureNamePlus.Indonesian];
    public static bool NeedsBigWorkshopHub
    {
        get
        {
            GameCulture current = LanguageManager.Instance.ActiveCulture;
            return bigWorkshopHub.Contains((CultureNamePlus)current.LegacyId);
        }
    }
    public static bool NeedsBigCharacterCreationUI
    {
        get
        {
            GameCulture current = LanguageManager.Instance.ActiveCulture;
            return bigCharCreationUI.Contains((CultureNamePlus)current.LegacyId);
        }
    }
    private static void InitializeCharCreationUI(ILContext il)
    {
        Mod mod = MoreLocales.Instance;
        try
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(i => i.MatchAdd()))
            {
                mod.Logger.Warn("InitializeCharCreationUI: Couldn't find instruction to add to");
                return;
            }
            c.EmitDelegate(() =>
            {
                if (NeedsBigCharacterCreationUI)
                    return 20;
                return 0;
            });
            c.EmitAdd();
        }
        catch
        {
            MonoModHooks.DumpIL(mod, il);
        }
    }

    private static void InitializeWorkshopHub(ILContext il)
    {
        Mod mod = MoreLocales.Instance;
        try
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(MoveType.After, i => i.MatchLdcI4(20)))
            {
                mod.Logger.Warn("InitializeWorkshopHub: Couldn't find instruction 0 to add to");
                return;
            }
            c.EmitDelegate(() =>
            {
                if (NeedsBigWorkshopHub)
                    return -10;
                return 0;
            });
            c.EmitAdd();
            if (!c.TryGotoNext(MoveType.After, i => i.MatchLdloc2(), i => i.MatchSub()))
            {
                mod.Logger.Warn("InitializeWorkshopHub: Couldn't find instruction 1 to add to");
                return;
            }
            c.EmitDelegate(() =>
            {
                if (NeedsBigWorkshopHub)
                    return 10;
                return 0;
            });
            c.EmitAdd();
        }
        catch
        {
            MonoModHooks.DumpIL(mod, il);
        }
    }
    void ILoadable.Unload()
    {

    }
}
