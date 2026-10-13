using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CapsLang.ViewModels;
using Microsoft.Win32;

namespace CapsLang.Views
{
    public partial class SettingsWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_THEMECHANGED = 0x031A;
        private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const int WM_INPUTLANGCHANGE = 0x0051;

        private readonly SettingsViewModel _viewModel;
        private DispatcherTimer _sharePopupTimer;
        private bool _isDark;
        private string _iconColor;
        private System.Drawing.Icon _smallIcon;
        private System.Drawing.Icon _bigIcon;

        public SettingsWindow(
            Config config,
            bool isPaused,
            Action<bool> onPausedChanged,
            Action<string> onIconColorChanged,
            System.Drawing.Icon smallIcon,
            System.Drawing.Icon bigIcon,
            Action<Config> onConfigSaved = null)
        {
            _isDark = ThemeHelper.IsDarkTheme();
            _iconColor = config?.IconColor ?? AppConstants.IconColors.Blue;
            InitializeComponent();

            _viewModel = new SettingsViewModel(
                config,
                isPaused,
                onPausedChanged,
                onIconColorChanged,
                onConfigSaved,
                PickProcessFile,
                PickFolder,
                PickExportFile,
                PickImportFile,
                ShowMessageDialog);

            this.DataContext = _viewModel;
            _viewModel.StatusChanged += UpdateLogoState;
            _viewModel.ShareCopied += OnShareCopied;

            this.MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 32);
            ThemePalette.Apply(this.Resources, _isDark, _iconColor, _viewModel.IsPaused);
            UpdateAppIcon(smallIcon, bigIcon, _iconColor);

            this.SourceInitialized += OnWindowSourceInitialized;
            this.LocationChanged += (s, e) => DismissSharePopup();
            this.Deactivated += (s, e) => DismissSharePopup();
            RootScrollViewer.ScrollChanged += (s, e) => DismissSharePopup();
            ShareCopiedPopup.Closed += (s, e) => _sharePopupTimer?.Stop();
            this.Closed += OnWindowClosed;
        }

        public void UpdateAppIcon(System.Drawing.Icon smallIcon, System.Drawing.Icon bigIcon, string iconColor)
        {
            _smallIcon = smallIcon;
            _bigIcon = bigIcon;
            _iconColor = iconColor ?? AppConstants.IconColors.Blue;

            ThemePalette.ApplyAccent(this.Resources, _isDark, _iconColor);
            ThemePalette.ApplyLogo(this.Resources, _iconColor, _viewModel.IsPaused);

            System.Drawing.Icon targetForWpf = _bigIcon ?? _smallIcon;
            if (targetForWpf != null)
            {
                try
                {
                    BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                        targetForWpf.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    this.Icon = bmp;
                }
                catch { }
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyNativeWindowIcons(hwnd);
            }
        }

        private void ApplyNativeWindowIcons(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (_bigIcon != null)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, _bigIcon.Handle);
            }
            if (_smallIcon != null)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, _smallIcon.Handle);
            }
        }

        public void UpdatePauseState(bool isPaused)
        {
            _viewModel.SyncPausedState(isPaused);
        }

        private void UpdateLogoState()
        {
            ThemePalette.ApplyLogo(this.Resources, _iconColor, _viewModel.IsPaused);
        }

        private void OnLanguageButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.ContextMenu != null)
            {
                element.ContextMenu.PlacementTarget = element;
                element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                element.ContextMenu.IsOpen = true;
            }
        }

        private void OnBackupButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.ContextMenu != null)
            {
                element.ContextMenu.PlacementTarget = element;
                element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                element.ContextMenu.IsOpen = true;
            }
        }

        private void OnWindowSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            ThemeHelper.ApplyTitleBarTheme(hwnd, _isDark, ThemePalette.GetWindowBackgroundColor(_isDark));
            ApplyNativeWindowIcons(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        private void OnShareCopied()
        {
            if (BtnShare != null)
            {
                ToolTipService.SetIsEnabled(BtnShare, false);
                ToolTipService.SetIsEnabled(BtnShare, true);
            }

            if (_sharePopupTimer == null)
            {
                _sharePopupTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _sharePopupTimer.Tick += (s, e) =>
                {
                    _sharePopupTimer.Stop();
                    if (ShareCopiedPopup != null)
                    {
                        ShareCopiedPopup.IsOpen = false;
                    }
                };
            }

            _sharePopupTimer.Stop();
            if (ShareCopiedPopup != null)
            {
                ShareCopiedPopup.IsOpen = false;
                ShareCopiedPopup.IsOpen = true;
            }
            _sharePopupTimer.Start();
        }

        private void DismissSharePopup()
        {
            if (ShareCopiedPopup != null && ShareCopiedPopup.IsOpen)
            {
                ShareCopiedPopup.IsOpen = false;
                _sharePopupTimer?.Stop();
            }
        }

        private void OnSharePopupMouseDown(object sender, MouseButtonEventArgs e)
        {
            DismissSharePopup();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUTLANGCHANGEREQUEST || msg == WM_INPUTLANGCHANGE)
            {
                handled = false;
            }
            else if (msg == WM_SETTINGCHANGE || msg == WM_THEMECHANGED || msg == WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                RefreshThemeIfNeeded(hwnd);
            }
            return IntPtr.Zero;
        }

        private void RefreshThemeIfNeeded(IntPtr hwnd)
        {
            bool nowDark = ThemeHelper.IsDarkTheme();
            if (nowDark == _isDark) return;

            _isDark = nowDark;
            ThemeHelper.ApplyTitleBarTheme(hwnd, _isDark, ThemePalette.GetWindowBackgroundColor(_isDark));
            ThemePalette.Apply(this.Resources, _isDark, _iconColor, _viewModel.IsPaused);
        }

        private string PickProcessFile()
        {
            var ofd = new OpenFileDialog
            {
                Title = Loc.Dialog.Process.Title,
                Filter = Loc.Dialog.Process.Filter,
                CheckFileExists = true
            };
            return ofd.ShowDialog(this) == true ? ofd.FileName : null;
        }

        private string PickFolder()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            return NativeFolderPicker.ShowDialog(hwnd, Loc.Dialog.Folder.Prompt);
        }

        private string PickExportFile()
        {
            var sfd = new SaveFileDialog
            {
                Title = Loc.Dialog.Export.Title,
                Filter = Loc.Dialog.Export.Filter,
                FileName = $"{_viewModel.AppTitle}_Config.xml",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                DefaultExt = "xml",
                AddExtension = true,
                OverwritePrompt = true
            };
            return sfd.ShowDialog(this) == true ? sfd.FileName : null;
        }

        private string PickImportFile()
        {
            var ofd = new OpenFileDialog
            {
                Title = Loc.Dialog.Import.Title,
                Filter = Loc.Dialog.Import.Filter,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                DefaultExt = "xml",
                CheckFileExists = true
            };
            return ofd.ShowDialog(this) == true ? ofd.FileName : null;
        }

        private void ShowMessageDialog(string message, string title, bool isError)
        {
            MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButton.OK,
                isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hwnd)?.RemoveHook(WndProc);
            }

            if (_sharePopupTimer != null)
            {
                _sharePopupTimer.Stop();
                _sharePopupTimer = null;
            }
            if (ShareCopiedPopup != null)
            {
                ShareCopiedPopup.IsOpen = false;
            }

            _viewModel.StatusChanged -= UpdateLogoState;
            _viewModel.ShareCopied -= OnShareCopied;
            _viewModel.Dispose();
            this.DataContext = null;
            this.Content = null;
            this.Resources.Clear();
        }
    }
}
