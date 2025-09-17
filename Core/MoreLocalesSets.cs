using MoreLocales.Core.Inflections;
using System.Collections.Generic;
using Terraria.ID;
using Terraria.Localization;

namespace MoreLocales.Core
{
    /// <summary>
    /// Holds the data for a read-only word that can be used to inflect other words.
    /// </summary>
    public struct RecognizableWordData(InflectionPattern pattern, InflectionAndParadigm data)
    {
        private static readonly RecognizableWordData _none;
        /// <summary>
        /// Whether this is valid data or not.
        /// </summary>
        public readonly bool Valid => Pattern != null;
        /// <summary>
        /// No data.
        /// </summary>
        public static RecognizableWordData None => _none;
        /// <summary>
        /// The pattern that matches this word.
        /// </summary>
        public readonly InflectionPattern Pattern = pattern;
        /// <summary>
        /// The inflection and paradigm of this word.
        /// </summary>
        public InflectionAndParadigm Data = data;
    }
    /// <summary>
    /// Holds the data for a word's base form and its inflections.
    /// </summary>
    public readonly struct InflectableWord
    {
        public enum BracketType : byte
        {
            None,
            Parentheses,
            CurlyBrackets,
            AngleBrackets,
            SquareBrackets,
        }
        /// <summary>
        /// The base form of the word.
        /// </summary>
        public readonly string BaseForm;
        /// <summary>
        /// Whether this word is inflectable or not.
        /// </summary>
        public readonly bool Uninflectable;
        /// <summary>
        /// The base pattern that matches this word.
        /// </summary>
        public readonly InflectionPattern BasePattern;
        /// <summary>
        /// The initial inflection and paradigm of this word.
        /// </summary>
        public readonly InflectionAndParadigm BaseData;
        /// <summary>
        /// Alternate forms of this word mapped by inflection.
        /// </summary>
        public readonly Dictionary<InflectionData, string> AlternateForms;
        /// <summary>
        /// Words with brackets will be sanitized and the bracket type will be here.
        /// </summary>
        public readonly BracketType Brackets;
        /// <summary>
        /// Creates a new instance of <see cref="InflectableWord"/> by providing the base form of a word.
        /// </summary>
        /// <param name="baseForm"></param>
        public InflectableWord(string baseForm)
        {
            if (string.IsNullOrWhiteSpace(baseForm) || LPlusFile.Current is null)
            {
                Uninflectable = true;
                return;
            }

            Brackets = baseForm[0] switch
            {
                '(' => BracketType.Parentheses,
                '{' => BracketType.CurlyBrackets,
                '<' => BracketType.AngleBrackets,
                '[' => BracketType.SquareBrackets,
                _ => BracketType.None,
            };
            if (Brackets != BracketType.None)
                baseForm = baseForm[1..^1];
            
            var possiblePattern = LPlusFile.Current.Inflections.GetPattern(baseForm, out var possibleBaseData);
            if (possiblePattern is null)
            {
                Uninflectable = true;
                return;
            }
            BaseForm = baseForm;
            BasePattern = possiblePattern;
            BaseData = possibleBaseData;
            AlternateForms = [];
        }
        /// <summary>
        /// Ensures the existence (or non-existence) of the inflected version of this word.
        /// </summary>
        /// <param name="inflectionData">The inflection to ensure existence for.</param>
        /// <returns>Whether or not the inflection exists.</returns>
        public readonly bool EnsureExists(InflectionData inflectionData)
        {
            if (Uninflectable)
                return false;
            if (!AlternateForms.TryGetValue(inflectionData, out string possibleInflectedForm))
            {
                if (!LPlusFile.Current.Inflections.Inflect(BaseForm, BasePattern, new InflectionAndParadigm(inflectionData, BaseData.Paradigm), out possibleInflectedForm))
                    possibleInflectedForm = "X";
                AlternateForms.Add(inflectionData, possibleInflectedForm);
            }
            return possibleInflectedForm[0] != 'X';
        }
        /// <summary>
        /// Retrieves the requested value. If the inflection isn't found, the base form will be returned.<br/>
        /// To ensure that an inflection exists, use <see cref="EnsureExists(InflectionData)"/> first.
        /// </summary>
        /// <param name="inflection">Inflection. Leave null to retrieve base form.</param>
        /// <param name="withBrackets">Whether or not to return this word formatted with the stored bracket type.</param>
        /// <returns></returns>
        public readonly string Get(InflectionData? inflection = null, bool withBrackets = true)
        {
            string finalWord = BaseForm;
            if (inflection.HasValue && !Uninflectable && AlternateForms.TryGetValue(inflection.Value, out string inflected))
                finalWord = inflected;
            BracketType b = withBrackets ? Brackets : BracketType.None;
            return b switch
            {
                BracketType.None => finalWord,
                BracketType.Parentheses => $"({finalWord})",
                BracketType.CurlyBrackets => $"{{{finalWord}}}",
                BracketType.AngleBrackets => $"<{finalWord}>",
                BracketType.SquareBrackets => $"[{finalWord}]",
                _ => finalWord,
            };
        }
    }
    /// <summary>
    /// sorry nothing
    /// </summary>
    public static class MoreLocalesSets
    {
        internal static bool _didFirstLoad = false;
        internal static readonly RecognizableWordData[] CachedInflectionData = ItemID.Sets.Factory.CreateCustomSet(RecognizableWordData.None);
        internal static readonly InflectableWord[] Prefixes = PrefixID.Sets.Factory.CreateCustomSet(default(InflectableWord));
        internal static void ReloadedLocalizations()
        {
            // AddComment actually causes files to be reloaded so i need to take that into account
            if (!_didFirstLoad)
                return;
            for (int i = 1; i < CachedInflectionData.Length; i++)
            {
                CachedInflectionData[i] = LangFeaturesPlus.GetItemInflection(i);
            }
            for (int i = 1; i < Prefixes.Length; i++)
            {
                LocalizedText prefix = Terraria.Lang.prefix[i];
                if (prefix == null)
                    continue;
                Prefixes[i] = new InflectableWord(prefix.Value);
            }
        }
    }
}
