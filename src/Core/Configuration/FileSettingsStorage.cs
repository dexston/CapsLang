using System;
using System.IO;
using System.Text;

namespace CapsLang
{
    public class FileSettingsStorage : ISettingsStorage
    {
        public const string SettingsFileName = "CapsLang_Settings.xml";

        private readonly object _saveLock = new object();

        public string SettingsFilePath { get; }

        public FileSettingsStorage()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName))
        {
        }

        public FileSettingsStorage(string customFilePath)
        {
            SettingsFilePath = customFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        }

        public bool HasSettings()
        {
            try
            {
                var fi = new FileInfo(SettingsFilePath);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public Config Load()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new Config();
            }

            try
            {
                string content = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                if (ConfigXml.TryParse(content, out Config config))
                {
                    return config;
                }
            }
            catch
            {
            }

            return new Config();
        }

        public void Save(Config config)
        {
            if (config == null) return;

            lock (_saveLock)
            {
                string targetPath = SettingsFilePath;
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

                try
                {
                    string xmlContent = ConfigXml.Export(config);
                    File.WriteAllText(tempPath, xmlContent, Encoding.UTF8);

                    if (File.Exists(targetPath))
                    {
                        try
                        {
                            File.Replace(tempPath, targetPath, null);
                        }
                        catch
                        {
                            File.Copy(tempPath, targetPath, true);
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
        }
    }
}
