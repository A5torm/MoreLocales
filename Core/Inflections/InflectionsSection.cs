using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Terraria;

namespace MoreLocales.Core.Inflections
{
    /// <summary>
    /// Container to work easily with inflection data (gender and number) and paradigm.
    /// </summary>
    /// <param name="Inflection">Gender and number.</param>
    /// <param name="Paradigm">
    /// Paradigm.
    /// Essentially a specific form of a word that can have different inflections.<para/>
    /// Take Spanish '-o' vs '-ón' as an example.<br/>
    /// Both are masculine endings, but they are inflected differently from each other, so they are part of different paradigms.
    /// </param>
    public readonly record struct InflectionAndParadigm(InflectionData Inflection, int Paradigm = -1)
    {
        private static InflectionAndParadigm _none;
        public static InflectionAndParadigm None => _none;
    }
    /// <summary>
    /// Contains per-language methods to inflect nouns and adjectives based on grammatical gender and number.
    /// </summary>
    internal struct InflectionsSection
    {
        public InflectionPattern[] WordRecognizers;
        public Dictionary<InflectionData, InflectionPattern[]> InflectionRecognizers;
        public void Merge(in InflectionsSection other)
        {
            InflectionRecognizers = MiscHelper.MaybeMerge(InflectionRecognizers, other.InflectionRecognizers);
            WordRecognizers = MiscHelper.MaybeMerge(WordRecognizers, other.WordRecognizers);
        }
        public static bool Parse(string fileName, string name, in Dictionary<string, List<LPlusFileEntry>> raw, out InflectionsSection section)
        {
            section = default;
            if (raw is null || raw.Count > 2 || !SectionsHelper.Is<InflectionsSection>(in fileName, in name, out string[] tags))
                return false;

            var entries = raw["INFLECTIONS_META"];

            LPlusFileEntry? wordEntry = null;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.Key[0] == 'W')
                {
                    if (!wordEntry.HasValue)
                        wordEntry = entry;
                    else
                        throw new LPlusFileParsingException(LPlusError.UnexpectedEntry, null, default, entry.ToString());
                    entries.RemoveAt(i);
                }
            }

