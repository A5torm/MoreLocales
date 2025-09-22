using MoreLocales.Core.Inflections;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace MoreLocales.Common;

internal sealed class TestPrefixesCommand : ModCommand, ILocalizedModType
{
    public override bool IsCaseSensitive => true;
    public override string Command => "testprefixes";
    public override CommandType Type => CommandType.Chat;
    public string LocalizationCategory => "Commands";
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (LPlusFile.Current is null)
        {
            ref MoreLocalesCulture c = ref MoreLocalesAPI.ActiveCulture;
            caller.Reply(this.GetLocalization("Error.LanguageNoInflections").Format(c.Name, c.Culture.Name));
            return;
        }
        if (args.Length < 1)
        {
            caller.Reply(this.GetLocalizedValue("Error.NoArgs"));
            return;
        }
        string itemID = args[0];
        int item = 0;
        if (itemID.Equals("-h", System.StringComparison.Ordinal))
            item = caller.Player.HeldItem.type;
        else if (int.TryParse(itemID, out int numberID))
            item = numberID;
        else if (ItemID.Search.TryGetId(itemID, out int numberID0))
            item = numberID0;
        else
        {
            caller.Reply(this.GetLocalization("Error.UnknownItem").Format(itemID));
            return;
        }

        ref RecognizableWordData w = ref MoreLocalesSets.CachedInflectionData[item];
        caller.Reply(this.GetLocalization("Success.BasicInfo").Format(ItemID.Search.GetName(item), w.Data.Inflection, w.Data.Paradigm));

        Item dummy = ContentSamples.ItemsByType[item];
        int pre = dummy.prefix;
        for (int i = 1; i < PrefixLoader.PrefixCount; i++)
        {
            if (!dummy.CanApplyPrefix(i))
                continue;
            dummy.prefix = i;
            LocalizedText prefix = Lang.prefix[i];
            if (prefix is null || string.IsNullOrWhiteSpace(prefix.Value))
                continue;
            caller.Reply(dummy.HoverName);
        }
        dummy.prefix = pre;
    }
}
