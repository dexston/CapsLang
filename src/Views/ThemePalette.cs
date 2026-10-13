using System.Windows;
using System.Windows.Media;

namespace CapsLang.Views
{
    internal static class ThemePalette
    {
        private static readonly string[] ResourceKeys =
        {
            "ResWindowBg",
            "ResCardBg",
            "ResCardBorder",
            "ResInputBg",
            "ResInputBorder",
            "ResBtnBg",
            "ResBtnHoverBg",
            "ResBtnBorder",
            "ResAccentBg",
            "ResAccentHoverBg",
            "ResTextPrimary",
            "ResTextSecondary",
            "ResTextMuted",
            "ResDeleteHoverBg",
            "ResDeleteHoverFg",
            "ResBadgeBg"
        };

        private static readonly SolidColorBrush AccentBlue = MakeFrozen(14, 124, 228);          // #0E7CE4
        private static readonly SolidColorBrush AccentBlueHoverDark = MakeFrozen(38, 144, 245);  // #2690F5
        private static readonly SolidColorBrush AccentBlueHoverLight = MakeFrozen(11, 104, 196); // #0B68C4
        private static readonly SolidColorBrush LogoBorderBlue = MakeFrozen(110, 190, 255);       // #6EBEFF

        private static readonly SolidColorBrush AccentAmber = MakeFrozen(228, 118, 14);         // #E4760E
        private static readonly SolidColorBrush AccentAmberHoverDark = MakeFrozen(245, 138, 38); // #F58A26
        private static readonly SolidColorBrush AccentAmberHoverLight = MakeFrozen(196, 98, 11); // #C4620B
        private static readonly SolidColorBrush LogoBorderAmber = MakeFrozen(255, 185, 110);      // #FFB96E

        private static readonly SolidColorBrush LogoPausedBg = MakeFrozen(89, 89, 89);          // #595959
        private static readonly SolidColorBrush LogoPausedBorder = MakeFrozen(153, 153, 153);   // #999999

        public static readonly Color DarkWindowBg = Color.FromRgb(32, 32, 32);     // #202020
        public static readonly Color LightWindowBg = Color.FromRgb(243, 243, 243); // #F3F3F3

        public static Color GetWindowBackgroundColor(bool dark) => dark ? DarkWindowBg : LightWindowBg;

        private static readonly SolidColorBrush DeleteHoverRed = MakeFrozen(165, 42, 42);    // #A52A2A

        private static readonly SolidColorBrush[] DarkBrushes = CreatePalette(true);
        private static readonly SolidColorBrush[] LightBrushes = CreatePalette(false);

        private static SolidColorBrush MakeFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush MakeFrozen(byte r, byte g, byte b) => MakeFrozen(Color.FromRgb(r, g, b));

        private static SolidColorBrush[] CreatePalette(bool dark)
        {
            return new[]
            {
                dark ? MakeFrozen(DarkWindowBg)  : MakeFrozen(LightWindowBg), // ResWindowBg
                dark ? MakeFrozen(43, 43, 43)    : MakeFrozen(255, 255, 255), // ResCardBg
                dark ? MakeFrozen(58, 58, 58)    : MakeFrozen(229, 229, 229), // ResCardBorder
                dark ? MakeFrozen(31, 31, 31)    : MakeFrozen(250, 250, 250), // ResInputBg
                dark ? MakeFrozen(82, 82, 82)    : MakeFrozen(198, 198, 198), // ResInputBorder
                dark ? MakeFrozen(55, 55, 55)    : MakeFrozen(251, 251, 251), // ResBtnBg
                dark ? MakeFrozen(68, 68, 68)    : MakeFrozen(240, 240, 240), // ResBtnHoverBg
                dark ? MakeFrozen(72, 72, 72)    : MakeFrozen(210, 210, 210), // ResBtnBorder
                AccentBlue,                                                   // ResAccentBg (default)
                dark ? AccentBlueHoverDark       : AccentBlueHoverLight,      // ResAccentHoverBg (default)
                dark ? MakeFrozen(245, 245, 245) : MakeFrozen(26, 26, 26),    // ResTextPrimary
                dark ? MakeFrozen(165, 165, 165) : MakeFrozen(96, 96, 96),    // ResTextSecondary
                dark ? MakeFrozen(120, 120, 120) : MakeFrozen(145, 145, 145), // ResTextMuted
                DeleteHoverRed,                                               // ResDeleteHoverBg (#A52A2A)
                MakeFrozen(255, 255, 255),                                    // ResDeleteHoverFg (White on #A52A2A)
                dark ? MakeFrozen(56, 56, 56)    : MakeFrozen(236, 236, 236)  // ResBadgeBg
            };
        }

        public static void Apply(ResourceDictionary resources, bool isDark, string iconColor, bool isPaused)
        {
            SolidColorBrush[] palette = isDark ? DarkBrushes : LightBrushes;
            for (int i = 0; i < ResourceKeys.Length; i++)
            {
                resources[ResourceKeys[i]] = palette[i];
            }
            ApplyAccent(resources, isDark, iconColor);
            ApplyLogo(resources, iconColor, isPaused);
        }

        public static void ApplyAccent(ResourceDictionary resources, bool isDark, string iconColor)
        {
            bool isAmber = string.Equals(iconColor, AppConstants.IconColors.Amber, System.StringComparison.OrdinalIgnoreCase);
            resources["ResAccentBg"] = isAmber ? AccentAmber : AccentBlue;
            resources["ResAccentHoverBg"] = isAmber
                ? (isDark ? AccentAmberHoverDark : AccentAmberHoverLight)
                : (isDark ? AccentBlueHoverDark : AccentBlueHoverLight);
            resources["ResAccentFg"] = Brushes.White;
        }

        public static void ApplyLogo(ResourceDictionary resources, string iconColor, bool isPaused)
        {
            if (isPaused)
            {
                resources["ResLogoBg"] = LogoPausedBg;
                resources["ResLogoBorder"] = LogoPausedBorder;
            }
            else
            {
                bool isAmber = string.Equals(iconColor, AppConstants.IconColors.Amber, System.StringComparison.OrdinalIgnoreCase);
                resources["ResLogoBg"] = isAmber ? AccentAmber : AccentBlue;
                resources["ResLogoBorder"] = isAmber ? LogoBorderAmber : LogoBorderBlue;
            }
        }
    }
}
