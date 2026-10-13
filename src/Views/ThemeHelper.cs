using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using System.Windows.Media;
using Microsoft.Win32;

namespace CapsLang
{
    public static class ThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static bool IsDarkTheme(bool checkSystemShell = false)
        {
            if (SystemInformation.HighContrast)
            {
                return false;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    string valName = checkSystemShell ? "SystemUsesLightTheme" : "AppsUseLightTheme";
                    object raw = key?.GetValue(valName) ?? (checkSystemShell ? key?.GetValue("AppsUseLightTheme") : null);
                    if (raw is int val)
                    {
                        return val == 0;
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
            }
            return false;
        }

        public static void ApplyTitleBarTheme(IntPtr hwnd, bool isDark, Color captionColor)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int darkMode = isDark ? 1 : 0;
                // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 for Windows 10 20H1+ and Windows 11, 19 for Windows 10 1809-1903
                if (DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref darkMode, sizeof(int));
                }

                // DWMWA_WINDOW_CORNER_PREFERENCE (33): 2 = DWMWCP_ROUND (Windows 11)
                int cornerPreference = 2;
                DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));

                // DWMWA_CAPTION_COLOR (35): match title bar to window background on Windows 11
                // Win32 COLORREF format: 0x00BBGGRR
                int colorRef = (captionColor.B << 16) | (captionColor.G << 8) | captionColor.R;
                DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
