using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Terraria.DataStructures;
using Terraria.ModLoader.Core;

namespace MoreLocales.Core.Inflections
{
    internal partial class LPlusFile(InflectionsSection inflections = default, ItemsSection items = default, PrefixesSection prefixes = default)
    {
        internal static Regex Split = SplitRegex();
        public static LPlusFile Current { get; internal set; }
        internal static Dictionary<Mod, Dictionary<string, List<TmodFile.FileEntry>>> _lplusFiles;
        public InflectionsSection Inflections = inflections;
        public ItemsSection Items = items;
        public PrefixesSection Prefixes = prefixes;
        public Mod Source { get; private set; }
        internal static void UpdateCurrent()
        {
            Current = null;
            ref MoreLocalesCulture c = ref MoreLocalesAPI.ActiveCulture;
            Mod source = c.FunctionalOwner;
            string langCode = c.Culture.Name;
            string fallbackCode = c.FallbackCulture == 1 ? null : MoreLocalesAPI.extraCulturesV2[c.FallbackCulture].Culture.Name;

            if (fallbackCode != null)
                LoadLPlusFiles(fallbackCode, source);
            LoadLPlusFiles(langCode, source);
        }
        internal static void LoadLPlusFiles(string langCode, Mod source)
        {
            foreach (var kvp in _lplusFiles)
            {
                if (!kvp.Value.TryGetValue(langCode, out var cultureFiles))
                    continue;
                Current ??= new()
                {
                    Source = source
                };
                foreach (var file in CollectionsMarshal.AsSpan(cultureFiles))
                {
                    using Stream stream = source.File.GetStream(file);
                    using StreamReader reader = new(stream, Encoding.UTF8, true);
                    string content = reader.ReadToEnd();
                    Current.Add(content, file.Name);
                }
            }
        }
        internal void Add(string content, string fileName)
        {
            var shallowData = ShallowParse(Split.Split(content), fileName);
            DeepParse(fileName, shallowData, out var inflectionsSection, out var itemsSection, out var prefixesSection);
            Inflections.Merge(in inflectionsSection);
            Items.Merge(in itemsSection);
            Prefixes.Merge(in prefixesSection);
        }
        internal void SetupNameOverrides()
        {
            Items.SetupItemNameOverrides();
            Prefixes.SetupPrefixNameOverrides();
        }
        internal void SetupInflectionAndFormOverrides()
        {
            Items.SetupItemInflectionOverrides();
            Prefixes.SetupPrefixFormOverrides();
        }
        internal static void Initialize()
        {
            List<(Mod mod, string langCode, TmodFile.FileEntry file)> simpleList = [];
            Mod[] mods = ModLoader.Mods;
            int modsCount = 0;
            for (int i = 0; i < mods.Length; i++)
            {
                Mod mod = mods[i];

                var list = GetInflectionFiles(mod);
                if (list is null)
                    continue;
                simpleList.AddRange(list);
                modsCount++;
            }

            _lplusFiles = new(modsCount);
            foreach (var (mod, langCode, file) in CollectionsMarshal.AsSpan(simpleList))
            {
                if (!_lplusFiles.TryGetValue(mod, out var dict))
                {
                    dict = [];
                    _lplusFiles.Add(mod, dict);
                }
                if (!dict.TryGetValue(langCode, out var list))
                {
                    list = [];
                    dict.Add(langCode, list);
                }
                list.Add(file);
            }
        }
        internal static List<(Mod mod, string langCode, TmodFile.FileEntry file)> GetInflectionFiles(Mod mod)
        {
            TmodFile file = mod.File;

            if (file is null || !file.IsOpen)
                return null;

            // i don't consider it necessary to go for the same amount of performance as GetLocalizationFiles since they're a lot less, generally speaking
            var list = new List<(Mod mod, string langCode, TmodFile.FileEntry file)>(file.Count);

            for (int i = 0; i < file.Count; i++)
            {
                TmodFile.FileEntry entry = file.fileTable[i];
                if (!entry.Name.AsSpan().EndsWith(".lplus"))
                    continue;
                list.Add((mod, Path.GetFileNameWithoutExtension(entry.Name), entry));
            }
            return list;
        }
        internal static Dictionary<string, Dictionary<string, List<LPlusFileEntry>>> ShallowParse(string[] lines, string fileName)
        {
            Dictionary<string, Dictionary<string, List<LPlusFileEntry>>> data = [];
            string currentSection = null;
            string currentSubsection = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                line = line.Trim();

                if (line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                switch (line[0])
                {
                    case '#':
                        continue;
                    case '[':
                        if (line.Length < 3 || line[^1] != ']')
                            ThrowError(LPlusError.InvalidSectionOrSubsection, 0);
                        currentSubsection = null;
                        currentSection = line[1..^1];
                        if (!data.ContainsKey(currentSection))
                            data.Add(currentSection, []);
                        break;
                    case '<':
                        if (line.Length < 3 || line[^1] != '>')
                            ThrowError(LPlusError.InvalidSectionOrSubsection, 0);
                        if (currentSection is null)
                            ThrowError(LPlusError.SubsectionWithoutSection, 0);
                        currentSubsection = line[1..^1];
                        if (!data[currentSection].ContainsKey(currentSubsection))
                            data[currentSection].Add(currentSubsection, []);
                        break;
                    default:
                        if (currentSection is null)
                            ThrowError(LPlusError.EntryOutside, 0);
                        if (currentSubsection is null)
                        {
                            string sectionMetaName = $"{currentSection.ToUpperInvariant()}_META";
                            if (!data[currentSection].ContainsKey(sectionMetaName))
                                data[currentSection].Add(sectionMetaName, []);
                            data[currentSection][sectionMetaName].Add(LPlusFileEntry.Make(line));
                            break;
                        }
                        data[currentSection][currentSubsection].Add(LPlusFileEntry.Make(line));
                        break;
                }

                void ThrowError(LPlusError error, int column)
                {
                    throw new LPlusFileParsingException(error, fileName, new Point16(column, i), line);
                }
            }

            return data;
        }
        internal static void DeepParse(string fileName, Dictionary<string, Dictionary<string, List<LPlusFileEntry>>> shallowData,
            out InflectionsSection inflectionsSection, out ItemsSection itemsSection, out PrefixesSection prefixesSection)
        {
            inflectionsSection = default;
            itemsSection = default;
            prefixesSection = default;

            foreach (var kvp in shallowData)
            {
                string sectionName = kvp.Key;
                var subsections = kvp.Value;

                if (InflectionsSection.Parse(fileName, sectionName, in subsections, out var inflecSection))
                {
                    inflectionsSection = inflecSection;
                }
                else if (ItemsSection.Parse(fileName, sectionName, in subsections, out var iSection))
                {
                    itemsSection = iSection;
                }
                else if (PrefixesSection.Parse(fileName, sectionName, in subsections, out var pSection))
                {
                    prefixesSection = pSection;
                }
            }
        }
        [GeneratedRegex(@"\r?\n")]
        private static partial Regex SplitRegex();
    }
    internal static class SectionsHelper
    {
        public static bool Is<TSection>(in string fileName, in string possibleMatch, out string[] tags, bool throwIfHasTags = false) where TSection : struct
        {
            string sectionName = typeof(TSection).Name;
            if (sectionName.AsSpan().EndsWith("Section", StringComparison.Ordinal))
                sectionName = sectionName[..^7];
            Tags(in fileName, in possibleMatch, out string actualName, out tags, throwIfHasTags);
            bool isSame = actualName.ToUpperInvariant().Equals(sectionName.ToUpperInvariant());
            return isSame;
        }
        public static void Tags(in string fileName, in string name, out string actualName, out string[] tags, bool throwIfAny = false)
        {
            tags = null;
            var nameAndTags = name.Split(':', 2);
            actualName = nameAndTags[0];
            if (nameAndTags.Length == 1)
                return;
            if (throwIfAny)
                throw new LPlusFileParsingException(LPlusError.SectionTagsUnexpected, fileName, default, name);
            tags = nameAndTags[1].Split(',');
        }
    }
    internal readonly record struct LPlusFileEntry(string Key, string Value)
    {
        public static LPlusFileEntry Make(string trimmedLine)
        {
            var parts = trimmedLine.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                throw new LPlusFileParsingException(LPlusError.MalformedEntry, null, default, trimmedLine);
            var valueParts = parts[1].Split('#');
            valueParts = valueParts[0].Split("//");
            return new(parts[0], valueParts[0]);
        }
        public string[] GetValues()
        {
            return Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
