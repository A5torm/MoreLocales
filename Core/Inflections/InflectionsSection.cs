using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    public sealed class InflectionPattern(InflectionPatternType type, string match, bool not)
    {
        public InflectionException[] Exceptions;
        public InflectionPatternType Type = type;
        public string Match = match;
        public bool Not = not;
        public bool Single = type != InflectionPatternType.DoesntExist && match.Length == 1;
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
        public bool TryMatch(ReadOnlySpan<char> word)
        {
            if (word.Length == 0)
                return false ^ Not;
            if (Single)
            {
                char match = Match[0];
                switch (Type)
                {
                    case InflectionPatternType.Whole:
                        if (word.Length != 1)
                            return Not;
                        goto case InflectionPatternType.Prefix;
                    case InflectionPatternType.Prefix:
                        return (char.ToLowerInvariant(word[0]) == match) ^ Not;
                    case InflectionPatternType.Suffix:
                        return (char.ToLowerInvariant(word[^1]) == match) ^ Not;
                }
            }

            const StringComparison o = StringComparison.Ordinal;

            Span<char> sp = stackalloc char[word.Length];
            word.ToLowerInvariant(sp);
            ReadOnlySpan<char> span = (ReadOnlySpan<char>)sp;

            return Type switch
            {
                InflectionPatternType.Whole => span.Equals(Match, o) ^ Not,
                InflectionPatternType.Prefix => span.StartsWith(Match, o) ^ Not,
                InflectionPatternType.Suffix => span.EndsWith(Match, o) ^ Not,
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
        public bool TryReplace(ReadOnlySpan<char> word, ReadOnlySpan<char> replacement, out ReadOnlySpan<char> result)
        {
            result = word;
            if (!TryRemove(word, out ReadOnlySpan<char> removed))
                return false;

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
            return TryReplace(word, replacement.Match, out result);
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

            pattern = pattern.ToLowerInvariant();
            if (pattern.Length == 1)
            {
                result = new(InflectionPatternType.Whole, pattern, false);
            }
            int actualStartIndex = 0;
            int actualLength = 0;
            bool not = false;
            InflectionPatternType type = InflectionPatternType.Whole;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '!':
                        if (i != 0)
                            return false;
                        actualStartIndex++;
                        not = true;
                        break;
                    case '-':
                        if (i == 0 || (i == 1 && not))
                        {
                            actualStartIndex++;
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
                    default:
                        actualLength++;
                        break;
                }
            }
            result = new(type, pattern.Substring(actualStartIndex, actualLength), not);

            if (exceptions != null)
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
    public enum InflectionPatternType
    {
        Whole,
        Prefix,
        Suffix,
        Infix,
        DoesntExist,
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
            if (Type == SubpatternRecognitionType.None)
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
        None = 0,
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
