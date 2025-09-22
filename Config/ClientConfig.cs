using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace MoreLocales.Config;

/// <summary>
/// MoreLocales configuration.
/// </summary>
public class ClientSideConfig : ModConfig
{
    /// <inheritdoc/>
    public override ConfigScope Mode => ConfigScope.ClientSide;
#pragma warning disable
    public static ClientSideConfig Instance;
#pragma warning restore 
    /// <summary>
    /// Related to formatting via <see cref="AdjectiveOrder"/>.
    /// </summary>
    [Header("$Mods.MoreLocales.Configs.Headers.Features")]
    [DefaultValue(false)]
    public bool LocalizedPrefixPlacement;

    /// <summary>
    /// Related to formatting via <see cref="LangFeaturesPlus.GetPrefixNameWithItemContext(Terraria.Item)"/>.
    /// </summary>
    [DefaultValue(true)]
    public bool LocalizedPrefixGenderPluralization;

    /// <summary>
    /// Forces a font style (only does anything for CJK fonts).
    /// </summary>
    [Header("$Mods.MoreLocales.Configs.Headers.Fonts")]
    [DefaultValue(LocalizedFont.None)]
    [DrawTicks]
    public LocalizedFont ForcedFont;
}
