using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MoreLocales.Core.Inflections;

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
    private static readonly InflectionAndParadigm _none;
    /// <summary>
    /// Default inflection, paradigm 0.
    /// </summary>
    public static InflectionAndParadigm None => _none;
}
/// <summary>
/// Contains per-language methods to inflect nouns and adjectives based on grammatical gender and number.
/// </summary>
internal struct InflectionsSection
{
    public GrammaticalGender[] ExistingGenders;
    public GrammaticalNumber[] ExistingNumbers;
    public InflectionPattern[] WordRecognizers;
    public Dictionary<InflectionData, InflectionPattern[]> InflectionRecognizers;
    public void Merge(in InflectionsSection other)
    {
        InflectionRecognizers = MiscHelper.MaybeMerge(InflectionRecognizers, other.InflectionRecognizers);
        WordRecognizers = MiscHelper.MaybeMerge(WordRecognizers, other.WordRecognizers);
        ExistingGenders = MiscHelper.MaybeMerge(ExistingGenders, other.ExistingGenders);
        ExistingNumbers = MiscHelper.MaybeMerge(ExistingNumbers, other.ExistingNumbers);
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
        HashSet<GrammaticalGender> tempGenders = new(2);
        HashSet<GrammaticalNumber> tempNumbers = new(2);

        foreach (var inflection in CollectionsMarshal.AsSpan(inflectionEntries))
        {
            if (!LangFeaturesPlus.TryParseInflectionName(inflection.Key, out GrammaticalGender? g, out GrammaticalNumber? n) || (!g.HasValue || !n.HasValue))
                throw new LPlusFileParsingException(LPlusError.BadEntryFormat, fileName, default, inflection.ToString());

            GrammaticalGender gen = g.Value;
            GrammaticalNumber num = n.Value;

            tempGenders.Add(gen);
            tempNumbers.Add(num);

            InflectionData d = (InflectionData)gen | (InflectionData)((byte)num << 4);
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

        ExistingGenders = tempGenders.ToArray();
        ExistingNumbers = tempNumbers.ToArray();
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
    public readonly InflectionPattern GetPattern(InflectionData inflection, int paradigm = 0, bool strict = false)
    {
        var patterns = GetPatterns(inflection);
        if (patterns is null)
            return null;
        if (paradigm < 0 || paradigm >= patterns.Length)
            return null;
        InflectionPattern test = patterns[paradigm];
        if (test.Type == InflectionPatternType.DoesntExist && !strict)
        {
            // if we don't find the pattern directly, we can then fall back to this process:
            // 1. try to find a pattern of the same gender, but different number
            // 2. try to find a pattern of the same number, but different gender
            // we return the first found in both cases
            // also this process assumes all inflection arrays have the same length
            inflection.Deconstruct(out GrammaticalGender g, out GrammaticalNumber n);
            InflectionData tempInflection = inflection;
            InflectionPattern found = null;
            for (int i = 0; i < ExistingGenders.Length; i++)
            {
                GrammaticalGender possibleG = ExistingGenders[i];
                if (possibleG == g)
                    continue;
                tempInflection.Set(possibleG);
                var possiblePatterns = GetPatterns(tempInflection);
                if (possiblePatterns is null)
                    continue;
                found = possiblePatterns[paradigm];
                if (found.Type == InflectionPatternType.DoesntExist)
                    continue;
                return found;
            }
            tempInflection = inflection;
            for (int i = 0; i < ExistingNumbers.Length; i++)
            {
                GrammaticalNumber possibleN = ExistingNumbers[i];
                if (possibleN == n)
                    continue;
                tempInflection.Set(possibleN);
                var possiblePatterns = GetPatterns(tempInflection);
                if (possiblePatterns is null)
                    continue;
                found = possiblePatterns[paradigm];
                if (found.Type == InflectionPatternType.DoesntExist)
                    continue;
                return found;
            }
        }
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
        InflectionData inflection = 0;
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
/// <summary>
/// A structure containing exceptions for a specific <see cref="InflectionPattern"/>.
/// </summary>
/// <param name="Pattern">The pattern.</param>
/// <param name="Data">The inflection that is different from the pattern's inflection.</param>
public readonly record struct InflectionException(InflectionPattern Pattern, InflectionData Data);
/// <summary>
/// Describes a pattern for noun recognition or adjective inflection.
/// </summary>
/// <remarks>
/// Creates a basic inflection pattern.
/// </remarks>
/// <param name="type">The desired type.</param>
/// <param name="match">The desired text value.</param>
/// <param name="not">Whether or not this will be a negative matching pattern.</param>
/// <param name="literalMask">A mask that tells which characters (that are recognized as special) of <paramref name="match"/> should be literal as opposed to actually special.</param>
public sealed class InflectionPattern(InflectionPatternType type, string match, bool not, uint literalMask = 0u)
{
    /// <summary>
    /// The exceptions for this pattern, which have a different <see cref="InflectionData"/> value than the parent.
    /// </summary>
    public InflectionException[] Exceptions;
    /// <summary>
    /// The type of this pattern.
    /// </summary>
    public InflectionPatternType Type = type;
    /// <summary>
    /// If this pattern has special characters, this will be a map to help it make decisions during recognition and generation.
    /// </summary>
    public SpecialPatternCharacter[] SpecialMap = GenerateSpecialMap(match, literalMask);
    /// <summary>
    /// The raw text value of this pattern.
    /// </summary>
    public string Match = match;
    /// <summary>
    /// Whether or not this is a negative matching pattern.
    /// </summary>
    public bool Not = not;
    /// <summary>
    /// Generates a special map for a given raw string inflection pattern.
    /// </summary>
    /// <param name="match">The raw text value of the pattern.</param>
    /// <param name="literalMask">A mask that tells which characters (that are recognized as special) of <paramref name="match"/> should be literal as opposed to actually special.</param>
    /// <returns>The map.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static SpecialPatternCharacter[] GenerateSpecialMap(string match, uint literalMask = 0u)
    {
        if (match is null)
            return null;
        SpecialPatternCharacter[] result = null;
        for (int i = 0; i < match.Length; i++)
        {
            if ((literalMask & (1u << i)) != 0)
                continue;
            char c = match[i];
            if (!char.IsUpper(c))
                continue;
            result ??= new SpecialPatternCharacter[match.Length];
            result[i] = c switch
            {
                'N' => SpecialPatternCharacter.StrictNone,
                'X' => SpecialPatternCharacter.AnyCharacter,
                'G' => SpecialPatternCharacter.Grave,
                'A' => SpecialPatternCharacter.Acute,
                'C' => SpecialPatternCharacter.Circumflex,
                'T' => SpecialPatternCharacter.Tilde,
                'M' => SpecialPatternCharacter.Macron,
                'B' => SpecialPatternCharacter.Breve,
                'D' => SpecialPatternCharacter.Diaeresis,
                'R' => SpecialPatternCharacter.Ring,
                'K' => SpecialPatternCharacter.Caron,
                'Q' => SpecialPatternCharacter.Comma,
                'L' => SpecialPatternCharacter.Cedilla,
                'O' => SpecialPatternCharacter.Ogonek,
                _ => throw new InvalidOperationException($"Character '{c}' was not recognized as corresponding to any diacritic type.")
            };
        }
        return result;
    }
    /// <summary>
    /// Checks if this word is actually considered an exception for this pattern.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <param name="pattern">If the word is an exception, then this will be the correct pattern that matches it best, otherwise null.</param>
    /// <param name="inflection">If the word is an exception, then this will be the inflection that actually matches the <paramref name="word"/>.</param>
    /// <returns>Whether or not this word is an exception for this pattern.</returns>
    public bool CheckException(ReadOnlySpan<char> word, out InflectionPattern pattern, out InflectionData inflection)
    {
        pattern = null;
        inflection = 0;
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
            char d = SpecialMap is null ? '\u0000' : (char)SpecialMap[i];
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
    /// <summary>
    /// Checks if a word matches this pattern.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>Whether or not the word matches the pattern.</returns>
    /// <exception cref="NotSupportedException"></exception>
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
    /// <summary>
    /// Attempts to remove this pattern from the given word.
    /// </summary>
    /// <param name="word">The word to attempt to remove this pattern from.</param>
    /// <param name="result">The word with this pattern removed. Or the same as the input if not removed.</param>
    /// <returns>Whether or not the pattern was successfully removed from the word.</returns>
    /// <exception cref="NotSupportedException"></exception>
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
    /// <summary>
    /// Attempts to replace this pattern in a word with the given replacement.
    /// </summary>
    /// <param name="word">The word to attempt to replace this pattern from.</param>
    /// <param name="replacement">The replacement that will be used to replace this pattern.</param>
    /// <param name="result">The result of the replacement.</param>
    /// <param name="replacementDiacritics">An array of special pattern character values to take into account when replacing.<para/>
    /// Can be generated using <see cref="GenerateSpecialMap(string, uint)"/>.
    /// </param>
    /// <returns>Whether or not the replacement actually took place.</returns>
    /// <exception cref="NotSupportedException"></exception>
    public bool TryReplace(ReadOnlySpan<char> word, ReadOnlySpan<char> replacement, out ReadOnlySpan<char> result, SpecialPatternCharacter[] replacementDiacritics = null)
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
                if (diacritic == SpecialPatternCharacter.None)
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
                if (diacritic == SpecialPatternCharacter.AnyCharacter)
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
    /// <summary>
    /// Replaces one pattern from a word with another, given that both patterns have the same <see cref="Type"/>.
    /// </summary>
    /// <param name="word">The word to replace this pattern from with another.</param>
    /// <param name="replacement">The pattern to use as a replacement. Must have the same <see cref="Type"/> as this pattern.</param>
    /// <param name="result">The result of the replacement.</param>
    /// <returns>Whether or not the replacement actually took place.</returns>
    public bool TryReplace(ReadOnlySpan<char> word, in InflectionPattern replacement, out ReadOnlySpan<char> result)
    {
        result = word;
        if (Type != replacement.Type)
            return false;
        return TryReplace(word, replacement.Match, out result, replacement.SpecialMap);
    }
    /// <summary>
    /// Tries to parse an <see cref="InflectionPattern"/> given its raw string representation.
    /// </summary>
    /// <param name="pattern">The raw string representation of an <see cref="InflectionPattern"/>.</param>
    /// <param name="result">The result of parsing.</param>
    /// <param name="exceptions">A generic list of possible exceptions that is generally passed into multiple <see cref="TryParse(string, out InflectionPattern, List{InflectionException})"/> calls.<para/>
    /// Elements <b>might</b> be removed from the list if the exceptions inside match this pattern.
    /// </param>
    /// <returns></returns>
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
public enum SpecialPatternCharacter
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