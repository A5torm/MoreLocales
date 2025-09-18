using MoreLocales.Core.Inflections;
using System;
using Terraria;
using Terraria.ID;

namespace MoreLocales.Common
{
    internal sealed class ItemInflectionsCommand : ModCommand, ILocalizedModType
    {
        public string LocalizationCategory => "Commands";
        public override string Command => "iteminflections";
        public override CommandType Type => CommandType.Chat | CommandType.Console;
        public override void Action(CommandCaller caller, string input, string[] args)
        {
            bool localConsole = args.Length > 0 && args[0].Equals("-c", StringComparison.Ordinal);
            if (LPlusFile.Current is null)
            {
                ref MoreLocalesCulture c = ref MoreLocalesAPI.ActiveCulture;
                caller.Reply(this.GetLocalization("Error.LanguageNoInflections").Format(c.Name, c.Culture.Name));
                return;
            }
            var items = MoreLocalesSets.CachedInflectionData;
            for (int i = 1; i < items.Length; i++)
            {
                if (!LangFeaturesPlus.ItemIsGenderPluralizable(i))
                    continue;
                ref var item = ref items[i];
                string itemName = ItemID.Search.GetName(i);
                string itemNameLocalized = Lang.GetItemNameValue(i);
                string final = $"{itemNameLocalized} ({itemName}): {item.Data.Inflection}/{item.Data.Paradigm}";
                if (localConsole)
                    Console.WriteLine(final);
                else
                    caller.Reply(final);
            }
            if (caller.CommandType == CommandType.Chat && !localConsole)
                caller.Reply(this.GetLocalizedValue("Advice"));
        }
    }
}
