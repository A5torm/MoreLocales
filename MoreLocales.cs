/*
 * Copyright (C) 2025 qAngel
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <https://www.gnu.org/licenses/>.
 */

global using Microsoft.Xna.Framework;
global using Microsoft.Xna.Framework.Graphics;
global using Mono.Cecil.Cil;
global using MonoMod.Cil;
global using MoreLocales.Core;
global using MoreLocales.Utilities;
global using ReLogic.Graphics;
global using Terraria.ModLoader;
using MoreLocales.Common;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Terraria.Localization;

namespace MoreLocales
{
    /// <summary>
    /// A super cool localization extension mod. <para/>
    /// <see href="https://github.com/queueAngel/MoreLocales"/>
    /// </summary>
	public class MoreLocales : Mod
	{
        internal static LocalizedText InvalidGrammaticalGenderError;
        internal static LocalizedText InvalidGrammaticalNumberError;
        internal static LocalizedText InvalidInflectionError;
        internal static Dictionary<Mod, string> inflectionFileKeys = [];
        /// <summary>
        /// The instance of MoreLocales.
        /// </summary>
        public static MoreLocales Instance { get; private set; }
        static MoreLocales()
        {
            LocalizationTweaks.Apply();
        }
        /// <inheritdoc/>
        public MoreLocales()
        {
            Instance = this;
            MoreLocalesAPI.ProtectFilesFromLegacyMarking(Instance);
            MoreLocalesAPI.InitCustomCultureModsArray();
            MoreLocalesAPI._canRegister = true;
            MoreLocalesAPI.DoLoad();
        }
        /// <inheritdoc/>
        public override void Load()
        {
            AssetHelper.Setup(Instance);
            FontHelperV2.DoLoad();
            LangFeaturesPlus.DoLoad();
            MoreLocalesAPI.DoSafeLoad();
        }
        /// <inheritdoc/>
        public override void PostSetupContent()
        {
            InvalidGrammaticalGenderError = GetLocalization("Misc.Error.InvalidGrammaticalGender");
            InvalidGrammaticalNumberError = GetLocalization("Misc.Error.InvalidGrammaticalNumber");
            InvalidInflectionError = GetLocalization("Misc.Error.InvalidInflection");

            LangUtils.InitCategories();

            BetterLangMenuV2.InitAssetsSafe();

            MoreLocalesSets._didFirstLoad = true;
            MoreLocalesSets.ReloadedLocalizations();

            MoreLocalesAPI.cachedVanillaCulture = LanguageManager.Instance.ActiveCulture.LegacyId;
            MoreLocalesAPI.LoadCustomCultureData();

            if (FontHelperV2.CharDataInlined && OperatingSystem.IsWindows())
                MessageBox.Show(GetLocalization("Misc.Error.FontPatchingError").Value, Language.GetTextValue("Error.Error"));
        }
        /// <summary>
        /// Call this for your mod during <see cref="Mod.Load"/> or earlier if you don't want localization files for inflection data (grammatical gender, pluralization) to be generated at all.
        /// </summary>
        /// <param name="mod">The mod to protect.</param>
        public static void ProtectModFromInflectionFileGeneration(Mod mod)
        {
            inflectionFileKeys.Add(mod, null);
            Instance.Logger.Info($"Mod {mod} was successfully added to the list of mods protected from inflection localization file generation.");
        }
        /// <inheritdoc/>
        public override object Call(params object[] args)
        {
            if (args.Length == 1)
            {
                object possibleMod = args[0];
                if (possibleMod is Mod mod0)
                {
                    ProtectModFromInflectionFileGeneration(mod0);
                    return null;
                }
                else if (possibleMod is string modName && ModLoader.TryGetMod(modName, out Mod mod1))
                {
                    ProtectModFromInflectionFileGeneration(mod1);
                    return null;
                }
            }

            throw new InvalidOperationException
                ("""
                MoreLocales does not have a Mod.Call API. Using a weakReference is your only option if you do not wish for this mod to be a dependency in your mod.
                This is in order to avoid extreme verbosity. Please consult the wiki for further information: https://github.com/queueAngel/MoreLocales/wiki/Home
                """);
        }
        /// <inheritdoc/>
        public override void Unload()
        {
            MoreLocalesAPI.DoUnload();
            LocalizationTweaks.Unapply();
        }
    }
}
