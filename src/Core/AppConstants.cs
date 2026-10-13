using System.Collections.Generic;

namespace CapsLang
{
    public static class AppConstants
    {
        public static class IconColors
        {
            public const string Blue = "blue";
            public const string Amber = "amber";

            public static readonly IReadOnlyList<string> All = new[] { Blue, Amber };
        }

        public const int MaxVisibleRuleRows = 4;
        public const double RuleItemHeight = 32.0;
        public const double RuleItemVerticalMargin = 2.0;
        public const double MaxRuleListHeight = MaxVisibleRuleRows * (RuleItemHeight + RuleItemVerticalMargin);
    }
}
