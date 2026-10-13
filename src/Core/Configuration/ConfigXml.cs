using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CapsLang
{
    public static class ConfigXml
    {
        public const string RootElement = "CapsLang";
        public const string ElementSettings = "Settings";
        public const string ElementProcesses = "ExcludedProcesses";
        public const string ElementFolders = "ExcludedFolders";
        public const string ElementItem = "Item";

        public const string AttrLanguage = "Language";
        public const string AttrIconColor = "IconColor";

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        public static string Export(Config config)
        {
            if (config == null) config = new Config();

            string normLang = AppLanguage.FromCode(config.Language).Code;
            string normIcon = config.IconColor?.Trim().ToLowerInvariant();
            if (!AppConstants.IconColors.All.Contains(normIcon))
            {
                normIcon = AppConstants.IconColors.Blue;
            }

            var excludedProcesses = Config.CompileSet(config.ExcludedProcesses, Config.NormalizeProcess)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new XElement(ElementItem, p));

            var excludedFolders = Config.CompileSet(config.ExcludedFolders, Config.NormalizeFolder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new XElement(ElementItem, f));

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XComment($" {AppMetadata.Title} (v{AppMetadata.Version}) "),
                new XComment($" {AppMetadata.RepositoryUrl} "),
                new XElement(RootElement,
                    new XElement(ElementSettings,
                        new XAttribute(AttrLanguage, normLang),
                        new XAttribute(AttrIconColor, normIcon)),
                    new XElement(ElementProcesses, excludedProcesses),
                    new XElement(ElementFolders, excludedFolders)
                )
            );

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var sw = new Utf8StringWriter())
            {
                using (var writer = XmlWriter.Create(sw, settings))
                {
                    doc.Save(writer);
                }
                return sw.ToString();
            }
        }

        public static bool TryParse(string content, out Config config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(content)) return false;

            try
            {
                XDocument doc = XDocument.Parse(content);
                XElement root = doc.Root;
                if (root == null || !string.Equals(root.Name.LocalName, RootElement, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string language = AppLanguage.Auto.Code;
                string iconColor = AppConstants.IconColors.Blue;
                bool hasSettings = false;

                XElement settingsElem = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, ElementSettings, StringComparison.OrdinalIgnoreCase));
                if (settingsElem != null)
                {
                    hasSettings = true;
                    XAttribute langAttr = settingsElem.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, AttrLanguage, StringComparison.OrdinalIgnoreCase));
                    if (langAttr != null && !string.IsNullOrWhiteSpace(langAttr.Value))
                    {
                        language = AppLanguage.FromCode(langAttr.Value.Trim()).Code;
                    }

                    XAttribute iconAttr = settingsElem.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, AttrIconColor, StringComparison.OrdinalIgnoreCase));
                    if (iconAttr != null && !string.IsNullOrWhiteSpace(iconAttr.Value))
                    {
                        string normIcon = iconAttr.Value.Trim().ToLowerInvariant();
                        if (AppConstants.IconColors.All.Contains(normIcon))
                        {
                            iconColor = normIcon;
                        }
                    }
                }

                var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                XElement procElem = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, ElementProcesses, StringComparison.OrdinalIgnoreCase));
                if (procElem != null)
                {
                    foreach (XElement item in procElem.Elements().Where(e => string.Equals(e.Name.LocalName, ElementItem, StringComparison.OrdinalIgnoreCase)))
                    {
                        string val = item.Value.Trim();
                        if (IsValidProcessRule(val))
                        {
                            processes.Add(val.ToLowerInvariant());
                        }
                    }
                }

                var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                XElement folderElem = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, ElementFolders, StringComparison.OrdinalIgnoreCase));
                if (folderElem != null)
                {
                    foreach (XElement item in folderElem.Elements().Where(e => string.Equals(e.Name.LocalName, ElementItem, StringComparison.OrdinalIgnoreCase)))
                    {
                        string val = item.Value.Trim();
                        if (IsValidFolderRule(val))
                        {
                            folders.Add(Config.NormalizeFolder(val));
                        }
                    }
                }

                if (!hasSettings && processes.Count == 0 && folders.Count == 0)
                {
                    return false;
                }

                Config result = new Config
                {
                    Language = language,
                    IconColor = iconColor
                };
                result.UpdateRules(folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase),
                                   processes.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

                config = result;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidProcessRule(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
            if (trimmed.Length <= 4) return false;
            if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (trimmed.IndexOfAny(InvalidFileNameChars) >= 0) return false;
            return true;
        }

        private static bool IsValidFolderRule(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
            if (trimmed.IndexOfAny(InvalidPathChars) >= 0) return false;

            // Must be an absolute rooted path: drive letter (e.g. C:\) or UNC (\\server\share)
            bool isDrive = trimmed.Length >= 3 &&
                           char.IsLetter(trimmed[0]) &&
                           trimmed[1] == ':' &&
                           (trimmed[2] == '\\' || trimmed[2] == '/');

            bool isUnc = (trimmed.StartsWith(@"\\") || trimmed.StartsWith("//")) && trimmed.Length > 3;

            return isDrive || isUnc;
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