            section = new(fileName, in wordEntry, entries, raw.GetValueOrDefault("Exceptions"));
            return true;
        }
        public InflectionsSection(string fileName, in LPlusFileEntry? wordEntry, List<LPlusFileEntry> inflectionEntries, List<LPlusFileEntry> exceptionEntries)
        {
            // parse word recognizer patterns
            if (wordEntry.HasValue)
            {
                string[] word = wordEntry.Value.GetValues();

                WordRecognizers = new InflectionPattern[word.Length];
                for (int i = 0; i < word.Length; i++)
                {
                    if (!InflectionPattern.TryParse(word[i], out WordRecognizers[i]))
                        throw new LPlusFileParsingException(LPlusError.BadSimpleMatch, null, default, word[i]);
                }
            }

            // parse exception patterns
            List<InflectionException> exceptions = null;
            if (exceptionEntries != null)
            {
                exceptions = new(exceptionEntries.Count);
                foreach (var exceptionEntry in CollectionsMarshal.AsSpan(exceptionEntries))
                {
                    if (!LangFeaturesPlus.TryParseInflectionName(exceptionEntry.Key, out GrammaticalGender? g, out GrammaticalNumber? n) || (!g.HasValue || !n.HasValue))
                        throw new LPlusFileParsingException(LPlusError.BadEntryFormat, fileName, default, exceptionEntry.ToString());

                    InflectionData d = (InflectionData)g.Value | (InflectionData)((byte)n.Value << 4);
                    string[] exceptionsRaw = exceptionEntry.GetValues();
                    for (int i = 0; i < exceptionsRaw.Length; i++)
                    {
                        if (!InflectionPattern.TryParse(exceptionsRaw[i], out var exceptionPattern))
                            throw new LPlusFileParsingException(LPlusError.BadSimpleMatch, null, default, exceptionsRaw[i]);
                        exceptions.Add(new(exceptionPattern, d));
                    }
                }
            }

            InflectionRecognizers = [];
            foreach (var inflection in CollectionsMarshal.AsSpan(inflectionEntries))
            {
                if (!LangFeaturesPlus.TryParseInflectionName(inflection.Key, out GrammaticalGender? g, out GrammaticalNumber? n) || (!g.HasValue || !n.HasValue))
                    throw new LPlusFileParsingException(LPlusError.BadEntryFormat, fileName, default, inflection.ToString());

                InflectionData d = (InflectionData)g.Value | (InflectionData)((byte)n.Value << 4);
                string[] paradigms = inflection.GetValues();
                if (paradigms.Length == 0)
                    throw new LPlusFileParsingException(LPlusError.BadEntryFormat, fileName, default, inflection.ToString());
                InflectionPattern[] paradigmPattern = new InflectionPattern[paradigms.Length];
                for (int i = 0; i < paradigms.Length; i++)
                {
                    if (!InflectionPattern.TryParse(paradigms[i], out paradigmPattern[i], exceptions))
                        throw new LPlusFileParsingException(LPlusError.BadSimpleMatch, null, default, paradigms[i]);
                }

                InflectionRecognizers.Add(d, paradigmPattern);
            }
        }
        public readonly bool IsValidWord(ReadOnlySpan<char> word)
        {
            if (WordRecognizers is null)
                return true;
            for (int i = 0; i < WordRecognizers.Length; i++)
            {
                if (!WordRecognizers[i].TryMatch(word))
                    return false;
            }
            return true;
        }
        public readonly string ExtractFunctionalWord(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1 || WordRecognizers is null)
                return parts[0];
            string[] final = new string[parts.Length];
            int k = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsValidWord(parts[i]))
                    continue;
                final[k++] = parts[i];
            }
            return final[0];
        }
        public readonly bool Inflect(string word, InflectionData targetInflection, out string inflected) => Inflect(word, new InflectionAndParadigm(targetInflection, 0), out inflected);
        public readonly bool Inflect(string word, InflectionAndParadigm target, out string inflected, InflectionAndParadigm? sourceData = null)
        {
            inflected = word;
            InflectionPattern sourcePattern;

            if (sourceData.HasValue)
                sourcePattern = GetPattern(sourceData.Value.Inflection, sourceData.Value.Paradigm);
            else
                sourcePattern = GetPattern(word, out _);

            if (sourcePattern is null)
                return false;

            InflectionPattern targetPattern = GetPattern(target.Inflection, target.Paradigm);
            if (targetPattern is null)
                return false;

            if (!sourcePattern.TryReplace(word, targetPattern, out var result))
                return false;

            inflected = result.ToString();
            return true;
        }
        public readonly bool Inflect(string word, InflectionPattern source, InflectionAndParadigm target, out string inflected)
        {
            inflected = word;
            InflectionPattern targetPattern = GetPattern(target.Inflection, target.Paradigm);
            if (targetPattern is null)
                return false;

            if (!source.TryReplace(word, targetPattern, out var result))
                return false;

            inflected = result.ToString();
            return true;
        }
        public readonly InflectionPattern[] GetPatterns(InflectionData inflection)
        {
            if (InflectionRecognizers.TryGetValue(inflection, out var arr))
                return arr;
            return null;
        }
        public readonly InflectionPattern GetPattern(InflectionData inflection, int paradigm = 0)
        {
            var patterns = GetPatterns(inflection);
            if (patterns is null)
                return null;
            if (paradigm < 0 || paradigm >= patterns.Length)
                return null;
            return patterns[paradigm];
        }
        public readonly InflectionPattern GetPattern(InflectionData inflection, ReadOnlySpan<char> word, out InflectionAndParadigm data)
        {
            data = default;
            var arr = GetPatterns(inflection);
            if (arr is null)
                return null;
            InflectionPattern result = GetPattern(arr, word, out int paradigm);
            data = new(inflection, paradigm);
            return result;
        }
        public static InflectionPattern GetPattern(InflectionPattern[] arr, ReadOnlySpan<char> word, out int paradigm)
        {
            paradigm = -1;
            InflectionPattern bestMatch = null;
            for (int i = 0; i < arr.Length; i++)
            {
                InflectionPattern p = arr[i];

                if (p.TryMatch(word) && BetterMatch(ref bestMatch, in p))
                    paradigm = i;
            }
            return bestMatch;
        }
        public readonly InflectionPattern GetPattern(ReadOnlySpan<char> word, out InflectionAndParadigm data)
        {
            data = default;
            if (InflectionRecognizers is null)
                return null;
            InflectionPattern bestMatch = null;
            int paradigm = 0;
            InflectionData inflection = InflectionData.Default;
            foreach (var kvp in InflectionRecognizers)
            {
                InflectionPattern bestForInflection = GetPattern(kvp.Value, word, out var paradigm0);
                if (bestForInflection is null)
                    continue;
                BetterMatch(ref bestMatch, bestForInflection);
                inflection = kvp.Key;
                paradigm = paradigm0;
            }
            data = new(inflection, paradigm);
            return bestMatch;
        }
        public static bool BetterMatch(ref InflectionPattern latestBest, in InflectionPattern possibleNewBest)
        {
            if (latestBest is null || possibleNewBest.Match.Length > latestBest.Match.Length)
            {
                latestBest = possibleNewBest;
                return true;
            }
            return false;
        }
    }
    public readonly record struct InflectionException(InflectionPattern Pattern, InflectionData Data);
    public sealed class InflectionPattern
    {
        public InflectionException[] Exceptions;
        public InflectionPatternType Type;
        public RecognizableDiacriticType[] DiacriticsMap;
        public string Match;
        public bool Not;
        public InflectionPattern(InflectionPatternType type, string match, bool not, uint literalMask = 0u)
        {
            Type = type;
            Match = match;
            Not = not;
            DiacriticsMap = GenerateDiacriticsMap(match, literalMask);
        }
        public static RecognizableDiacriticType[] GenerateDiacriticsMap(string match, uint literalMask = 0u)
        {
            if (match is null)
                return null;
            RecognizableDiacriticType[] result = null;
            for (int i = 0; i < match.Length; i++)
            {
                if ((literalMask & (1u << i)) != 0)
                    continue;
                char c = match[i];
                if (!char.IsUpper(c))
                    continue;
                result ??= new RecognizableDiacriticType[match.Length];
                result[i] = c switch
                {
                    'N' => RecognizableDiacriticType.StrictNone,
                    'X' => RecognizableDiacriticType.AnyCharacter,
                    'G' => RecognizableDiacriticType.Grave,
                    'A' => RecognizableDiacriticType.Acute,
                    'C' => RecognizableDiacriticType.Circumflex,
                    'T' => RecognizableDiacriticType.Tilde,
                    'M' => RecognizableDiacriticType.Macron,
                    'B' => RecognizableDiacriticType.Breve,
                    'D' => RecognizableDiacriticType.Diaeresis,
                    'R' => RecognizableDiacriticType.Ring,
                    'K' => RecognizableDiacriticType.Caron,
                    'Q' => RecognizableDiacriticType.Comma,
                    'L' => RecognizableDiacriticType.Cedilla,
                    'O' => RecognizableDiacriticType.Ogonek,
                    _ => throw new InvalidOperationException($"Character '{c}' was not recognized as corresponding to any diacritic type.")
                };
            }
            return result;
        }
        public bool CheckException(ReadOnlySpan<char> word, out InflectionPattern pattern, out InflectionData inflection)
        {
            pattern = null;
            inflection = InflectionData.Default;
            if (Exceptions is null)
                return false;
            for (int i = 0; i < Exceptions.Length; i++)
            {
                ref var exception = ref Exceptions[i];
                if (exception.Pattern.TryMatch(word))
                {
                    pattern = exception.Pattern;
                    inflection = exception.Data;
                    return true;
                }
            }
            return false;
        }
        internal bool CheckStringEquality(ReadOnlySpan<char> word)
        {
            for (int i = 0; i < Match.Length; i++)
            {
                // get the diacritic map value
                char d = DiacriticsMap is null ? '\u0000' : (char)DiacriticsMap[i];
                // if matching to any character, continue
                if (d == '\u0002')
                    continue;
                // turn character to form that will actually be compared
                char c = char.ToLowerInvariant(word[i]);
                // most common check, direct character check
                if (d == 0 && Match[i] == c)
                    continue;
                // break up the character into its individual parts
                string normalized = c.ToString().Normalize(NormalizationForm.FormD);
                // if the length is one, that means this character has no diacritics, so it only matches for StrictNone
                if (normalized.Length == 1)
                {
                    if (d == '\u0001')
                        continue;
                    return false;
                }
                // bool to tell us if this character actually has that diacritic or not
                bool thisHasDiacritic = false;
                for (int j = 1; j < normalized.Length; j++)
                {
                    // this means the diacritic was within the diacritics
                    if (normalized[j] == d)
                    {
                        thisHasDiacritic = true;
                        break;
                    }
                }
                // if the diacritic wasn't found, it doesn't match
                if (!thisHasDiacritic)
                    return false;
            }
            return true;
        }
        public bool TryMatch(ReadOnlySpan<char> word)
        {
            if (word.Length == 0)
                return Not;

            Span<char> sp = stackalloc char[word.Length];
            // this makes using \ in a pattern to match specifically uppercase letters useless. maybe address this later?
            // though, only language i could see this being a problem for is *maybe* klingon (and even then idk cuz i don't speak it)
            word.ToLowerInvariant(sp);
            ReadOnlySpan<char> span = (ReadOnlySpan<char>)sp;

            return Type switch
            {
                InflectionPatternType.Whole => Match.Length != span.Length ? Not : CheckStringEquality(span) ^ Not,
                InflectionPatternType.Prefix => Match.Length > span.Length ? Not : CheckStringEquality(span.Slice(0, Match.Length)) ^ Not,
                InflectionPatternType.Suffix => Match.Length > span.Length ? Not : CheckStringEquality(span.Slice(span.Length - Match.Length)) ^ Not,
                InflectionPatternType.Infix => throw new NotSupportedException("Infixes cannot yet be used, sorry!"),
                _ => Not
            };
        }
        public bool TryRemove(ReadOnlySpan<char> word, out ReadOnlySpan<char> result)
        {
            result = word;
            if (!TryMatch(word))
                return false;

            result = Type switch
            {
                InflectionPatternType.Whole => string.Empty,
                InflectionPatternType.Prefix => word.Slice(Match.Length),
                InflectionPatternType.Suffix => word.Slice(0, word.Length - Match.Length),
                _ => throw new NotSupportedException($"Inflection pattern type '{Type}' is not yet supported!")
            };
            return true;
        }
        public bool TryReplace(ReadOnlySpan<char> word, ReadOnlySpan<char> replacement, out ReadOnlySpan<char> result, RecognizableDiacriticType[] replacementDiacritics = null)
        {
            result = word;
            if (!TryRemove(word, out ReadOnlySpan<char> removed))
                return false;
            char[] actualReplacement = null;

            if (replacementDiacritics != null)
            {
                for (int i = 0; i < replacement.Length; i++)
                {
                    var diacritic = replacementDiacritics[i];
                    if (diacritic == RecognizableDiacriticType.None)
                        continue;
                    actualReplacement ??= replacement.ToArray();
                    int indexInWord = Type switch
                    {
                        InflectionPatternType.Whole => i,
                        InflectionPatternType.Prefix => i + (Match.Length - replacement.Length),
                        InflectionPatternType.Suffix => i + removed.Length,
                        _ => throw new NotSupportedException($"Inflection pattern type '{Type}' is not yet supported!"),
                    };
                    if (indexInWord < 0 || indexInWord >= word.Length)
                        continue;
                    if (diacritic == RecognizableDiacriticType.AnyCharacter)
                        actualReplacement[i] = word[indexInWord];
                    else
                        TextHelper.TryAddDiacritic(word[indexInWord], diacritic, out actualReplacement[i]);
                }
            }
            if (actualReplacement != null)
                replacement = actualReplacement.AsSpan();
            result = Type switch
            {
                InflectionPatternType.Whole => replacement,
                InflectionPatternType.Prefix => MiscHelper.Merge(replacement, removed, out _),
                InflectionPatternType.Suffix => MiscHelper.Merge(removed, replacement, out _),
                _ => throw new NotSupportedException($"Inflection pattern type '{Type}' is not yet supported!"),
            };
            return true;
        }
        public bool TryReplace(ReadOnlySpan<char> word, in InflectionPattern replacement, out ReadOnlySpan<char> result)
        {
            result = word;
            if (Type != replacement.Type)
                return false;
            return TryReplace(word, replacement.Match, out result, replacement.DiacriticsMap);
        }
        public static bool TryParse(string pattern, out InflectionPattern result, List<InflectionException> exceptions = null)
        {
            result = default;
            if (string.IsNullOrEmpty(pattern))
                return false;

            if (pattern.Length == 1 && pattern[0] == 'X')
            {
                result = new InflectionPattern(InflectionPatternType.DoesntExist, null, false);
                return true;
            }

            // todo: support pointing to other columns/rows

            if (pattern.Length == 1)
            {
                result = new(InflectionPatternType.Whole, pattern, false);
            }
            string finalMatch = string.Empty;
            bool not = false;
            InflectionPatternType type = InflectionPatternType.Whole;
            uint literalMask = 0u;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '!':
                        if (i != 0)
                            return false;
                        not = true;
                        break;
                    case '-':
                        if (i == 0 || (i == 1 && not))
                        {
                            type = InflectionPatternType.Suffix;
                        }
                        else if (i == pattern.Length - 1)
                        {
                            if (type == InflectionPatternType.Suffix)
                                type = InflectionPatternType.Infix;
                            else
                                type = InflectionPatternType.Prefix;
                        }
                        else
                            return false;
                        break;
                    case '\\':
                        if (i == pattern.Length - 1)
                            break;
                        literalMask |= (1u << finalMatch.Length);
                        finalMatch += pattern[i + 1];
                        i++;
                        break;
                    default:
                        finalMatch += c;
                        break;
                }
            }
            result = new(type, finalMatch, not, literalMask);

            if (exceptions != null && exceptions.Count != 0)
            {
                InflectionException[] finalExceptions = new InflectionException[exceptions.Count];
                int count = 0;
                for (int i = exceptions.Count - 1; i >= 0; i--)
                {
                    InflectionException exception = exceptions[i];
                    if (result.TryMatch(exception.Pattern.Match))
                    {
                        finalExceptions[count++] = exception;
                        exceptions.RemoveAt(i);
                    }
                }
                Array.Resize(ref finalExceptions, count);
                result.Exceptions = finalExceptions;
            }
            return true;
        }
        /// <inheritdoc/>
        public override string ToString()
        {
            string startString = Not ? "!" : string.Empty;
            string endString = Type switch
            {
                InflectionPatternType.Whole => Match,
                InflectionPatternType.Prefix => $"{Match}-",
                InflectionPatternType.Suffix => $"-{Match}",
                InflectionPatternType.Infix => $"-{Match}-",
                _ => Match,
            };
            return startString + endString;
        }
    }
    /// <summary>
    /// Type of inflection pattern.<br/>
    /// Dictates how inflection patterns recognize a word and work with other inflection patterns.
    /// </summary>
    public enum InflectionPatternType
    {
        /// <summary>
        /// Represents a string of letter characters with word endings guaranteed at both ends.
        /// </summary>
        Whole,
        /// <summary>
        /// Represents a string of letter characters with a word ending guaranteed before it.
        /// </summary>
        Prefix,
        /// <summary>
        /// Represents a string of letter characters with a word ending guaranteed after it.
        /// </summary>
        Suffix,
        /// <summary>
        /// Represents a string of letter characters with no word endings guaranteed.
        /// </summary>
        Infix,
        /// <summary>
        /// Represents a pattern which doesn't exist in the main table. Marked as X in LPlus files.
        /// </summary>
        DoesntExist,
    }
    /// <summary>
    /// An enum containing all diacritics that can be specified for an <see cref="InflectionPattern"/> in an LPlus file.
    /// </summary>
    public enum RecognizableDiacriticType
    {
        /// <summary>
        /// Indicates indifference to whether or not a character has a diacritic.
        /// </summary>
        None = 0,
        /// <summary>
        /// Letters without diacritics.<para/>
        /// Written 'N' in LPlus files.
        /// </summary>
        StrictNone = 1,
        /// <summary>
        /// During the recognition stage, this will match to any character.<br/>
        /// During the generation stage, this will keep the original character at that position.<para/>
        /// Written 'X' in LPlus files.
        /// </summary>
        AnyCharacter = 2,
        /// <summary>
        /// Letters with the grave diacritic, e. g. Àà<para/>
        /// Written 'G' in LPlus files.
        /// </summary>
        Grave = '\u0300',
        /// <summary>
        /// Letters with the acute diacritic, e. g. Áá<para/>
        /// Written 'A' in LPlus files.
        /// </summary>
        Acute = '\u0301',
        /// <summary>
        /// Letters with the circumflex diacritic, e. g. Ââ<para/>
        /// Written 'C' in LPlus files.
        /// </summary>
        Circumflex = '\u0302',
        /// <summary>
        /// Letters with the tilde diacritic, e. g. Ãã<para/>
        /// Written 'T' in LPlus files.
        /// </summary>
        Tilde = '\u0303',
        /// <summary>
        /// Letters with the macron diacritic, e. g. Āā<para/>
        /// Written 'M' in LPlus files.
        /// </summary>
        Macron = '\u0304',
        /// <summary>
        /// Letters with the breve diacritic, e. g. Ăă<para/>
        /// Written 'B' in LPlus files.
        /// </summary>
        Breve = '\u0306',
        /// <summary>
        /// Letters with the diaeresis/umlaut diacritic, e. g. Ää<para/>
        /// Written 'D' in LPlus files.
        /// </summary>
        Diaeresis = '\u0308',
        /// <summary>
        /// Letters with the ring diacritic, e. g. Åå<para/>
        /// Written 'R' in LPlus files.
        /// </summary>
        Ring = '\u030A',
        /// <summary>
        /// Letters with the caron diacritic, e. g. Ǎǎ<para/>
        /// Written 'K' in LPlus files.
        /// </summary>
        Caron = '\u030C',
        /// <summary>
        /// Letters with the comma diacritic, e. g. Șș<para/>
        /// </summary>
        Comma = '\u0326',
        /// <summary>
        /// Letters with the cedilla diacritic, e. g. Çç<para/>
        /// Written 'L' in LPlus files.
        /// </summary>
        Cedilla = '\u0327',
        /// <summary>
        /// Letters with the ogonek diacritic, e. g. Ąą<para/>
        /// Written 'O' in LPlus files.
        /// </summary>
        Ogonek = '\u0328',
    }
    // le garbage
    /*
    public struct InflectionPattern
    {
        public InflectionSubpattern[] Subpatterns;
    }
    public struct InflectionSubpattern
    {
        public string RawSubpattern;
        public sbyte CharactersBefore;
        public sbyte CharactersAfter;
        public InflectionSubpatternRange[] Ranges;
        public bool Match(string word)
        {
            int wordLength = word.Length;
            int wordIndex
            int sliceIndex = 0;
            for (int i = 0; i < Ranges.Length; i++)
            {
                if (!Ranges[i].Match())
            }
        }
    }
    public struct InflectionSubpatternRange
    {
        internal const int Unit = 0;
        internal const int Any = 1;
        internal const int Not = 2;
        internal const int Uppercase = 3;
        internal const int Diacritic = 4;
        public byte Range;
        public SubpatternRecognitionType Type;
        public bool Match(ReadOnlySpan<char> range, ReadOnlySpan<char> matchTarget)
        {
            if (Type == SubpatternRecognitionType.StrictNone)
                return false;

            BitsByte b = (byte)Type;
            bool t = !b[Not];
            bool f = !t;

            if (b[Any])
            {
                if (b[Unit])
                    throw new Exception($"[MoreLocales] Unexpected error, please report to the developer of Localization+: {Type}, {range}, {matchTarget}");

                foreach (char c in range)
                    foreach (char c2 in matchTarget)
                        if (c == c2)
                            return t;
                return f;
            }
            
            if (b[Unit])
            {
                if (range.SequenceEqual(matchTarget))
                    return t;
            }

            return f;
        }
    }
    /// <summary>
    /// Defines a set of flags for subpattern ranges' success conditions.
    /// </summary>
    [Flags]
    public enum SubpatternRecognitionType : byte
    {
        /// <summary>
        /// Meta range. Is stored and passed to other ranges if connected.
        /// </summary>
        StrictNone = 0,
        // logical flags
        /// <summary>
        /// Take this entire subpattern as a single text unit that must match the original string exactly.
        /// </summary>
        Unit = 1,
        /// <summary>
        /// Take this entire subpattern as an array of characters, any of which could match the original string.
        /// </summary>
        Any = 2,
        /// <summary>
        /// This subpattern's result should be reversed (failure is success, and viceversa).
        /// </summary>
        Not = 4,
        // qualitative flags
        /// <summary>
        /// Match for uppercase.
        /// </summary>
        Uppercase = 8,
        /// <summary>
        /// Match for diacritics (can be specific or general).
        /// </summary>
        Normalized = 32,
    }
    */
}
