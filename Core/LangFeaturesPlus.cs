using MoreLocales.Config;
using MoreLocales.Core.Inflections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace MoreLocales.Core
{
    /// <summary>
    /// Container for all features of Localization+ that are not (directly) related to extra language support.
    /// </summary>
    public static partial class LangFeaturesPlus
    {
        private const string StringToReplace = "{Prefix}";
        private static readonly string[] GenderNames = Enum.GetNames<GrammaticalGender>();
        private static readonly string[] NumberNames = Enum.GetNames<GrammaticalNumber>();
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
        private static readonly LocalizedText DummyText = new(string.Empty, string.Empty);
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

            ref var data = ref MoreLocalesSets.CachedInflectionData[context.type];
            if (!data.Valid)
                return ogText;
            InflectionData inflection = data.Data.Inflection;
            inflection.Deconstruct(out var gender, out var pluralization);
            if (!LanguageManager.Instance.ActiveCulture.InflectionDataChangesAdjectiveForm(gender, pluralization))
                return ogText; // adjective form stays the same

            ref var prefixData = ref MoreLocalesSets.Prefixes[prefix];
            if (prefixData.Uninflectable)
                return ogText;

            if (!prefixData.EnsureExists(inflection))
                return ogText;

            DummyText.SetValue(prefixData.Get(inflection));
            return DummyText;
        }
        /// <summary>
        /// Allows you to work with what's usually used as a pluralization system (things like <c>"It has been {0} {^0:day;days}."</c>), but by supplying indices to those arrays (in this case <c>["day","days"]</c>) directly, meaning it can be used for direct pluralization via <see cref="GrammaticalNumber"/>, or even just dynamically choosing elements from each array to display stuff dynamically.<para/>
        /// This must be used <b>before</b> any formatting via <see cref="LocalizedText.Format(object[])"/>, <see cref="LocalizedText.WithFormatArgs(object[])"/>, etc.
        /// </summary>
        /// <param name="baseText"></param>
        /// <param name="indices"></param>
        /// <returns></returns>
        public static LocalizedText WithIndexFormat(this LocalizedText baseText, params int[] indices)
        {
            string key = baseText.Key;
            DirectPluralizationTextBinding key2 = new(key, indices);
            if (boundDirectPluralizeTextCache.TryGetValue(key2, out var value))
            {
                return value;
            }

            value = new LocalizedText(key, LanguageManager.Instance.GetTextValue(key));

            string finalFormatted = value.Value;
            bool hasPlurals = LocalizedText.PluralizationPatternRegex.IsMatch(finalFormatted);
            value._hasPlurals = hasPlurals;

            if (!hasPlurals)
                return baseText;

            finalFormatted = LocalizedText.PluralizationPatternRegex.Replace(finalFormatted, (m) =>
            {
                // in {^0:day;days}, this would be 0
                int num = Convert.ToInt32(m.Groups[1].Value);

                // in {^0:day;days}, this would be ["day", "days"]
                string[] array = m.Groups[2].Value.Split(';');

                // to get the real index, we get the index (from indices) at the index provided by the text itself
                // for example:
                // Text: {^0:I;We} haven't eaten for {^1:a day;a couple days;several days}!
                // let's say we want to get "We haven't eaten for several days!", so we supply [1, 2] as indices.
                // so, for the first match, the text accesses index 0, which is 1 in our array, and then we index into the string array with the obtained index to get "We"
                // same thing for the second one: the text accesses index 1, which we suppied as 2, and then we index into the string array to get "several days".
                int index = indices[num];
                return array[Math.Min(index, array.Length - 1)];
            });

            value.SetValue(finalFormatted);

            boundDirectPluralizeTextCache.Add(key2, value);

            return value;
        }
        internal static readonly Dictionary<DirectPluralizationTextBinding, LocalizedText> boundDirectPluralizeTextCache = [];
        internal record struct DirectPluralizationTextBinding(string Key, int[] Indices);
#pragma warning disable CS1572
        /// <summary>
        /// Checks if this culture changes the adjective form based on grammatical gender and/or pluralization of the noun.<para/>
        /// This is added to a custom culture via the <see cref="GrammarData"/> parameter when registering manually, or <see cref="ModCulture.ContextChangesAdjective(GrammaticalGender, GrammaticalNumber)"/> when using the autoloaded culture API.
        /// </summary>
        /// <param name="c">The culture to check.</param>
        /// <param name="data">The inflection data to check for.</param>
        /// <param name="gender">The grammatical gender to check for.</param>
        /// <param name="pluralization">The pluralization to check for.</param>
        /// <returns></returns>
#pragma warning restore
        public static bool InflectionDataChangesAdjectiveForm(this GameCulture c, InflectionData data)
        {
            data.Deconstruct(out GrammaticalGender gender, out GrammaticalNumber pluralization);
            return c.InflectionDataChangesAdjectiveForm(gender, pluralization);
        }
        /// <inheritdoc cref="InflectionDataChangesAdjectiveForm(GameCulture, InflectionData)"/>
        public static bool InflectionDataChangesAdjectiveForm(this GameCulture c, GrammaticalGender gender, GrammaticalNumber pluralization)
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
        /// Gets this item type's current pattern, inflection data, and paradigm.
        /// </summary>
        /// <param name="type">Item type.</param>
        /// <returns></returns>
        public static RecognizableWordData GetItemInflection(int type)
        {
            if (!ItemIsGenderPluralizable(type) || LPlusFile.Current is null)
                return RecognizableWordData.None;

            string name = Lang.GetItemNameValue(type);
            string functional = LPlusFile.Current.Inflections.ExtractFunctionalWord(name);

            var pattern = LPlusFile.Current.Inflections.GetPattern(functional, out var possibleData);
            if (pattern is null)
                return RecognizableWordData.None;
            if (pattern.CheckException(functional, out var exceptionPattern, out var exceptionInflection))
                return new(exceptionPattern, new(exceptionInflection, -1));
            return new(pattern, possibleData);
        }
        /// <summary>
        /// Maps characters to their corresponding gender.<br/>
        /// 'M' or 'C' are <see cref="GrammaticalGender.Masculine"/>, 'F' is <see cref="GrammaticalGender.Feminine"/>, and 'N' is <see cref="GrammaticalGender.Neuter"/>;
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="throwIfInvalid">Whether to throw an error if the character doesn't correspond to any grammatical gender.</param>
        /// <returns></returns>
        public static GrammaticalGender CharToGender(char c, bool throwIfInvalid = false)
        {
            switch (c)
            {
                case '0' or 'M' or 'C':
                    return GrammaticalGender.Masculine;
                case '1' or 'F':
                    return GrammaticalGender.Feminine;
                case '2' or 'N':
                    return GrammaticalGender.Neuter;
                default:
                    if (throwIfInvalid)
                        throw new Exception(MoreLocales.InvalidGrammaticalGenderError.Format(c));
                    return 0;
            }
        }
        /// <summary>
        /// Maps genders to their common representative characters (M, F, and N).
        /// </summary>
        /// <param name="g">The gender.</param>
        /// <param name="throwIfInvalid">Whether to throw an error if the gender isn't recognized.</param>
        /// <returns></returns>
        public static char GenderToChar(GrammaticalGender g, bool throwIfInvalid = false)
        {
            if (!Enum.IsDefined(g))
            {
                if (throwIfInvalid)
                    throw new Exception(MoreLocales.InvalidGrammaticalGenderError.Format(g));
                return 'M';
            }
            return g.ToString()[0];
        }
        private static readonly string SingularAliases = "0/S";
        private static readonly string PluralAliases = "1/P/F";
        private static readonly string ManyAliases = "2/M";
        private static readonly string[] DefaultAliases = [SingularAliases, PluralAliases, ManyAliases];
        /// <summary>
        /// Maps characters to their corresponding grammatical number.
        /// 'S' is <see cref="GrammaticalNumber.Singular"/>, 'P' or 'F' are <see cref="GrammaticalNumber.Plural"/>, and 'M' is <see cref="GrammaticalNumber.Many"/>.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="throwIfInvalid">Whether to throw an error if the character doesn't correspond to any default grammatical number.</param>
        /// <returns></returns>
        public static GrammaticalNumber CharToNumber(char c, bool throwIfInvalid = false)
        {
            for (int i = 0; i < DefaultAliases.Length; i++)
            {
                string[] aliases = DefaultAliases[i].Split('/');
                for (int j = 0; j < aliases.Length; j++)
                    if (aliases[j][0] == c)
                        return (GrammaticalNumber)i;
            }
            if (throwIfInvalid)
                throw new Exception(MoreLocales.InvalidGrammaticalNumberError.Format(c));
            return 0;
        }
        /// <summary>
        /// Maps a <see cref="GrammaticalNumber"/> to its corresponding alias string. For parsing from text files.
        /// </summary>
        public static string NumberToAliases(GrammaticalNumber n)
        {
            if (n > GrammaticalNumber.Many)
                return null;
            return DefaultAliases[(byte)n];
        }
        /// <summary>
        /// Maps a <see cref="GrammaticalNumber"/> to its corresponding character.
        /// </summary>
        /// <param name="n"></param>
        /// <param name="throwIfInvalid"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static char NumberToChar(GrammaticalNumber n, bool throwIfInvalid = false)
        {
            if (!Enum.IsDefined(n))
            {
                if (throwIfInvalid)
                    throw new Exception(MoreLocales.InvalidGrammaticalNumberError.Format(n));
                return 'S';
            }
            if ((int)n == 1)
                return 'P';
            return n.ToString()[0];
        }
        private static readonly Dictionary<string, GrammaticalGender> _specialGenderAbbv = new()
        {
            { "Cmn", GrammaticalGender.Common },
            { "Nt", GrammaticalGender.Neuter },
        };
        private static readonly Dictionary<string, GrammaticalNumber> _specialNumberAbbv = new(StringComparer.Ordinal)
        {
            { "Sg", GrammaticalNumber.Singular },
            { "Fw", GrammaticalNumber.Few },
            { "Mn", GrammaticalNumber.Many },
        };
        /// <summary>
        /// Splits a string by capital letters.
        /// </summary>
        public static readonly Regex SplitByCapitalLetters = SplitByCapitals();
        /// <summary>
        /// Tries to parse some inflection name (like MascSg, FemPl, NeutMn), abbreviated or otherwise, and in any order.
        /// </summary>
        /// <param name="inflection">An inflection name.</param>
        /// <param name="gender">Maybe gender.</param>
        /// <param name="number">Maybe number.</param>
        /// <returns>Whether or not the inflection name was successfully parsed.</returns>
        public static bool TryParseInflectionName(ReadOnlySpan<char> inflection, out GrammaticalGender? gender, out GrammaticalNumber? number)
        {
            gender = null;
            number = null;
            int i = 0;
            foreach (var match in SplitByCapitalLetters.EnumerateMatches(inflection))
            {
                if (++i > 2)
                    return false;
                ReadOnlySpan<char> subname = inflection.Slice(match.Index, match.Length);
                if (match.Length < 4)
                {
                    string s = subname.ToString(); // TODO: when tmod moves to .NET 10, this allocation won't be necessary due to IAlternateEqualityComparer
                    if (_specialGenderAbbv.TryGetValue(s, out GrammaticalGender g))
                        gender = g;
                    else if (_specialNumberAbbv.TryGetValue(s, out GrammaticalNumber n))
                        number = n;
                }
                if (!gender.HasValue)
                {
                    var almostGender = ParsePartialName(in GenderNames, in subname);
                    if (almostGender != null)
                        gender = Enum.Parse<GrammaticalGender>(almostGender);
                }
                if (!number.HasValue)
                {
                    var almostNumber = ParsePartialName(in NumberNames, in subname);
                    if (almostNumber != null)
                        number = Enum.Parse<GrammaticalNumber>(almostNumber);
                }
            }
            return true;
        }
        /// <summary>
        /// Tries to parse some gender name (like Masc, Fem, Neut), abbreviated or otherwise.
        /// </summary>
        /// <param name="genderName">A gender name.</param>
        /// <param name="gender">The parsed gender.</param>
        /// <returns>Whether or not the gender name was successfully parsed.</returns>
        public static bool TryParseGenderName(ReadOnlySpan<char> genderName, out GrammaticalGender gender)
        {
            gender = default;
            if (genderName.Length < 4 && _specialGenderAbbv.TryGetValue(genderName.ToString(), out gender)) // TODO: when tmod moves to .NET 10, this allocation won't be necessary due to IAlternateEqualityComparer
                return true;
            var almostGender = ParsePartialName(in GenderNames, in genderName);
            if (almostGender == null)
                return false;
            gender = Enum.Parse<GrammaticalGender>(almostGender);
            return true;
        }
        /// <summary>
        /// Tries to parse some number name (like Sg, Pl, Mn), abbreviated or otherwise.
        /// </summary>
        /// <param name="numberName">A number name.</param>
        /// <param name="number">The parsed number.</param>
        /// <returns>Whether or not the number name was successfully parsed.</returns>
        public static bool TryParseNumberName(ReadOnlySpan<char> numberName, out GrammaticalNumber number)
        {
            number = default;
            if (numberName.Length < 3 && _specialNumberAbbv.TryGetValue(numberName.ToString(), out number)) // TODO: when tmod moves to .NET 10, this allocation won't be necessary due to IAlternateEqualityComparer
                return true;
            var almostNumber = ParsePartialName(in NumberNames, in numberName);
            if (almostNumber == null)
                return false;
            number = Enum.Parse<GrammaticalNumber>(almostNumber);
            return true;
        }
        private static ReadOnlySpan<char> ParsePartialName(in string[] names, in ReadOnlySpan<char> subname)
        {
            for (int i = 0; i < names.Length; i++)
            {
                ReadOnlySpan<char> name = names[i];
                for (int j = name.Length; j >= 1; j--)
                {
                    ReadOnlySpan<char> partialName = name.Slice(0, j);
                    if (partialName.Equals(subname, StringComparison.Ordinal))
                        return name;
                }
            }
            return null;
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

            GrammaticalGender finalGender = GrammaticalGender.Masculine;

            // we want to default to 0 for an entry like "/M" for a language with adjective pluralization but no grammatical gender
            if (!string.IsNullOrEmpty(values[0]))
            {
                char gender = char.ToUpper(values[0][0]);

                finalGender = CharToGender(gender);
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
                    string[] aliasesCollection = new string[3];
                    // parse
                    for (int i = 0; i < aliasesCollection.Length; i++)
                    {
                        if (string.IsNullOrEmpty(aliasesCollection[i]))
                            aliasesCollection[i] = NumberToAliases((GrammaticalNumber)i);
                        if (aliasesCollection[i].Split('/').Contains(values[1].ToUpper()))
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
        public static void Deconstruct(this InflectionData data, out GrammaticalGender gender, out GrammaticalNumber pluralization)
        {
            gender = (GrammaticalGender)((byte)data & 0xF);
            pluralization = (GrammaticalNumber)((byte)data >> 4);
        }
        /// <summary>
        /// Mutates <paramref name="baseData"/> with the values provided for gender and number if they're not null;
        /// </summary>
        /// <param name="baseData">The inflection data to mutate.</param>
        /// <param name="maybeGender">The gender to mutate it with.</param>
        /// <param name="maybeNumber">The number to mutate it with.</param>
        /// <returns></returns>
        public static void Set(this ref InflectionData baseData, GrammaticalGender? maybeGender = null, GrammaticalNumber? maybeNumber = null)
        {
            if (maybeGender.HasValue)
            {
                if (maybeNumber.HasValue)
                {
                    baseData = (InflectionData)((byte)maybeGender.Value | ((byte)maybeNumber.Value << 4));
                    return;
                }
                baseData.Set(maybeGender.Value);
            }
            if (maybeNumber.HasValue)
                baseData.Set(maybeNumber.Value);
        }
        public static void Set(this ref InflectionData baseData, GrammaticalGender gender)
        {
            baseData = (InflectionData)(((byte)baseData & 0xF0) | (byte)gender);
        }
        public static void Set(this ref InflectionData baseData, GrammaticalNumber number)
        {
            baseData = (InflectionData)(((byte)baseData & 0xF) | ((byte)number << 4));
        }
        [GeneratedRegex(@"\p{Lu}\p{Ll}*")]
        private static partial Regex SplitByCapitals();
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
        MascSg = 0,
        MascPl = 0b_00010000,
        FemSg = 0b_00000001,
        FemPl = 0b_00010001,
    }
    /// <summary>
    /// Grammatical gender.
    /// </summary>
    public enum GrammaticalGender : byte
    {
        /// <summary>
        /// Masculine grammatical gender. Also known as Common gender in certain languages.
        /// </summary>
        Masculine = 0,
        /// <summary>
        /// Common (masculine) grammatical gender. (same value as <see cref="Masculine"/>)
        /// </summary>
        Common = 0,
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
    public enum GrammaticalNumber : byte
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
