using System;
using System.Windows.Input;

namespace CapsLang.ViewModels
{
    public sealed class LanguageOptionViewModel : ViewModelBase
    {
        private bool _isSelected;

        public AppLanguage Language { get; }

        public string Code => Language.Code;
        public string Title => Language.Title;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ICommand SelectCommand { get; }

        public LanguageOptionViewModel(
            AppLanguage language,
            bool isSelected,
            Action<AppLanguage> onSelect)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
            _isSelected = isSelected;
            SelectCommand = new RelayCommand(() => onSelect?.Invoke(language));
        }

        public void RefreshLocalization(string currentLanguageCode)
        {
            OnPropertyChanged(nameof(Title));
            IsSelected = string.Equals(Language.Code, currentLanguageCode, StringComparison.OrdinalIgnoreCase);
        }
    }
}
