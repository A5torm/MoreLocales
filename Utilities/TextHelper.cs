using MoreLocales.Core.Inflections;
using System.Text;

namespace MoreLocales.Utilities;

public static class TextHelper
{
    public static bool HasDiacritic(char c, RecognizableDiacriticType diacritic)
    {
        char d = (char)diacritic;
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
    public static bool TryAddDiacritic(char c, RecognizableDiacriticType diacritic, out char cWithDiacritic)
    {
        cWithDiacritic = c;
        if (diacritic == RecognizableDiacriticType.None)
            return true;
        else if (diacritic == RecognizableDiacriticType.StrictNone)
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
