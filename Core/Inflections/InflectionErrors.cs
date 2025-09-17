using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.DataStructures;

namespace MoreLocales.Core.Inflections
{
    internal enum LPlusError
    {
        None,
        EntryOutside,
        SubsectionWithoutSection,
        InvalidSectionOrSubsection,
        SectionTagsUnexpected,
        SectionTagsUnexpectedCount,
        MalformedEntry,
        UnexpectedEntry,
        UnexpectedEntryCount,
        BadEntryFormat,
        BadSimpleMatch,
        OverrideOverlap,
    }
    internal sealed class LPlusFileParsingException(LPlusError error, string fileName, Point16 position, string line) : Exception
    {
        public override string Message => MoreLocales.Instance.GetLocalization($"Misc.Error.LPlus{error}").Format(fileName, position, line);
    }
}
