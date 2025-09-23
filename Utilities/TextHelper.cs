using MoreLocales.Core.Inflections;
using System.Text;

namespace MoreLocales.Utilities;
/// <summary>
/// Types of boundary symbols that could be around a word, like quotes, parentheses, curly brackets, etc.
/// </summary>
public enum BoundaryType : byte
{
    /// <summary>
    /// No boundary symbol.
    /// </summary>
    None,
    /// <summary>
    /// <c>'Word'</c>
    /// </summary>
    DumbSingleQuotes,
    /// <summary>
    /// <c>"Word"</c>
    /// </summary>
    DumbDoubleQuotes,
    /// <summary>
    /// <c>‘Word’</c>
    /// </summary>
    SmartSingleQuotes,
    /// <summary>
    /// <c>“Word”</c>
    /// </summary>
    SmartDoubleQuotes,
    /// <summary>
    /// <c>„Word“</c>
    /// </summary>
    CurvedQuotes,
    /// <summary>
    /// <c>‹Word›</c>
    /// </summary>
    SingleGuillemets,
    /// <summary>
    /// <c>«Word»</c>
    /// </summary>
    DoubleGuillemets,
    /// <summary>
    /// <c>(Word)</c>
    /// </summary>
    Parentheses,
    /// <summary>
    /// <c>{Word}</c>
    /// </summary>
    CurlyBrackets,
    /// <summary>
    /// <c>&lt;Word&gt;</c>
    /// </summary>
    AngleBrackets,
    /// <summary>
    /// <c>〈Word〉</c>
    /// </summary>
    WideAngleBrackets,
    /// <summary>
    /// <c>《Word》</c>
    /// </summary>
    WideDoubleAngleBrackets,
    /// <summary>
    /// <c>[Word]</c>
    /// </summary>
    SquareBrackets,
    /// <summary>
    /// <c>「Word」</c>
    /// </summary>
    CornerBrackets,
    /// <summary>
    /// <c>『Word』</c>
    /// </summary>
    WhiteCornerBrackets,
}
/// <summary>
/// Contains some helpers for working with text.
/// </summary>
public static class TextHelper
{
    /// <summary>
    /// Checks if a character has a certain diacritic.
    /// </summary>
    /// <param name="c">The character to check.</param>
    /// <param name="diacritic">The diacritic to check.</param>
    /// <returns>Whether or not the given character has that diacritic.</returns>
    public static bool HasDiacritic(char c, SpecialPatternCharacter diacritic)
    {
        char d = (char)diacritic;
        if (d == '\u0002')
            return true;
        string decompose = c.ToString().Normalize(NormalizationForm.FormD);
        if (d <= '\u0001')
        {
            if (decompose.Length == 1)
                return true;
            return false;
        }
        for (int i = 1; i < decompose.Length; i++)
        {
            if (d == decompose[i])
                return true;
        }
        return false;
    }
    /// <summary>
    /// Tries to add a diacritic to a given character.
    /// </summary>
    /// <param name="c">The character to try to add a diacritic to.</param>
    /// <param name="diacritic">The diacritic to add.</param>
    /// <param name="cWithDiacritic">The character after trying to add a diacritic to it.</param>
    /// <returns>Whether or not the diacritic was successfully added to the character.</returns>
    public static bool TryAddDiacritic(char c, SpecialPatternCharacter diacritic, out char cWithDiacritic)
    {
        cWithDiacritic = c;
        if (diacritic is SpecialPatternCharacter.None or SpecialPatternCharacter.AnyCharacter)
            return true;
        else if (diacritic == SpecialPatternCharacter.StrictNone)
        {
            cWithDiacritic = c.ToString().Normalize(NormalizationForm.FormD)[0];
            return true;
        }
        else
        {
            string composed = (c.ToString() + (char)diacritic).Normalize(NormalizationForm.FormC);
            if (composed.Length == 1)
            {
                cWithDiacritic = composed[0];
                return true;
            }
            return false;
        }
    }
    /// <summary>
    /// Attempts to recognize the boundary type used by the given string.
    /// </summary>
    /// <param name="text">The string that may or may not have a boundary around it.</param>
    /// <returns>The boundary type, if detected. Otherwise <see cref="BoundaryType.None"/>.</returns>
    public static BoundaryType GetBoundaryType(string text)
    {
        return text[0] switch
        {
            '\'' => BoundaryType.DumbSingleQuotes,
            '"' => BoundaryType.DumbDoubleQuotes,
            '‘' => BoundaryType.SmartSingleQuotes,
            '“' => BoundaryType.SmartDoubleQuotes,
            '„' => BoundaryType.CurvedQuotes,
            '‹' => BoundaryType.SingleGuillemets,
            '«' => BoundaryType.DoubleGuillemets,
            '(' => BoundaryType.Parentheses,
            '{' => BoundaryType.CurlyBrackets,
            '<' => BoundaryType.AngleBrackets,
            '〈' => BoundaryType.WideAngleBrackets,
            '《' => BoundaryType.WideDoubleAngleBrackets,
            '「' => BoundaryType.CornerBrackets,
            '『' => BoundaryType.WhiteCornerBrackets,
            '[' => BoundaryType.SquareBrackets,
            _ => BoundaryType.None,
        };
    }
    /// <summary>
    /// Formats a string using the provided boundary type.
    /// </summary>
    /// <param name="text">The string to format.</param>
    /// <param name="boundary">The boundary to format it with.</param>
    /// <returns>The string after being formatted using the provided boundary type.</returns>
    public static string FormatWithBoundary(string text, BoundaryType boundary)
    {
        return boundary switch
        {
            BoundaryType.None => text,
            BoundaryType.DumbSingleQuotes => $"'{text}'",
            BoundaryType.DumbDoubleQuotes => $"\"{text}\"",
            BoundaryType.SmartSingleQuotes => $"‘{text}’",
            BoundaryType.SmartDoubleQuotes => $"“{text}”",
            BoundaryType.CurvedQuotes => $"„{text}“",
            BoundaryType.SingleGuillemets => $"‹{text}›",
            BoundaryType.DoubleGuillemets => $"«{text}»",
            BoundaryType.Parentheses => $"({text})",
            BoundaryType.CurlyBrackets => $"{{{text}}}",
            BoundaryType.AngleBrackets => $"<{text}>",
            BoundaryType.WideAngleBrackets => $"〈{text}〉",
            BoundaryType.WideDoubleAngleBrackets => $"《{text}》",
            BoundaryType.SquareBrackets => $"[{text}]",
            BoundaryType.CornerBrackets => $"「{text}」",
            BoundaryType.WhiteCornerBrackets => $"『{text}』",
            _ => text,
        };
    }
}
