using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapsLang
{
    public class Config
    {
        public string Language { get; set; } = AppLanguage.Auto.Code;
        public string IconColor { get; set; } = AppConstants.IconColors.Blue;
        public IReadOnlyList<string> ExcludedFolders { get; private set; }
        public IReadOnlyList<string> ExcludedProcesses { get; private set; }

        private volatile CompiledRules _rules = CompiledRules.Empty;

        public CompiledRules Rules => _rules;

        public string[] FolderRules => _rules.FolderRules;
        public bool HasRules => _rules.HasRules;

        public Config()
        {
            Language = AppLanguage.Auto.Code;
            IconColor = AppConstants.IconColors.Blue;
            ExcludedFolders = new List<string>();
            ExcludedProcesses = new List<string>();
            _rules = CompiledRules.Empty;
        }

        public void UpdateRules(IEnumerable<string> folders, IEnumerable<string> processes)
        {
            ExcludedFolders = folders != null ? new List<string>(folders) : new List<string>();
            ExcludedProcesses = processes != null ? new List<string>(processes) : new List<string>();
            RebuildCompiledRules();
        }

        public void ApplyFrom(Config other)
        {
            if (other == null) return;
            Language = other.Language;
            IconColor = other.IconColor;
            ExcludedFolders = other.ExcludedFolders != null ? new List<string>(other.ExcludedFolders) : new List<string>();
            ExcludedProcesses = other.ExcludedProcesses != null ? new List<string>(other.ExcludedProcesses) : new List<string>();
            RebuildCompiledRules();
        }

        internal static HashSet<string> CompileSet(IEnumerable<string> source, Func<string, string> normalizer)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                foreach (string item in source)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        string clean = normalizer(item);
                        if (!string.IsNullOrEmpty(clean))
                            set.Add(clean);
                    }
                }
            }
            return set;
        }

        public void RebuildCompiledRules()
        {
            var procSet = CompileSet(ExcludedProcesses, NormalizeProcess);
            var folderRules = CompileSet(ExcludedFolders, NormalizeFolder).ToArray();
            _rules = new CompiledRules(procSet, folderRules);
        }

        public static bool HasSettings()
        {
            return SettingsStorage.Current.HasSettings();
        }

        public static Config Load()
        {
            return SettingsStorage.Current.Load();
        }

        public void Save()
        {
            SettingsStorage.Current.Save(this);
            RebuildCompiledRules();
        }

        internal static readonly char[] CleanTrimChars = { ' ', '\t', '\r', '\n', '\"' };

        public static string NormalizeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string clean = path.Trim(CleanTrimChars).Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(clean)) return "";
            return clean + "\\";
        }

        public static string NormalizeProcess(string proc)
        {
            if (string.IsNullOrWhiteSpace(proc)) return "";
            string clean = proc.Trim(CleanTrimChars);
            try
            {
                clean = Path.GetFileName(clean);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                return "";
            }

            if (string.IsNullOrWhiteSpace(clean)) return "";

            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                clean += ".exe";
            }
            return clean.ToLowerInvariant();
        }
    }

    public class CompiledRules
    {
        public static readonly CompiledRules Empty = new CompiledRules(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new string[0]
        );

        internal HashSet<string> ProcessSet { get; }
        public string[] FolderRules { get; }

        public int ProcessCount => ProcessSet.Count;
        public bool ContainsProcess(string procName) => procName != null && ProcessSet.Contains(procName);

        public bool HasRules => (ProcessSet != null && ProcessSet.Count > 0)
                             || (FolderRules != null && FolderRules.Length > 0);

        public CompiledRules(HashSet<string> processSet, string[] folderRules)
        {
            ProcessSet = processSet ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            FolderRules = folderRules ?? new string[0];
        }
    }
}
