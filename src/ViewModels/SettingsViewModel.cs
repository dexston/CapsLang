using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace CapsLang.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly Config _config;
        private readonly Action<bool> _onPausedChanged;
        private readonly Action<string> _onIconColorChanged;
        private readonly Action<Config> _onConfigSaved;
        private readonly Func<string> _pickProcessFile;
        private readonly Func<string> _pickFolder;
        private readonly Func<string> _pickExportFile;
        private readonly Func<string> _pickImportFile;
        private readonly Action<string, string, bool> _showMessage;

        private bool _isPaused;
        private bool _isStartupEnabled;
        private bool _disposed;
        public RuleSectionViewModel ProcessesSection { get; }
        public RuleSectionViewModel FoldersSection { get; }

        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand AddProcessCommand { get; }
        public ICommand AddFolderCommand { get; }

        public event Action StatusChanged;
        public event Action ShareCopied;

        public SettingsViewModel(
            Config config,
            bool isPaused,
            Action<bool> onPausedChanged,
            Action<string> onIconColorChanged,
            Action<Config> onConfigSaved,
            Func<string> pickProcessFile,
            Func<string> pickFolder,
            Func<string> pickExportFile,
            Func<string> pickImportFile,
            Action<string, string, bool> showMessage)
        {
            _config = config;
            _isPaused = isPaused;
            _onPausedChanged = onPausedChanged;
            _onIconColorChanged = onIconColorChanged;
            _onConfigSaved = onConfigSaved;
            _pickProcessFile = pickProcessFile;
            _pickFolder = pickFolder;
            _pickExportFile = pickExportFile;
            _pickImportFile = pickImportFile;
            _showMessage = showMessage;

            _isStartupEnabled = StartupService.Current.IsEnabled();

            ProcessesSection = new RuleSectionViewModel(
                _config.ExcludedProcesses,
                () => Loc.Settings.Processes.Title,
                () => Loc.Settings.Processes.Desc,
                () => Loc.Settings.EmptyHint,
                candidate => TrySaveRules(FoldersSection?.Items ?? _config.ExcludedFolders, candidate));

            FoldersSection = new RuleSectionViewModel(
                _config.ExcludedFolders,
                () => Loc.Settings.Folders.Title,
                () => Loc.Settings.Folders.Desc,
                () => Loc.Settings.EmptyHint,
                candidate => TrySaveRules(candidate, ProcessesSection?.Items ?? _config.ExcludedProcesses));

            ExportCommand = new RelayCommand(ExecuteExport);
            ImportCommand = new RelayCommand(ExecuteImport);
            AddProcessCommand = new RelayCommand(ExecuteAddProcess);
            AddFolderCommand = new RelayCommand(ExecuteAddFolder);
            OpenSourceCodeCommand = new RelayCommand(ExecuteOpenSourceCode);
            ShareCommand = new RelayCommand(ExecuteShare);
            ToggleEasterEggThemeCommand = new RelayCommand(ExecuteToggleEasterEggTheme);

            var languages = new List<LanguageOptionViewModel>();
            foreach (AppLanguage lang in AppLanguage.All)
            {
                bool isSelected = string.Equals(_config.Language, lang.Code, StringComparison.OrdinalIgnoreCase);
                languages.Add(new LanguageOptionViewModel(lang, isSelected, ExecuteSelectLanguage));
            }
            Languages = languages.AsReadOnly();

            Loc.LanguageChanged += OnLanguageChanged;
        }


        public string WindowTitle => Loc.Settings.Title;
        public string StatusText => _isPaused ? Loc.Tray.Paused : Loc.Tray.Active;
        public string ActiveTitle => Loc.Settings.Active.Title;
        public string ActiveDesc => Loc.Settings.Active.Desc;
        public string StartupTitle => Loc.Settings.Startup.Title;
        public string StartupDesc => Loc.Settings.Startup.Desc;
        public string BackupTooltip => Loc.Settings.Header.TooltipBackup;
        public string ExportMenuHeader => Loc.Settings.Menu.Export;
        public string ImportMenuHeader => Loc.Settings.Menu.Import;

        public string AppTitle => AppMetadata.Title;
