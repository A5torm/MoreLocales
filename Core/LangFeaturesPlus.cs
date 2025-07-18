using MoreLocales.Config;
using System;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using System.Reflection;
using System.IO;
using System.Text;

namespace MoreLocales.Core
{
    /// <summary>
    /// Container for all features of Localization+ that are not (directly) related to extra language support.
    /// </summary>
    public static class LangFeaturesPlus
    {
        private const string StringToReplace = "{Prefix}";
        private static readonly string[] GenderNames = Enum.GetNames<GrammaticalGender>();
        private delegate void VoidsOrig();
        private delegate void HandleFileChangedOrRenamed_orig(string modName, string fileName);
        internal static int noFileWatcherTimer = 0;
        internal static void DoLoad()
        {
            // prefix stuff
            MonoModHooks.Modify(typeof(Item).GetMethod("get_Name"), RemovePrefixLiteralFromName);
            IL_Item.AffixName += LocalizedPrefixPosition;
            // comment stuff
            MonoModHooks.Add(typeof(LocalizationLoader).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic), UpdateLocalizationHook);
            MonoModHooks.Add(typeof(LocalizationLoader).GetMethod("HandleFileChangedOrRenamed", BindingFlags.Static | BindingFlags.NonPublic), FileWatcherHandlingHook);
        }
        private static void FileWatcherHandlingHook(HandleFileChangedOrRenamed_orig orig, string modName, string fileName)
        {
            if (noFileWatcherTimer > 0)
                return;
            
            orig(modName, fileName);
        }
        internal static string UniqueFileID(string modName, GameCulture culture, string filePrefix) => $"{modName}/{culture.Name}/{filePrefix}";
        private static void UpdateLocalizationHook(VoidsOrig orig)
        {
            if (!Main.dedServ)
                LangUtils.ConsumeCommentsQueue();

            if (noFileWatcherTimer > 0)
            {
                noFileWatcherTimer--;
                return;
            }

            orig();
        }
        internal static string RemovePrefixLiteral(string input)
        {
            int index = input.IndexOf(StringToReplace);
            if (index == -1)
                return input;

            if (index == 0) // beginning case
            {
                int start = StringToReplace.Length;

                if (input.Length > start && char.IsWhiteSpace(input[start]))
                    start++;

                return input[start..];
            }

            if (index + StringToReplace.Length == input.Length) // end case
            {
                int end = index;

                if (char.IsWhiteSpace(input[end - 1]))
                    end--;

                return input[..end];
            }

            // middle case

            string before = input[..index];
            string after = input[(index + StringToReplace.Length)..];

            if (char.IsWhiteSpace(before[^1]) && char.IsWhiteSpace(after[0]))
                after = after[1..];

            return before + after;
        }
        private static void RemovePrefixLiteralFromName(ILContext il)
        {
            Mod m = MoreLocales.Instance;
            try
            {
                var c = new ILCursor(il);

                c.GotoNext(i => i.MatchRet());

                c.EmitCall(typeof(LangFeaturesPlus).GetMethod(nameof(RemovePrefixLiteral), BindingFlags.Static | BindingFlags.NonPublic));
            }
            catch
            {
                MonoModHooks.DumpIL(m, il);
            }
        }
        private static void LocalizedPrefixPosition(ILContext il)
        {
            Mod m = MoreLocales.Instance;
            try
            {
                // this edit is a little loaded.
                // there's a case in this method specifically for prefix names that start with (. these names are formatted in a specific way in Terraria (at the end instead of at the start).
                // this case needs to be changed. instead of returning the end-formatted name, we make the case remove the parentheses, store the result, then jump to the normal case for further formatting.

                // for convenience, we can add the config value as a local
                var localConfigOption = new VariableDefinition(il.Import(typeof(bool)));
                il.Body.Variables.Add(localConfigOption);

                var c = new ILCursor(il);

                // init our local
                c.EmitLdsfld(typeof(ClientSideConfig).GetField(nameof(ClientSideConfig.Instance)));
                c.EmitLdfld(typeof(ClientSideConfig).GetField(nameof(ClientSideConfig.LocalizedPrefixPlacement)));
                c.EmitStloc(localConfigOption.Index);

                // let's load the correct (inflected) prefix value first
                if (!c.TryGotoNext(MoveType.After, i => i.MatchLdelemRef()))
                {
                    m.Logger.Warn("LocalizedPrefixPosition: Couldn't find original prefix load for replacement");
                    return;
                }
                c.EmitPop(); // pop the original localizedtext value before the string value is obtained from it
                c.EmitLdarg0(); // get the item
                c.EmitCall(typeof(LangFeaturesPlus).GetMethod(nameof(GetPrefixNameWithItemContext))); // get the new value

                // this is the label for the final case (last line of the method)
                ILLabel finalTextLabel = null;

                // first we get the final case label
                if (!c.TryGotoNext(i => i.MatchCallvirt(out _), i => i.MatchBrfalse(out finalTextLabel)))
                {
                    m.Logger.Warn("LocalizedPrefixPosition: Couldn't find final label for branching");
                    return;
                }

                // then we find where we can do our branching (inside the code block for the parentheses check)
                if (!c.TryGotoNext(i => i.MatchLdarg0(), i => i.MatchCall<Item>("get_Name"), i => i.MatchLdstr(" ")))
                {
                    m.Logger.Warn("LocalizedPrefixPosition: Couldn't find correct location for branching");
                    return;
                }

                // we'll make a label to skip our special parentheses removal. this is for making the config option work.
                var skipParenthesesRemovalLabel = il.DefineLabel();

                // now, we branch according to the config value
                c.EmitLdloc(localConfigOption.Index);

                c.EmitBrfalse(skipParenthesesRemovalLabel);

                // now, we do the parentheses thing
                c.EmitLdloc0(); // load the localized prefix string (we already know it's in parentheses)
                c.EmitDelegate<Func<string, string>>(s =>
                {
                    return s[1..^1]; // return the string without the first and last characters
                });
                c.EmitStloc0(); // store the cleaned-up string back in the local

                c.EmitBr(finalTextLabel);

                // mark the label to continue normally if the config option is off
                c.MarkLabel(skipParenthesesRemovalLabel);

                // this part of the edit is now done. something like "Espada corta de hierro (Pequeño)" will now show up as "Pequeño Espada corta de hierro".

                // part two: replacing occurences of {Prefix} with the actual prefix, and custom formatting.
                // remember that Item.Name now returns the item name with the {Prefix} literal removed, so we have to get the actual lang value.

                c.GotoLabel(finalTextLabel);

                // the original last case code will not run at all: now this label's target will be the code that we emit from here on

                c.EmitLdarg0(); // item
                c.EmitLdloc0(); // prefix name (sanitized)

                c.EmitDelegate<Func<Item, string, string>>((item, prefix) =>
                {
                    string realName = CultureHelper.GetRealName(item);

                    // custom position will take priority over localized order
                    if (realName.Contains(StringToReplace))
                        return realName.Replace(StringToReplace, prefix);

                    // localized order
                    AdjectiveOrder realOrder = MoreLocalesAPI.ActiveCulture.GrammarData.AdjectiveOrder;

                    return realOrder.Apply(realName, prefix);
                });

                c.EmitRet();
            }
            catch
            {
                MonoModHooks.DumpIL(m, il);
            }
        }
        /// <summary>
        /// Retrieves a LocalizedText that contains the gendered and pluralized form of a prefix depending on the item it's applied to (if applicable)
        /// </summary>
        /// <param name="context">The item.</param>
        public static LocalizedText GetPrefixNameWithItemContext(Item context)
        {
            int prefix = context.prefix;

            LocalizedText ogText = Lang.prefix[prefix];

            if (prefix == 0 || !ClientSideConfig.Instance.LocalizedPrefixGenderPluralization)
                return ogText;

            MoreLocalesSets.CachedInflectionData[context.type].Deconstruct(out GrammaticalGender gender, out Pluralization pluralization);

            if (!LanguageManager.Instance.ActiveCulture.InflectionDataChangesAdjectiveForm(gender, pluralization))
                return ogText; // adjective form stays the same

            bool vanilla = prefix < PrefixID.Count;
            ModPrefix modPrefix = null;

            if (!vanilla)
                modPrefix = PrefixLoader.GetPrefix(prefix);

            if (!(modPrefix?.Mod ?? MoreLocales.Instance).TryGetInflectionFileKey(out string inflectionFile))
                return ogText;

            string prefixName = vanilla ? PrefixID.Search.GetName(prefix) : modPrefix.Name;

            string genderName = GenderNames[(byte)gender];

            string englishName = vanilla ? LangUtils.GetVanillaLocalizationValues(ogText, GameCulture.DefaultCulture)[0] : string.Empty;
            string defaultFunc() => (vanilla ? englishName : ogText.Value);

            LocalizedText possiblyFinal = Language.GetOrRegister($"{inflectionFile}.Prefixes.{prefixName}.{genderName}", defaultFunc).WithFormatArgs((byte)pluralization);

            if (possiblyFinal.Value == englishName)
                return ogText;
            return possiblyFinal;
        }
        internal static void EnsureKeysForPrefixExist(int prefix, bool addComments)
        {
            bool vanilla = prefix < PrefixID.Count;
            ModPrefix modPrefix = null;

            if (!vanilla)
                modPrefix = PrefixLoader.GetPrefix(prefix);

            if (!(modPrefix?.Mod ?? MoreLocales.Instance).TryGetInflectionFileKey(out string inflectionFile))
                return;

            string prefixName = vanilla ? PrefixID.Search.GetName(prefix) : modPrefix.Name;

            string fullNoGender = $"{inflectionFile}.Prefixes.{prefixName}";

            for (int i = 0; i < GenderNames.Length; i++)
            {
                Language.GetOrRegister($"{fullNoGender}.{GenderNames[i]}", () => Lang.prefix[prefix].Value ?? prefixName);

                if (addComments)
                {
                    string commentBody =
                        vanilla
                        ? string.Join(" | ", LangUtils.GetVanillaLocalizationValues(Lang.prefix[prefix]))
                        : prefixName;
                    LangUtils.AddComment(fullNoGender, commentBody, HjsonCommentType.Hash);
                }
            }
        }
#pragma warning disable CS1572
        /// <summary>
        /// Checks if this culture changes the adjective form based on grammatical gender and/or pluralization of the noun.<para/>
        /// This is added to a custom culture via the <see cref="GrammarData"/> parameter when registering manually, or <see cref="ModCulture.ContextChangesAdjective(GrammaticalGender, Pluralization)"/> when using the autoloaded culture API.
        /// </summary>
        /// <param name="c">The culture to check.</param>
        /// <param name="data">The inflection data to check for.</param>
        /// <param name="gender">The grammatical gender to check for.</param>
        /// <param name="pluralization">The pluralization to check for.</param>
        /// <returns></returns>
#pragma warning restore
        public static bool InflectionDataChangesAdjectiveForm(this GameCulture c, InflectionData data)
        {
            data.Deconstruct(out GrammaticalGender gender, out Pluralization pluralization);
            return c.InflectionDataChangesAdjectiveForm(gender, pluralization);
        }
        /// <inheritdoc cref="InflectionDataChangesAdjectiveForm(GameCulture, InflectionData)"/>
        public static bool InflectionDataChangesAdjectiveForm(this GameCulture c, GrammaticalGender gender, Pluralization pluralization)
        {
            var possibleFunc = MoreLocalesAPI.extraCulturesV2[c.LegacyId].GrammarData.ContextChangesAdjective;
            if (possibleFunc is null)
                return true;
            return possibleFunc(gender, pluralization);
        }
        /// <summary>
        /// Only items that can be reforged should be able to affect adjectives.
        /// </summary>
        /// <param name="type">The type of the item to look up.</param>
        /// <returns>Whether or not this item can have prefixes for localization purposes.</returns>
        public static bool ItemIsGenderPluralizable(int type)
        {
            Item dummy = ContentSamples.ItemsByType[type];
            return dummy.CanHavePrefixes();
            /*
            if (type < ItemID.Count)
                return dummy.CanHavePrefixes();
            retur
            */
        }
        /// <summary>
        /// Gets this item type's current inflection data.
        /// </summary>
        /// <param name="type">Item type.</param>
        /// <param name="addComments">Add comments to the localization file or not.</param>
        /// <returns></returns>
        public static InflectionData GetItemInflection(int type, bool addComments = false)
        {
            if (!ItemIsGenderPluralizable(type))
                return InflectionData.Default;

            bool vanilla = type < ItemID.Count;

            ModItem modItem = null;
            if (!vanilla)
                modItem = ItemLoader.GetItem(type);

            string itemName = vanilla ? ItemID.Search.GetName(type) : modItem.Name;

            if (itemName == null)
                return InflectionData.Default;

            LocalizedText data = null;

            if (!(modItem?.Mod ?? MoreLocales.Instance).TryGetInflectionFileKey(out string inflectionFile))
                return InflectionData.Default;

            string key = $"{inflectionFile}.Items.{itemName}";
            data = Language.GetOrRegister(key, () => "/");

            if (addComments)
            {
                string commentBody =
                    vanilla
                    ? string.Join(" | ", LangUtils.GetVanillaLocalizationValues($"ItemName.{itemName}"))
                    : Lang.GetItemName(type).Value;
                LangUtils.AddComment(key, $"DisplayName: {commentBody}", HjsonCommentType.Hash);
            }

            if (TryParse(data.Value, out InflectionData inflectionData))
                return inflectionData;

            return InflectionData.Default;
        }
        /// <summary>
        /// Attempts to parse a string containing inflection data into <see cref="InflectionData"/>.
        /// </summary>
        /// <param name="value">The inflection data string.</param>
        /// <param name="result">The result of the parsing operation if successful.</param>
        /// <param name="sourceMod">The mod this value belongs to. If your mod contains pluralization aliases (set by the localizers), you must set this to your mod instance.</param>
        /// <returns>Whether or not the operation was successful.</returns>
        public static bool TryParse(string value, out InflectionData result, Mod sourceMod = null)
        {
            result = InflectionData.Default;

            string[] values = value.Split('/');
            if (values.Length == 0 || values.Length > 2)
                return false;

            uint finalGender = 0;

            // we want to default to 0 for an entry like "/M" for a language with adjective pluralization but no grammatical gender
            if (!string.IsNullOrEmpty(values[0]))
            {
                char gender = char.ToUpper(values[0][0]);

                finalGender = gender switch
                {
                    '0' or 'M' or 'C' => 0,
                    '1' or 'F' => 1,
                    '2' or 'N' => 2,
                    _ => 0
                };
            }

            uint finalPluralization = 0;

            // we want to default to 0 for an entry like "F/" or "F" for a language with grammatical gender but no adjective pluralization
            if (values.Length == 2 && !string.IsNullOrEmpty(values[1]))
            {
                char plural = char.ToUpper(values[1][0]);

                // special format
                if (values[1].Length > 1 && plural == 'P' && uint.TryParse(values[1].AsSpan(1), out uint specialResult))
                {
                    finalPluralization = specialResult;
                }
                else if (sourceMod != null)
                {
                    // custom alias support
                    if (!sourceMod.TryGetInflectionFileKey(out string inflectionFile))
                        return false;

                    LocalizedText customAliasEntry = Language.GetOrRegister($"{inflectionFile}.PluralizationAliases");

                    string[] aliasesCollection = new string[3];
                    if (!string.IsNullOrEmpty(customAliasEntry.Value)) // we have aliases
                    {
                        string[] aliases = customAliasEntry.Value.ToUpper().Split('/');

                        if (aliases.Length > aliasesCollection.Length)
                            Array.Resize(ref aliasesCollection, aliases.Length);

                        for (int i = 0; i < aliases.Length; i++)
                        {
                            string alias = aliases[i];
                            if (!string.IsNullOrEmpty(alias))
                            {
                                aliasesCollection[i] += alias;
                            }
                        }
                    }
                    // parse
                    for (int i = 0; i < aliasesCollection.Length; i++)
                    {
                        if (string.IsNullOrEmpty(aliasesCollection[i]))
                            aliasesCollection[i] = i switch
                            {
                                // main aliases
                                0 => "0/S",
                                1 => "1/P/F",
                                2 => "2/M",
                                _ => null,
                            };
                        if (aliasesCollection[i].Split("/").Contains(values[1].ToUpper()))
                        {
                            finalPluralization = (uint)i;
                            break;
                        }
                    }
                }
            }

            result |= (InflectionData)finalGender;
            result |= (InflectionData)(finalPluralization << 4);

            return true;
        }
        /// <summary>
        /// Deconstructs an <see cref="InflectionData"/> into its individual parts.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="gender"></param>
        /// <param name="pluralization"></param>
        public static void Deconstruct(this InflectionData data, out GrammaticalGender gender, out Pluralization pluralization)
        {
            gender = (GrammaticalGender)((byte)data & 0xF);
            pluralization = (Pluralization)((byte)data >> 4);
        }
        /// <summary>
        /// Tries to get the inflection file key for a given mod.<para/>
        /// A mod can choose to opt out of having an inflection file generated by calling <see cref="MoreLocales.ProtectModFromInflectionFileGeneration(Mod)"/><br/>
        /// or calling <see cref="Mod.Call(object[])"/> on MoreLocales' instance with the mod/mod name as a single argument. (Must be done during <see cref="Mod.Load"/> or earlier)
        /// </summary>
        /// <param name="target"></param>
        /// <param name="inflectionFileKey"></param>
        /// <returns></returns>
        public static bool TryGetInflectionFileKey(this Mod target, out string inflectionFileKey)
        {
            inflectionFileKey = null;

            if (Main.dedServ)
                return false;

            if (MoreLocales.inflectionFileKeys.TryGetValue(target, out string possibleInflectionFileKey))
            {
                if (possibleInflectionFileKey is null)
                    return false;
                inflectionFileKey = possibleInflectionFileKey;
                return true;
            }

            string possibleKey = target.GetLocalizationKey($"{(target == MoreLocales.Instance ? "VanillaData." : string.Empty)}InflectionData");

            if (LangUtils.CategoryExists(possibleKey) || Language.Exists($"{possibleKey}.PluralizationAliases"))
            {
                MoreLocales.inflectionFileKeys.Add(target, possibleKey);
                inflectionFileKey = possibleInflectionFileKey;
                return true;
            }

            // generate the file in ModPath/Localization if possible

            if (!LangUtils.ModIsValidForWriting(target))
                return false;

            string localizationFolderPath = Path.Combine(target.SourceFolder, "Localization");

            if (!Directory.Exists(localizationFolderPath))
                Directory.CreateDirectory(localizationFolderPath);

            string newFilePath = Path.Combine(localizationFolderPath, $"en-US_{target.GetLocalizationKey("InflectionData")}.hjson");

            File.WriteAllText(newFilePath, InflectionDataFileTemplate, Encoding.UTF8);

            // it doesn't matter if the file exists on disk now because it has to be packed into the actual mod for it to work, so return false
            // just a warn will work for now but i wonder if there's a better way to signal that the mod has to be rebuilt

            MoreLocales.Instance.Logger.Warn($"Inflection file has been generated for mod {target.Name}.\nIf you wish to opt out, read this: https://github.com/queueAngel/MoreLocales/wiki/MoreLocales-is-generating-a-localization-file-when-I-don't-want-it-to \nThe mod needs to be rebuilt in order for the file to do anything.");

            return false;
        }
        private const string InflectionDataFileTemplate = @"# This file contains custom localization data defined by LocalizationPlus. It was automatically generated.
# To opt out, read this: https://github.com/queueAngel/MoreLocales/wiki/MoreLocales-is-generating-a-localization-file-when-I-don't-want-it-to

# Gender and Pluralization (Used mainly for the Localized Prefixes config option)

# If your language does not have the concept of grammatical gender,
# and does not distinguish between one or many of a certain thing,
# you can leave this section of the file completely untouched.

# If your language has either, you can change the data fields that are necessary for your language.
# The gender & pluralization data entries are structured in the following way:

# Gender/Pluralization

# If you wish to let both fields use their default values, keep the entry as ""/"".
# Gender can similarly be skipped by simply leaving the gender field empty like so: ""/Pluralization"".
# And pluralization can be skipped like this: ""Gender"" or like this: ""Gender/"".


# Gender

# For gender, the following starting characters are valid. You may also write the internal number value instead.
# Gender defaults to 'M' if not specified.

# 'M'		(Masculine)		Internally - 0
# 'F'		(Feminine)		Internally - 1
# 'N'		(Neuter)		Internally - 2
# 'C'		(Common)		Internally - 0


# Pluralization

# Pluralization is a tad bit more complex. Internally, the game uses certain formulas to determine pluralization types.
# Refer to this list to view your language's pluralization rules and types:
# https://docs.translatehouse.org/projects/localization-guide/en/latest/l10n/pluralforms.html
# This might also be a good resource:
# https://www.unicode.org/cldr/charts/43/supplemental/language_plural_rules.html

# There are two ways of writing pluralization types: Using the provided aliases, or the special format.
# If you wish, you can add your own aliases for pluralization types if a type's value is larger than 2, or for whatever other reason. (Edit the value of PluralizationAliases)
# Otherwise, read below.

# For pluralization, the following starting characters are valid. You may also write the internal number value instead (except for the special format).
# Pluralization defaults to 'S' if not specified.

# 'S'		(Singular)		Internally - 0
# 'P'		(Plural)		Internally - 1
# 'F'		(Few)			Internally - 1
# 'M'		(Many)			Internally - 2
# You can also specify any pluralization type with the following special format:
# 'Pn', where 'n' is any pluralization type.
# For example, 'M' with the special format would be written 'P2'.

PluralizationAliases: """"
";
    }
    /// <summary>
    /// Container for grammatical gender and pluralization.
    /// </summary>
    public enum InflectionData : byte
    {
        /// <summary>
        /// No inflection.
        /// </summary>
        Default = 0,
    }
    /// <summary>
    /// Grammatical gender.
    /// </summary>
    public enum GrammaticalGender : byte
    {
        /// <summary>
        /// Masculine grammatical gender. Also known as Common gender in certain languages.
        /// </summary>
        Masculine = 0,//, Common = 0,
        /// <summary>
        /// Feminine grammatical gender.
        /// </summary>
        Feminine = 1,
        /// <summary>
        /// Neuter grammatical gender.
        /// </summary>
        Neuter = 2,
    }
    /// <summary>
    /// Grammatical pluralization.
    /// </summary>
    public enum Pluralization : byte
    {
        /// <summary>
        /// Singular noun.
        /// </summary>
        Singular = 0,
        /// <summary>
        /// Basic plural noun.
        /// </summary>
        Plural = 1,
        /// <summary>
        /// Basic plural noun (same value as <see cref="Plural"/>).
        /// </summary>
        Few = 1,
        /// <summary>
        /// 'Many' plural noun. Used in certain languages.
        /// </summary>
        Many = 2,
    }
}
