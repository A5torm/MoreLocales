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
    internal struct PrefixesSection(NameOverride[] nameOverrides)
    {
        public NameOverride[] NameOverrides = nameOverrides;
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
            // now go through each item
            NameOverride[] nameOverrides = new NameOverride[raw.Count];
            int i = 0;
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
                    // add code for inflection overrides
                }
            }
            Array.Resize(ref nameOverrides, i);

            // now try parsing assignments individual forms of this prefix. these will be added to the AlternateForms dictionary of each prefix

            section = new(nameOverrides);
            return true;
        }
    }
}
