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
    internal enum LPlusError
    {
        None,
        EntryOutside,
        SubsectionWithoutSection,
        InvalidSectionOrSubsection,
        SectionTagsUnexpected,
        MalformedEntry,
        UnexpectedEntry,
        UnexpectedEntryCount,
        BadEntryFormat,
        BadSimpleMatch,
    }
    internal partial class LPlusFile(InflectionsSection inflections, RecognizeSection recognize)
    {
        internal static Regex Split;
        public static LPlusFile Current { get; internal set; }
        internal static Dictionary<Mod, Dictionary<string, List<TmodFile.FileEntry>>> _lplusFiles;
        public InflectionsSection Inflections = inflections;
        public RecognizeSection Recognize = recognize;
        public Mod Source { get; private set; }
        internal static void UpdateCurrent()
        {
            Current = null;
            ref MoreLocalesCulture c = ref MoreLocalesAPI.ActiveCulture;
            if (!_lplusFiles.TryGetValue(MoreLocales.Instance, out var dict))
                return;
            if (!dict.TryGetValue(c.Culture.Name, out var files))
                return;
            TmodFile.FileEntry file = files[0];
            using Stream stream = MoreLocales.Instance.File.GetStream(file);
            using StreamReader reader = new(stream, Encoding.UTF8, true);
            string content = reader.ReadToEnd();
            Current = Parse(content, file.Name);
            Current.Source = MoreLocales.Instance;
            // todo: add support for merging
            /*
            foreach (var kvp in _lplusFiles)
            {
                if (!kvp.Value.TryGetValue(c.Culture.Name, out var cultureFiles))
                    continue;
                foreach (var file in CollectionsMarshal.AsSpan(cultureFiles))
                {

                }
            }
            */
        }
        internal static void Initialize()
        {
            Split = SplitRegex();

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
            foreach (var t in CollectionsMarshal.AsSpan(simpleList))
            {
                if (!_lplusFiles.TryGetValue(t.mod, out var dict))
                {
                    dict = [];
                    _lplusFiles.Add(t.mod, dict);
                }
                if (!dict.TryGetValue(t.langCode, out var list))
                {
                    list = [];
                    dict.Add(t.langCode, list);
                }
                list.Add(t.file);
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
        internal static LPlusFile DeepParse(string fileName, Dictionary<string, Dictionary<string, List<LPlusFileEntry>>> shallowData)
        {
            InflectionsSection inflectionSection = default;
            RecognizeSection recognizeSection = default;
            foreach (var kvp in shallowData)
            {
                string sectionName = kvp.Key;
                var subsections = kvp.Value;

                if (InflectionsSection.Parse(fileName, sectionName, in subsections, out var inflSection))
                {
                    inflectionSection = inflSection;
                }
                else if (RecognizeSection.Parse(fileName, sectionName, in subsections, out var recogSection))
                {
                    recognizeSection = recogSection;
                }
            }
            return new LPlusFile(inflectionSection, recognizeSection);
        }
        internal static LPlusFile Parse(string content, string fileName)
        {
            var notNewline = SplitRegex();
            return DeepParse(fileName, ShallowParse(Split.Split(content), fileName));
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
    internal sealed class LPlusFileParsingException(LPlusError error, string fileName, Point16 position, string line) : Exception
    {
        public override string Message => MoreLocales.Instance.GetLocalization($"Misc.Error.LPlus{error}").Format(fileName, position, line);
    }
}
