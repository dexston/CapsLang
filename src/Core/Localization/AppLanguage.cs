using System;
using System.Collections.Generic;

namespace CapsLang
{
    public sealed class AppLanguage : IEquatable<AppLanguage>
    {
        private readonly Func<string> _titleGetter;

        public string Code { get; }
        public string Title => _titleGetter();

        public static readonly AppLanguage Auto = new AppLanguage("auto", () => Loc.Language.Auto);
        public static readonly AppLanguage Russian = new AppLanguage("ru", () => Loc.Language.Ru);
        public static readonly AppLanguage English = new AppLanguage("en", () => Loc.Language.En);

        public static readonly IReadOnlyList<AppLanguage> All = new[] { Auto, Russian, English };

        private AppLanguage(string code, Func<string> titleGetter)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            _titleGetter = titleGetter ?? throw new ArgumentNullException(nameof(titleGetter));
        }

        public static AppLanguage FromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Auto;
            }

            string clean = code.Trim();
            foreach (var lang in All)
            {
                if (string.Equals(lang.Code, clean, StringComparison.OrdinalIgnoreCase))
                {
                    return lang;
                }
            }

            return Auto;
        }

        public bool Equals(AppLanguage other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => obj is AppLanguage other && Equals(other);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Code);

        public override string ToString() => Code;

        public static bool operator ==(AppLanguage left, AppLanguage right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(AppLanguage left, AppLanguage right) => !(left == right);
    }
}
