using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using CapsLang.Views;

namespace CapsLang
{
    public class AppTrayContext : ApplicationContext
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private NotifyIcon _trayIcon;
        private ContextMenu _contextMenu;
        private MenuItem _toggleItem;
        private MenuItem _settingsItem;
        private MenuItem _exitItem;
        private volatile SettingsWindow _activeSettingsForm;

        private Icon _blueIcon;
        private Icon _amberIcon;
        private Icon _blueBigIcon;
        private Icon _amberBigIcon;
        private Icon _pausedIcon;

        private Icon ActiveIcon => string.Equals(_config?.IconColor, AppConstants.IconColors.Amber, StringComparison.OrdinalIgnoreCase) ? _amberIcon : _blueIcon;
        private Icon ActiveBigIcon => string.Equals(_config?.IconColor, AppConstants.IconColors.Amber, StringComparison.OrdinalIgnoreCase) ? _amberBigIcon : _blueBigIcon;

        private KeyboardHook _hook;
        private Config _config;
        private bool _isPaused = false;

        private EventWaitHandle _quitEvent;
        private EventWaitHandle _settingsEvent;
        private readonly SynchronizationContext _syncContext;
        private readonly List<RegisteredWaitHandle> _registeredWaits = new List<RegisteredWaitHandle>();
        private volatile bool _disposed = false;

        public AppTrayContext()
        {
            _syncContext = SynchronizationContext.Current;
            bool isFirstRun = !Config.HasSettings();
            _config = Config.Load();
            Loc.Initialize(_config.Language);
            _hook = new KeyboardHook(() => _config, () => _isPaused);
            Loc.LanguageChanged += UpdateTrayStrings;

            LoadIcons();
            InitIpcEvents();
            InitTray();
            Application.ApplicationExit += OnApplicationExit;
            _hook.Install();

            if (isFirstRun)
            {
                try
                {
                    _config.Save();
                }
                catch { }
                ShowSettings();
            }
        }

