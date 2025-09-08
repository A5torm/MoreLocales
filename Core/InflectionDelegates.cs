#pragma warning disable IDE1006, IDE0060

namespace MoreLocales.Core
{
    /// <summary>
    /// Contains some helper delegates for inflection when building a custom culture.
    /// </summary>
    public static class InflectionDelegates
    {
        /// <summary>
        /// This culture doesn't do adjective inflection for grammatical gender nor pluralization. (Like en-US)
        /// </summary>
        public static bool inflectionNeverChanges(GrammaticalGender gender, GrammaticalNumber pluralization) => false;
        /// <summary>
        /// This culture does adjective inflection if either the grammatical gender or pluralization aren't default. (Like es-ES)
        /// </summary>
        public static bool inflectionChangesWhenNotDefault(GrammaticalGender gender, GrammaticalNumber pluralization) => gender > 0 || pluralization > 0;
    }
}

#pragma warning restore
