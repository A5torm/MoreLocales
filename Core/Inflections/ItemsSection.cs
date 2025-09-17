using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.ID;

namespace MoreLocales.Core.Inflections
{
    internal readonly record struct ItemInflectionOverride(int ID, GrammaticalGender? GenderOverride, GrammaticalNumber? NumberOverride);
    internal readonly record struct NameOverride(int ID, string Override);
    internal struct ItemsSection(NameOverride[] nameOverrides, ItemInflectionOverride[] inflectionOverrides)
    {
        public NameOverride[] NameOverrides = nameOverrides;
        public ItemInflectionOverride[] InflectionOverrides = inflectionOverrides;
        public readonly void SetupItemNameOverrides()
        {
            if (NameOverrides is null)
                return;
            for (int i = 0; i < NameOverrides.Length; i++)
            {
                var nameOverride = NameOverrides[i];
                Lang._itemNameCache[nameOverride.ID].SetValue(nameOverride.Override);
            }
        }
        public readonly void SetupItemInflectionOverrides()
        {
            if (InflectionOverrides is null)
                return;
            for (int i = 0; i < InflectionOverrides.Length; i++)
            {
                var inflectionOverride = InflectionOverrides[i];
                ref RecognizableWordData data = ref MoreLocalesSets.CachedInflectionData[inflectionOverride.ID];
                InflectionData baseData = data.Data.Inflection;
                baseData.Set(inflectionOverride.GenderOverride, inflectionOverride.NumberOverride);
                data.Data = new(baseData);
            }
        }
        public static bool Parse(string fileName, string name, in Dictionary<string, List<LPlusFileEntry>> raw, out ItemsSection section)
        {
            section = default;
            if (raw is null || raw.Count == 0 || !SectionsHelper.Is<ItemsSection>(in fileName, in name, out var tags))
                return false;
            if (raw.ContainsKey("ITEMS_META"))
                throw new LPlusFileParsingException(LPlusError.UnexpectedEntry, fileName, default, name);
            // tag is used to specify mod name for ease of use, though using ModName/ItemInternalName also works.
            string modName = null;
            if (tags != null)
            {
                if (tags.Length != 1)
                    throw new LPlusFileParsingException(LPlusError.SectionTagsUnexpectedCount, fileName, default, name);
                modName = tags[0];
            }
            // now go through each item
            NameOverride[] nameOverrides = new NameOverride[raw.Count];
            int i = 0;
            ItemInflectionOverride[] inflectionOverrides = new ItemInflectionOverride[raw.Count];
            int j = 0;
            foreach (var subsectionWithFields in raw)
            {
                SectionsHelper.Tags(in fileName, subsectionWithFields.Key, out var itemName, out var itemTags);
                string finalItemName = modName is null ? itemName : $"{modName}/{itemName}";
                if (ItemID.Search.TryGetId(finalItemName, out int itemID))
                {
                    if (itemTags != null)
                    {
                        if (itemTags.Length != 1)
                            throw new LPlusFileParsingException(LPlusError.SectionTagsUnexpectedCount, fileName, default, itemName);
                        nameOverrides[i++] = new(itemID, itemTags[0]);
                    }
                    if (subsectionWithFields.Value is null)
                        continue;
                    GrammaticalGender? possibleGenderOverride = null;
                    GrammaticalNumber? possibleNumberOverride = null;
                    foreach (var inflectionOverride in CollectionsMarshal.AsSpan(subsectionWithFields.Value))
                    {
                        switch (inflectionOverride.Key[0])
                        {
                            case 'G':
                                if (possibleGenderOverride.HasValue)
                                    throw new LPlusFileParsingException(LPlusError.OverrideOverlap, fileName, default, finalItemName);
                                if (!LangFeaturesPlus.TryParseGenderName(inflectionOverride.Value, out GrammaticalGender parsedGender))
                                    throw new Exception(MoreLocales.InvalidGrammaticalGenderError.Format(inflectionOverride.Value));
                                possibleGenderOverride = parsedGender;
                                break;
                            case 'N':
                                if (possibleNumberOverride.HasValue)
                                    throw new LPlusFileParsingException(LPlusError.OverrideOverlap, fileName, default, finalItemName);
                                if (!LangFeaturesPlus.TryParseNumberName(inflectionOverride.Value, out GrammaticalNumber parsedNumber))
                                    throw new Exception(MoreLocales.InvalidGrammaticalNumberError.Format(inflectionOverride.Value));
                                possibleNumberOverride = parsedNumber;
                                break;
                            case 'I':
                                if (possibleNumberOverride.HasValue || possibleGenderOverride.HasValue)
                                    throw new LPlusFileParsingException(LPlusError.OverrideOverlap, fileName, default, finalItemName);
                                if (!LangFeaturesPlus.TryParseInflectionName(inflectionOverride.Value, out possibleGenderOverride, out possibleNumberOverride))
                                    throw new Exception(MoreLocales.Instance.GetLocalization("Misc.Error.InvalidInflection").Format(inflectionOverride.Value));
                                break;
                            default:
                                throw new LPlusFileParsingException(LPlusError.MalformedEntry, fileName, default, inflectionOverride.ToString());
                        }
                    }
                    if (possibleGenderOverride.HasValue || possibleNumberOverride.HasValue)
                        inflectionOverrides[j++] = new(itemID, possibleGenderOverride, possibleNumberOverride);
                }
            }

            Array.Resize(ref nameOverrides, i);
            Array.Resize(ref inflectionOverrides, j);

            section = new(nameOverrides, inflectionOverrides);
            return true;
        }
    }
}
