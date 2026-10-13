using System;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapsLang
{
    public class RegistryStartupService : IStartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "CapsLang";

        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    string val = key.GetValue(AppName) as string;
                    if (string.IsNullOrWhiteSpace(val)) return false;

                    string registeredPath = val.Trim(Config.CleanTrimChars);
                    return string.Equals(registeredPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) when (ex is SecurityException ||
                                      ex is UnauthorizedAccessException ||
                                      ex is IOException)
            {
                return false;
            }
        }

        public void SetEnabled(bool enable)
        {
            if (enable)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        throw new UnauthorizedAccessException(RunKeyPath);
                    }
                    key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    key?.DeleteValue(AppName, false);
                }
            }
        }
    }
}
