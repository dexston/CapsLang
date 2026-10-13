using System;
using System.IO;
using System.Windows.Forms;

namespace CapsLang
{
    public class ShortcutStartupService : IStartupService
    {
        public const string ShortcutFileName = "CapsLang.lnk";

        private readonly string _startupFolder;
        private readonly string _shortcutPath;

        public ShortcutStartupService()
            : this(Environment.GetFolderPath(Environment.SpecialFolder.Startup))
        {
        }

        public ShortcutStartupService(string startupFolder)
        {
            string folder = !string.IsNullOrWhiteSpace(startupFolder)
                ? startupFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.Startup);

            _startupFolder = !string.IsNullOrWhiteSpace(folder) ? folder : null;
            _shortcutPath = _startupFolder != null ? Path.Combine(_startupFolder, ShortcutFileName) : null;
        }

        public bool IsEnabled()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_shortcutPath) || !File.Exists(_shortcutPath)) return false;

                string target = ShortcutHelper.GetShortcutTarget(_shortcutPath);
                if (string.IsNullOrWhiteSpace(target)) return false;

                string registeredPath = target.Trim(Config.CleanTrimChars);
                return string.Equals(registeredPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void SetEnabled(bool enable)
        {
            if (string.IsNullOrWhiteSpace(_startupFolder) || string.IsNullOrWhiteSpace(_shortcutPath))
            {
                if (enable)
                {
                    throw new DirectoryNotFoundException("Startup folder is unavailable.");
                }
                return;
            }

            if (enable)
            {
                if (!Directory.Exists(_startupFolder))
                {
                    Directory.CreateDirectory(_startupFolder);
                }

                ShortcutHelper.CreateShortcut(
                    _shortcutPath,
                    Application.ExecutablePath,
                    "CapsLang keyboard layout switcher",
                    Application.StartupPath
                );
            }
            else
            {
                if (File.Exists(_shortcutPath))
                {
                    File.Delete(_shortcutPath);
                }
            }
        }
    }
}