#if PORTABLE
        public string AppVersionText => Loc.Settings.Header.VersionPortable(AppMetadata.Version);
        public string AppVersionTooltip => Loc.Settings.Header.TooltipVersionPortable(AppMetadata.Version);
#else
        public string AppVersionText => Loc.Settings.Header.Version(AppMetadata.Version);
        public string AppVersionTooltip => Loc.Settings.Header.TooltipVersion(AppMetadata.Version);
#endif
        public string AppDescriptionText => Loc.Settings.Header.Description;
        public string AppCopyrightText => AppMetadata.Copyright;
        public string SourceCodeTooltip => Loc.Settings.Header.TooltipSourceCode;
        public string ShareTooltip => Loc.Settings.Header.TooltipShare;
        public string ShareCopiedText => Loc.Settings.Header.TooltipShareCopied;
        public string LanguageTooltip => Loc.Settings.Header.TooltipLanguage;

        public IReadOnlyList<LanguageOptionViewModel> Languages { get; }

        public ICommand OpenSourceCodeCommand { get; }
        public ICommand ShareCommand { get; }
        public ICommand ToggleEasterEggThemeCommand { get; }

        public bool IsPaused => _isPaused;

        public bool IsActive
        {
            get => !_isPaused;
            set
            {
                bool targetPaused = !value;
                if (_isPaused != targetPaused)
                {
                    _isPaused = targetPaused;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPaused));
                    OnPropertyChanged(nameof(StatusText));
                    StatusChanged?.Invoke();
                    _onPausedChanged?.Invoke(_isPaused);
                }
            }
        }

        public void SyncPausedState(bool isPaused)
        {
            if (_disposed) return;

            if (_isPaused != isPaused)
            {
                _isPaused = isPaused;
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(StatusText));
                StatusChanged?.Invoke();
            }
        }

        public bool IsStartupEnabled
        {
            get => _isStartupEnabled;
            set
            {
                if (_isStartupEnabled == value) return;
                try
                {
                    StartupService.Current.SetEnabled(value);
                    _isStartupEnabled = value;
                    OnPropertyChanged();
                }
                catch (Exception ex)
                {
                    OnPropertyChanged();
                    if (ex is UnauthorizedAccessException || ex is SecurityException)
                    {
                        _showMessage?.Invoke(Loc.Dialog.Save.StorageBlocked(ex.Message), Loc.Dialog.Save.AccessTitle, true);
                    }
                    else
                    {
                        _showMessage?.Invoke(Loc.Dialog.Save.Error(ex.Message), Loc.Dialog.Save.ErrorTitle, true);
                    }
                }
            }
        }


        private void ExecuteAddProcess()
        {
            string file = _pickProcessFile?.Invoke();
            if (!string.IsNullOrEmpty(file))
            {
                string clean = Config.NormalizeProcess(file);
                ProcessesSection.TryAddItem(clean);
            }
        }

        private void ExecuteAddFolder()
        {
            string path = _pickFolder?.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                string clean = Config.NormalizeFolder(path);
                FoldersSection.TryAddItem(clean);
            }
        }

        private void ExecuteExport()
        {
            string targetFile = _pickExportFile?.Invoke();
            if (string.IsNullOrEmpty(targetFile)) return;

            try
            {
                string xmlContent = ConfigXml.Export(_config);
                File.WriteAllText(targetFile, xmlContent, Encoding.UTF8);
                _showMessage?.Invoke(Loc.Dialog.Export.Success(targetFile), Loc.Dialog.Export.SuccessTitle, false);
            }
            catch (Exception ex)
            {
                _showMessage?.Invoke(Loc.Dialog.Export.Error(ex.Message), Loc.Dialog.Export.ErrorTitle, true);
            }
        }

        private void ExecuteImport()
        {
            string sourceFile = _pickImportFile?.Invoke();
            if (string.IsNullOrEmpty(sourceFile)) return;

            ImportFromXml(sourceFile);
        }

        public bool ImportFromXml(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            try
            {
                if (!File.Exists(filePath))
                {
                    _showMessage?.Invoke(Loc.Dialog.Import.NotFound(filePath), Loc.Dialog.Import.ErrorTitle, true);
                    return false;
                }

                string content = File.ReadAllText(filePath, Encoding.UTF8);
                if (!ConfigXml.TryParse(content, out Config importedConfig))
                {
                    _showMessage?.Invoke(Loc.Dialog.Import.InvalidFile, Loc.Dialog.Import.ErrorTitle, true);
                    return false;
                }

                // 1. Save imported config to registry first (Source of Truth)
                importedConfig.Save();

                // 2. Apply to current VM config
                _config.ApplyFrom(importedConfig);

                // 3. Update UI collections
                ProcessesSection.ResetItems(_config.ExcludedProcesses);
                FoldersSection.ResetItems(_config.ExcludedFolders);

                // 4. Update language and theme in UI
                Loc.SetLanguage(_config.Language);
                UpdateLanguageSelection();
                _onIconColorChanged?.Invoke(_config.IconColor);

                // 5. Notify app runtime
                _onConfigSaved?.Invoke(_config);

                _showMessage?.Invoke(Loc.Dialog.Import.Success(filePath), Loc.Dialog.Import.SuccessTitle, false);
                return true;
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    _showMessage?.Invoke(Loc.Dialog.Save.StorageBlocked(ex.Message), Loc.Dialog.Save.AccessTitle, true);
                }
                else
                {
                    _showMessage?.Invoke(Loc.Dialog.Import.Error(ex.Message), Loc.Dialog.Import.ErrorTitle, true);
                }
                return false;
            }
        }

        private bool SaveConfigAndNotify()
        {
            try
            {
                _config.Save();
                _onConfigSaved?.Invoke(_config);
                return true;
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    _showMessage?.Invoke(Loc.Dialog.Save.StorageBlocked(ex.Message), Loc.Dialog.Save.AccessTitle, true);
                }
                else
                {
                    _showMessage?.Invoke(Loc.Dialog.Save.Error(ex.Message), Loc.Dialog.Save.ErrorTitle, true);
                }
                return false;
            }
        }

        private bool TrySaveRules(IEnumerable<string> folders, IEnumerable<string> processes)
        {
            _config.UpdateRules(folders, processes);
            if (SaveConfigAndNotify())
            {
                return true;
            }

            _config.UpdateRules(FoldersSection?.Items ?? _config.ExcludedFolders, ProcessesSection?.Items ?? _config.ExcludedProcesses);
            return false;
        }

        private void OnLanguageChanged()
        {
            OnPropertyChanged(string.Empty);

            UpdateLanguageSelection();

            ProcessesSection.RefreshLocalization();
            FoldersSection.RefreshLocalization();
        }

        private void ExecuteOpenSourceCode()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppMetadata.RepositoryUrl) { UseShellExecute = true });
            }
            catch { }
        }

        private void ExecuteShare()
        {
            try
            {
                Clipboard.SetText(AppMetadata.RepositoryUrl);
                ShareCopied?.Invoke();
            }
            catch (Exception ex)
            {
                _showMessage?.Invoke(ex.Message, Loc.Settings.Title, true);
            }
        }

        private void ExecuteToggleEasterEggTheme()
        {
            string prevColor = _config.IconColor;
            string nextColor = string.Equals(prevColor, AppConstants.IconColors.Amber, StringComparison.OrdinalIgnoreCase)
                ? AppConstants.IconColors.Blue
                : AppConstants.IconColors.Amber;

            _config.IconColor = nextColor;
            if (SaveConfigAndNotify())
            {
                _onIconColorChanged?.Invoke(nextColor);
            }
            else
            {
                _config.IconColor = prevColor;
            }
        }

        private void ExecuteSelectLanguage(AppLanguage lang)
        {
            if (lang == null) return;

            string prevLang = _config.Language;
            if (!string.Equals(prevLang, lang.Code, StringComparison.OrdinalIgnoreCase))
            {
                _config.Language = lang.Code;
                if (SaveConfigAndNotify())
                {
                    Loc.SetLanguage(lang);
                }
                else
                {
                    _config.Language = prevLang;
                }
            }
        }

        private void UpdateLanguageSelection()
        {
            foreach (var lang in Languages)
            {
                lang.RefreshLocalization(_config.Language);
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Loc.LanguageChanged -= OnLanguageChanged;
            StatusChanged = null;
            ShareCopied = null;
        }
    }
}