        private void LoadIcons()
        {
            Size traySize = SystemInformation.SmallIconSize;
            Size bigSize = SystemInformation.IconSize;

            using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app.ico"))
            {
                if (s == null) throw new InvalidOperationException("Embedded resource 'app.ico' is missing.");
                _blueIcon = new Icon(s, traySize);
                s.Position = 0;
                _blueBigIcon = new Icon(s, bigSize);
            }

            using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app_amber.ico"))
            {
                if (s == null) throw new InvalidOperationException("Embedded resource 'app_amber.ico' is missing.");
                _amberIcon = new Icon(s, traySize);
                s.Position = 0;
                _amberBigIcon = new Icon(s, bigSize);
            }

            using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("paused.ico"))
            {
                if (s == null) throw new InvalidOperationException("Embedded resource 'paused.ico' is missing.");
                _pausedIcon = new Icon(s, traySize);
            }
        }

        private void InitIpcEvents()
        {
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            _settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.SettingsEventName);

            _quitEvent.Reset();
            _settingsEvent.Reset();

            RegisterIpcWait(_quitEvent, ExitApp);
            RegisterIpcWait(_settingsEvent, ShowSettings);
        }

        private void RegisterIpcWait(WaitHandle handle, Action action)
        {
            if (handle == null) return;
            RegisteredWaitHandle rwh = ThreadPool.RegisterWaitForSingleObject(
                handle,
                (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        _syncContext.Post(_ =>
                        {
                            if (_disposed) return;
                            action();
                        }, null);
                    }
                },
                null,
                Timeout.Infinite,
                false
            );
            _registeredWaits.Add(rwh);
        }

        private void InitTray()
        {
            _contextMenu = new ContextMenu();

            _toggleItem = new MenuItem(Loc.Tray.Menu.Toggle, (s, e) => SetPaused(!_isPaused))
            {
                Checked = !_isPaused
            };
            _settingsItem = new MenuItem(Loc.Tray.Menu.Settings, (s, e) => ShowSettings());
            _exitItem = new MenuItem(Loc.Tray.Menu.Exit, (s, e) => ExitApp());

            _contextMenu.MenuItems.Add(_toggleItem);
            _contextMenu.MenuItems.Add(_settingsItem);
            _contextMenu.MenuItems.Add(_exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = _isPaused ? _pausedIcon : ActiveIcon,
                ContextMenu = _contextMenu,
                Text = _isPaused ? Loc.Tray.Paused : Loc.Tray.Active,
                Visible = true
            };

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.SessionEnding += OnSessionEnding;

            _trayIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowSettings();
                }
            };
        }

        private void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (_toggleItem != null)
            {
                _toggleItem.Checked = !_isPaused;
            }
            UpdateTrayStrings();
            if (_activeSettingsForm != null)
            {
                _activeSettingsForm.UpdatePauseState(paused);
            }
        }

        private void SetIconColor(string iconColor)
        {
            string normColor = string.Equals(iconColor, AppConstants.IconColors.Amber, StringComparison.OrdinalIgnoreCase) 
                ? AppConstants.IconColors.Amber 
                : AppConstants.IconColors.Blue;
            if (_config != null)
            {
                _config.IconColor = normColor;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Icon = _isPaused ? _pausedIcon : ActiveIcon;
            }
            if (_activeSettingsForm != null)
            {
                _activeSettingsForm.UpdateAppIcon(ActiveIcon, ActiveBigIcon, normColor);
            }
        }

        private void UpdateTrayStrings()
        {
            if (_toggleItem != null)
            {
                _toggleItem.Text = Loc.Tray.Menu.Toggle;
                _toggleItem.Checked = !_isPaused;
            }
            if (_settingsItem != null)
            {
                _settingsItem.Text = Loc.Tray.Menu.Settings;
            }
            if (_exitItem != null)
            {
                _exitItem.Text = Loc.Tray.Menu.Exit;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Icon = _isPaused ? _pausedIcon : ActiveIcon;
                _trayIcon.Text = _isPaused ? Loc.Tray.Paused : Loc.Tray.Active;
            }
        }

        private void ShowSettings()
        {
            if (_activeSettingsForm != null)
            {
                if (_activeSettingsForm.WindowState == System.Windows.WindowState.Minimized)
                {
                    _activeSettingsForm.WindowState = System.Windows.WindowState.Normal;
                }
                _activeSettingsForm.Activate();
                return;
            }

            Config freshConfig = Config.Load();

            _activeSettingsForm = new SettingsWindow(
                freshConfig,
                _isPaused,
                SetPaused,
                SetIconColor,
                ActiveIcon,
                ActiveBigIcon,
                onConfigSaved: updatedConfig =>
                {
                    _config = updatedConfig;
                    if (!string.Equals(Loc.CurrentLanguageCode, _config.Language, StringComparison.OrdinalIgnoreCase))
                    {
                        Loc.SetLanguage(_config.Language);
                    }
                    SetIconColor(_config.IconColor);
                    if (_hook != null) _hook.ResetCache();
                });
            _activeSettingsForm.Closed += (s, e) =>
            {
                _activeSettingsForm = null;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (_activeSettingsForm == null && !_disposed)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            if (_activeSettingsForm == null && !_disposed)
                            {
                                TrimProcessMemory();
                            }
                        });
                    }
                }));
            };
            _activeSettingsForm.Show();
            _activeSettingsForm.Activate();
        }

        private static void TrimProcessMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                EmptyWorkingSet(GetCurrentProcess());
            }
            catch { }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_hook != null)
            {
                _hook.ResetCache();
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => ExitApp(), null);
            }
            else
            {
                ExitApp();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    Loc.LanguageChanged -= UpdateTrayStrings;
                    Application.ApplicationExit -= OnApplicationExit;
                    SystemEvents.SessionSwitch -= OnSessionSwitch;
                    SystemEvents.SessionEnding -= OnSessionEnding;
                    if (_hook != null)
                    {
                        _hook.Dispose();
                        _hook = null;
                    }
                    if (_activeSettingsForm != null)
                    {
                        _activeSettingsForm.Close();
                        _activeSettingsForm = null;
                    }
                    foreach (var rwh in _registeredWaits)
                    {
                        rwh.Unregister(null);
                    }
                    _registeredWaits.Clear();
                    if (_quitEvent != null) { _quitEvent.Close(); _quitEvent = null; }
                    if (_settingsEvent != null) { _settingsEvent.Close(); _settingsEvent = null; }
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = false;
                        _trayIcon.Dispose();
                        _trayIcon = null;
                    }
                    if (_contextMenu != null)
                    {
                        _contextMenu.Dispose();
                        _contextMenu = null;
                    }
                    if (_blueIcon != null)
                    {
                        _blueIcon.Dispose();
                        _blueIcon = null;
                    }
                    if (_amberIcon != null)
                    {
                        _amberIcon.Dispose();
                        _amberIcon = null;
                    }
                    if (_blueBigIcon != null)
                    {
                        _blueBigIcon.Dispose();
                        _blueBigIcon = null;
                    }
                    if (_amberBigIcon != null)
                    {
                        _amberBigIcon.Dispose();
                        _amberBigIcon = null;
                    }
                    if (_pausedIcon != null)
                    {
                        _pausedIcon.Dispose();
                        _pausedIcon = null;
                    }
                }
            }
            base.Dispose(disposing);
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            Dispose();
        }

        private void ExitApp()
        {
            Dispose();
            ExitThread();
        }
    }
}
