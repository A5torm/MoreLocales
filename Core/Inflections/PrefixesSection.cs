using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terraria;

namespace MoreLocales.Core.Inflections
{
    internal struct PrefixesSection
    {
        public SimpleNameOverride[] NameOverrides;
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
        public static bool Parse(string fileName, string name, in Dictionary<string, List<LPlusFileEntry>> raw, out PrefixesSection section)
        {
            section = default;
            return false;
        }
    }
}
