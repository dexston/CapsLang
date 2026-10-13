using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace CapsLang
{
    public class RegistrySettingsStorage : ISettingsStorage
    {
        private const string RegKeyPath = @"Software\CapsLang";
        private const string FoldersValueName = "ExcludedFolders";
        private const string ProcessesValueName = "ExcludedProcesses";
        private const string LanguageValueName = "Language";
        private const string IconColorValueName = "IconColor";

        public bool HasSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    return key != null && key.ValueCount > 0;
                }
            }
            catch (Exception ex) when (ex is SecurityException ||
                                      ex is UnauthorizedAccessException ||
                                      ex is IOException)
            {
                return false;
            }
        }

        public Config Load()
        {
            Config cfg = new Config();
            var folders = new List<string>();
            var processes = new List<string>();

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    if (key != null)
                    {
                        LoadList(key, FoldersValueName, folders, Config.NormalizeFolder);
                        LoadList(key, ProcessesValueName, processes, Config.NormalizeProcess);

                        object langVal = key.GetValue(LanguageValueName);
                        if (langVal is string langStr && !string.IsNullOrWhiteSpace(langStr))
                        {
                            cfg.Language = AppLanguage.FromCode(langStr).Code;
                        }

                        object iconVal = key.GetValue(IconColorValueName);
                        if (iconVal is string iconStr && !string.IsNullOrWhiteSpace(iconStr))
                        {
                            string normIcon = iconStr.Trim().ToLowerInvariant();
                            if (AppConstants.IconColors.All.Contains(normIcon))
                            {
                                cfg.IconColor = normIcon;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException ||
                                      ex is UnauthorizedAccessException ||
                                      ex is IOException)
            {
            }

            cfg.UpdateRules(folders, processes);
            return cfg;
        }

        public void Save(Config config)
        {
            if (config == null) return;

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
            {
                if (key == null)
                {
                    throw new UnauthorizedAccessException(RegKeyPath);
                }

                SaveList(key, FoldersValueName, config.ExcludedFolders, Config.NormalizeFolder);
                SaveList(key, ProcessesValueName, config.ExcludedProcesses, Config.NormalizeProcess);

                string normLang = AppLanguage.FromCode(config.Language).Code;
                key.SetValue(LanguageValueName, normLang, RegistryValueKind.String);

                string normIcon = config.IconColor?.Trim().ToLowerInvariant();
                if (!AppConstants.IconColors.All.Contains(normIcon))
                {
                    normIcon = AppConstants.IconColors.Blue;
                }
                key.SetValue(IconColorValueName, normIcon, RegistryValueKind.String);
            }
        }

        private static void LoadList(RegistryKey key, string valueName, List<string> targetList, Func<string, string> normalizer)
        {
            object val = key.GetValue(valueName);
            if (val is string[] arr)
            {
                var sorted = Config.CompileSet(arr, normalizer)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                targetList.AddRange(sorted);
            }
        }

        private static void SaveList(RegistryKey key, string valueName, IEnumerable<string> items, Func<string, string> normalizer)
        {
            if (items == null)
            {
                key.SetValue(valueName, new string[0], RegistryValueKind.MultiString);
                return;
            }

            var clean = Config.CompileSet(items, normalizer)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            key.SetValue(valueName, clean, RegistryValueKind.MultiString);
        }
    }
}
