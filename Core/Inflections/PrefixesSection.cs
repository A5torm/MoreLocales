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
    internal readonly record struct PrefixFormOverride(int ID, InflectionData Inflection, string FormOverride);
    internal struct PrefixesSection(NameOverride[] nameOverrides, PrefixFormOverride[] formOverrides)
    {
        public NameOverride[] NameOverrides = nameOverrides;
        public PrefixFormOverride[] FormOverrides = formOverrides;
        public readonly void SetupPrefixNameOverrides()
        {
            if (NameOverrides is null)
                return;
            for (int i = 0; i < NameOverrides.Length; i++)
            {
                var nameOverride = NameOverrides[i];
                Lang.prefix[nameOverride.ID].SetValue(nameOverride.Override);
            }
        }
        public readonly void SetupPrefixFormOverrides()
        {
            if (FormOverrides is null)
                return;
            for (int i = 0; i < FormOverrides.Length; i++)
            {
                var formOverride = FormOverrides[i];
                ref InflectableWord prefix = ref MoreLocalesSets.Prefixes[formOverride.ID];
                if (prefix.AlternateForms is null) // this is null in certain cases. not any cases that actually matter though
                    continue;
                prefix.AlternateForms.Add(formOverride.Inflection, formOverride.FormOverride);
            }
        }
        public static bool Parse(string fileName, string name, in Dictionary<string, List<LPlusFileEntry>> raw, out PrefixesSection section)
        {
            section = default;
            if (raw is null || raw.Count == 0 || !SectionsHelper.Is<PrefixesSection>(in fileName, in name, out var tags))
                return false;
            if (raw.ContainsKey("PREFIXES_META"))
                throw new LPlusFileParsingException(LPlusError.UnexpectedEntry, fileName, default, name);
            // tag is used to specify mod name for ease of use, though using ModName/ItemInternalName also works.
            string modName = null;
            if (tags != null)
            {
                if (tags.Length != 1)
                    throw new LPlusFileParsingException(LPlusError.SectionTagsUnexpectedCount, fileName, default, name);
                modName = tags[0];
            }
            // now go through each prefix
            NameOverride[] nameOverrides = new NameOverride[raw.Count];
            int i = 0;
            List<PrefixFormOverride> formOverrides = new(raw.Count);
            foreach (var subsectionWithFields in raw)
            {
                SectionsHelper.Tags(in fileName, subsectionWithFields.Key, out var prefixName, out var prefixTags);
                string finalPrefixName = modName is null ? prefixName : $"{modName}/{prefixName}";
                if (PrefixID.Search.TryGetId(finalPrefixName, out int prefixID))
                {
                    if (prefixTags != null)
                    {
                        if (prefixTags.Length != 1)
                            throw new LPlusFileParsingException(LPlusError.SectionTagsUnexpectedCount, fileName, default, prefixName);
                        nameOverrides[i++] = new(prefixID, prefixTags[0]);
                    }
                    // now try parsing assignments individual forms of this prefix. these will be added to the AlternateForms dictionary of each prefix
                    if (subsectionWithFields.Value is null)
                        continue;
                    foreach (var formOverride in CollectionsMarshal.AsSpan(subsectionWithFields.Value))
                    {
                        if (!LangFeaturesPlus.TryParseInflectionName(formOverride.Key, out GrammaticalGender? parsedGender, out GrammaticalNumber? parsedNumber)
                            || !parsedGender.HasValue || !parsedNumber.HasValue)
                            throw new Exception(MoreLocales.InvalidInflectionError.Format(formOverride.Key));
                        InflectionData inflection = (InflectionData)((byte)parsedGender.Value | ((byte)parsedNumber.Value << 4));
                        formOverrides.Add(new(prefixID, inflection, formOverride.Value));
                    }
                }
            }

            if (i != 0)
                Array.Resize(ref nameOverrides, i);
            else
                nameOverrides = null;

            section = new(nameOverrides, formOverrides.Count == 0 ? null : formOverrides.ToArray());
            return true;
        }
    }
}
