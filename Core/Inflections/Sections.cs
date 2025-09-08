using System.Collections.Generic;

namespace MoreLocales.Core.Inflections
{
    internal struct InflectionsSection
    {
        public GrammaticalGender[] Genders;
        public GrammaticalNumber[] Numbers;
        public readonly GrammaticalGender? DefaultGender => Genders?[0];
        public readonly GrammaticalNumber? DefaultNumber => Numbers?[0];
        public static bool Parse(string fileName, string name, in Dictionary<string, List<LPlusFileEntry>> raw, out InflectionsSection section)
        {
            section = default;

            if (raw is null || raw.Count != 1 || !SectionsHelper.Is<InflectionsSection>(in fileName, in name, out _, true))
                return false;

            var entries = raw["INFLECTIONS_META"];

            bool? numberFirst = null;
            if (entries.Count != 1)
                ThrowError(LPlusError.UnexpectedEntryCount);

            var entry = entries[0];
            var key = entry.Key;
            var value = entry.Value;
            int upperCount = 0;

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!char.IsLetter(c))
                    ThrowError();
                if (char.IsUpper(c))
                {
                    bool throwError = ++upperCount > 2;
                    if (!numberFirst.HasValue)
                    {
                        if (c == 'N')
                            numberFirst = true;
                        else if (c == 'G')
                            numberFirst = false;
                        else
                            throwError = true;
                    }
                    if (throwError)
                        ThrowError();
                }
            }
            if (upperCount < 2)
                ThrowError();
            List<GrammaticalGender> genders = [];
            List<GrammaticalNumber> numbers = [];
            bool gendersFirst = !numberFirst.Value;
            bool secondPart = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsNumber(c))
                    ThrowError();

                if (char.IsWhiteSpace(c))
                    continue;

                if (!char.IsLetter(c))
                {
                    if (!secondPart)
                        secondPart = true;
                    else
                        ThrowError();
                }
                else if (char.IsUpper(c))
                {
                    bool addToNumbers = gendersFirst == secondPart;
                    char upper = char.ToUpperInvariant(c);
                    if (addToNumbers)
                    {
                        numbers.Add(LangFeaturesPlus.CharToNumber(upper, true));
                    }
                    else
                    {
                        genders.Add(LangFeaturesPlus.CharToGender(upper, true));
                    }
                }
            }

            section = new InflectionsSection()
            {
                Genders = [.. genders],
                Numbers = [.. numbers],
            };

            return true;

            void ThrowError(LPlusError error = LPlusError.BadEntryFormat)
            {
                throw new LPlusFileParsingException(error, fileName, default, name);
            }
        }
    }
}
