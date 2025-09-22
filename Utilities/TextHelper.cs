using MoreLocales.Core.Inflections;
using System.Text;

namespace MoreLocales.Utilities;
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
}
