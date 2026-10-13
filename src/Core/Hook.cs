using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CapsLang
{
    public sealed class KeyboardHook : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int VK_SHIFT = 0x10;
        const int VK_CAPITAL = 0x14;
        const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        const uint INPUTLANGCHANGE_FORWARD = 0x0002;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint LLKHF_INJECTED = 0x00000010;

        // Field offsets in native tagKBDLLHOOKSTRUCT (Win32 ABI, x86/x64):
        // vkCode (DWORD, 4B) at offset 0, scanCode (DWORD, 4B) at offset 4, flags (DWORD, 4B) at offset 8
        const int KbdOffsetVkCode = 0;
        const int KbdOffsetFlags = 8;

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _hookProc;
        private readonly Func<Config> _getConfig;
        private readonly Func<bool> _getIsPaused;

        private enum CapsMode
        {
            Released = 0,
            Swallowed = 1,
            PassedThrough = 2
        }

        private struct WindowCacheEntry
        {
            public IntPtr Hwnd;
            public bool IsExcluded;
        }

        private static readonly int GuiThreadInfoSize = Marshal.SizeOf(typeof(GUITHREADINFO));

        private readonly object _cacheLock = new object();
        private readonly WindowCacheEntry[] _windowCache = new WindowCacheEntry[8];
        private int _cacheIndex = 0;
        private volatile CapsMode _capsMode = CapsMode.Released;
        private readonly StringBuilder _pathBuffer = new StringBuilder(1024);
        private readonly IntPtr[] _layoutBuffer = new IntPtr[16];

        public KeyboardHook(Func<Config> getConfig, Func<bool> getIsPaused)
        {
            _getConfig = getConfig;
            _getIsPaused = getIsPaused;
            _hookProc = HookCallback;
        }

        public bool Install()
        {
            if (_hookId != IntPtr.Zero) return true;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            return _hookId != IntPtr.Zero;
        }

        public void Uninstall()
        {
            IntPtr hhk = Interlocked.Exchange(ref _hookId, IntPtr.Zero);
            if (hhk != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hhk);
            }
            _capsMode = CapsMode.Released;
        }

        public void Dispose()
        {
            Uninstall();
            GC.SuppressFinalize(this);
        }

        ~KeyboardHook()
        {
            Uninstall();
        }

        public void ResetCache()
        {
            lock (_cacheLock)
            {
                for (int i = 0; i < _windowCache.Length; i++)
                {
                    _windowCache[i].Hwnd = IntPtr.Zero;
                    _windowCache[i].IsExcluded = false;
                }
                _cacheIndex = 0;
            }
            _capsMode = CapsMode.Released;
        }

        private void CacheWindow(IntPtr hwnd, bool isExcluded)
        {
            lock (_cacheLock)
            {
                _windowCache[_cacheIndex].Hwnd = hwnd;
                _windowCache[_cacheIndex].IsExcluded = isExcluded;
                _cacheIndex = (_cacheIndex + 1) % _windowCache.Length;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = (int)wParam;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        if (lParam == IntPtr.Zero)
                        {
                            return CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }

                        uint vkCode = (uint)Marshal.ReadInt32(lParam, KbdOffsetVkCode);
                        if (vkCode == VK_CAPITAL)
                        {
                            uint flags = (uint)Marshal.ReadInt32(lParam, KbdOffsetFlags);
                            if ((flags & LLKHF_INJECTED) != 0)
                            {
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                            {
                                CapsMode prev = _capsMode;
                                _capsMode = CapsMode.Released;
                                if (prev == CapsMode.Swallowed)
                                {
                                    return (IntPtr)1;
                                }
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            if (_capsMode != CapsMode.Released)
                            {
                                if (_capsMode == CapsMode.PassedThrough)
                                {
                                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                                }
                                return (IntPtr)1;
                            }

                            if (_getIsPaused != null && _getIsPaused())
                            {
                                _capsMode = CapsMode.PassedThrough;
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            if ((GetKeyState(VK_SHIFT) & 0x8000) != 0)
                            {
                                _capsMode = CapsMode.PassedThrough;
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            IntPtr hwnd = GetForegroundWindow();
                            if (hwnd == IntPtr.Zero || IsExcludedWindow(hwnd))
                            {
                                _capsMode = CapsMode.PassedThrough;
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            _capsMode = CapsMode.Swallowed;

                            IntPtr targetHwnd = hwnd;
                            GUITHREADINFO gui = new GUITHREADINFO();
                            gui.cbSize = GuiThreadInfoSize;
                            if (GetGUIThreadInfo(0, ref gui) && gui.hwndFocus != IntPtr.Zero)
                            {
                                targetHwnd = gui.hwndFocus;
                            }

                            IntPtr nextHkl = GetNextKeyboardLayout(targetHwnd);
                            if (nextHkl == IntPtr.Zero && targetHwnd != hwnd)
                            {
                                targetHwnd = hwnd;
                                nextHkl = GetNextKeyboardLayout(hwnd);
                            }

                            bool sent = PostMessage(targetHwnd, WM_INPUTLANGCHANGEREQUEST, (IntPtr)INPUTLANGCHANGE_FORWARD, nextHkl);
                            if (!sent)
                            {
                                _capsMode = CapsMode.PassedThrough;
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch
            {
                _capsMode = CapsMode.Released;
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private IntPtr GetNextKeyboardLayout(IntPtr hwnd)
        {
            try
            {
                uint pid;
                uint tid = GetWindowThreadProcessId(hwnd, out pid);
                if (tid == 0) return IntPtr.Zero;

                IntPtr currentHkl = GetKeyboardLayout(tid);
                int count = GetKeyboardLayoutList(_layoutBuffer.Length, _layoutBuffer);
                if (count <= 1) return currentHkl;
                if (count > _layoutBuffer.Length) count = _layoutBuffer.Length;

                for (int i = 0; i < count; i++)
                {
                    if (_layoutBuffer[i] == currentHkl)
                    {
                        return _layoutBuffer[(i + 1) % count];
                    }
                }

                long curLcid = (long)currentHkl & 0xFFFF;
                for (int i = 0; i < count; i++)
                {
                    if (((long)_layoutBuffer[i] & 0xFFFF) == curLcid)
                    {
                        return _layoutBuffer[(i + 1) % count];
                    }
                }

                return _layoutBuffer[0];
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private bool IsExcludedWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                    return false;

                lock (_cacheLock)
                {
                    for (int i = 0; i < _windowCache.Length; i++)
                    {
                        if (_windowCache[i].Hwnd == hwnd)
                        {
                            return _windowCache[i].IsExcluded;
                        }
                    }
                }

                Config config = _getConfig != null ? _getConfig() : null;
                CompiledRules rules = config?.Rules;
                if (rules == null || !rules.HasRules)
                {
                    CacheWindow(hwnd, false);
                    return false;
                }

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0)
                {
                    CacheWindow(hwnd, true);
                    return true;
                }

                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    CacheWindow(hwnd, true);
                    return true;
                }

                bool success;
                try
                {
                    _pathBuffer.Clear();
                    uint size = (uint)_pathBuffer.Capacity;
                    success = QueryFullProcessImageName(hProcess, 0, _pathBuffer, ref size);
                }
                finally
                {
                    CloseHandle(hProcess);
                }

                if (!success)
                {
                    CacheWindow(hwnd, true);
                    return true;
                }

                // 1. Process match: O(1) HashSet check without allocating fullPath
                HashSet<string> procSet = rules.ProcessSet;
                if (procSet != null && procSet.Count > 0 && _pathBuffer.Length > 0)
                {
                    int lastSlash = -1;
                    for (int i = _pathBuffer.Length - 1; i >= 0; i--)
                    {
                        char c = _pathBuffer[i];
                        if (c == '\\' || c == '/')
                        {
                            lastSlash = i;
                            break;
                        }
                    }

                    int nameStart = lastSlash >= 0 ? lastSlash + 1 : 0;
                    int nameLen = _pathBuffer.Length - nameStart;
                    if (nameLen > 0)
                    {
                        string procName = _pathBuffer.ToString(nameStart, nameLen);
                        if (procSet.Contains(procName))
                        {
                            CacheWindow(hwnd, true);
                            return true;
                        }
                    }
                }

                // 2. Folder match: only allocate fullPath if folder rules actually exist
                string[] folderRules = rules.FolderRules;
                if (folderRules != null && folderRules.Length > 0)
                {
                    string fullPath = _pathBuffer.ToString();
                    for (int i = 0; i < folderRules.Length; i++)
                    {
                        string folder = folderRules[i];
                        if (string.IsNullOrEmpty(folder)) continue;

                        if (fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            CacheWindow(hwnd, true);
                            return true;
                        }
                    }
                }

                CacheWindow(hwnd, false);
                return false;
            }
            catch
            {
                CacheWindow(hwnd, true);
                return true;
            }
        }
    }
}
