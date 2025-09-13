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
    internal readonly record struct SimpleNameOverride(int ID, string Override);
    internal struct ItemsSection(SimpleNameOverride[] nameOverrides)
    {
        public SimpleNameOverride[] NameOverrides = nameOverrides;
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
            SimpleNameOverride[] nameOverrides = new SimpleNameOverride[raw.Count];
            int i = 0;
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
                    // add code for inflection overrides
                }
            }
            Array.Resize(ref nameOverrides, i);
            section = new(nameOverrides);
            return true;
        }
    }
}
