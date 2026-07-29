using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WINHELP
{
    /// <summary>
    /// 陪伴运行（小窗模式）总管理器 — 单例
    /// 职责：进入/退出小窗模式（进入时隐藏同程序其他所有窗口，退出时恢复），
    ///       注册全局热键以便从任意位置一键切换；热键支持用户自行注册（自定义优先，默认回退）。
    /// </summary>
    public static class CompanionManager
    {
        private static CompanionWindow? _window;
        private static readonly List<Window> _hiddenWindows = new();
        private static bool _inMode;

        /// <summary>当前是否处于小窗模式</summary>
        public static bool IsInCompanionMode => _inMode;

        /// <summary>全局热键是否注册成功</summary>
        public static bool HotkeyRegistered { get; private set; }

        /// <summary>模式切换时触发</summary>
        public static event Action? ModeChanged;

        // ===== 全局热键 =====
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9001;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static IntPtr _hwnd = IntPtr.Zero;
        private static HwndSource? _hwndSource;

        // 当前实际注册成功的组合（用于取消录制时回退）
        public static uint CurrentModifiers { get; private set; }
        public static uint CurrentVk { get; private set; }
        private static uint _prevMod, _prevVk;

        // 默认回退候选（依次尝试，注册第一个未被占用的）
        private static readonly (uint mod, uint vk, string label)[] _defaultCandidates =
        {
            (MOD_CONTROL | MOD_SHIFT, 0xC0, "Ctrl+Shift+`"),  // 反引号，极少被占用
            (MOD_CONTROL | MOD_ALT,   0x50, "Ctrl+Alt+P"),
            (MOD_CONTROL | MOD_SHIFT, 0x50, "Ctrl+Shift+P"),
            (MOD_CONTROL | MOD_ALT,   0x43, "Ctrl+Alt+C"),    // 原默认，可能被占用
        };

        /// <summary>当前实际注册成功的热键描述（用于界面提示）</summary>
        public static string HotkeyLabel { get; private set; } = "";

        /// <summary>候选列表：用户自定义优先，其次默认回退</summary>
        private static IEnumerable<(uint mod, uint vk, string label)> BuildCandidates()
        {
            if (SettingsManager.Current.HasCustomCompanionHotkey)
            {
                uint mod = (uint)SettingsManager.Current.CompanionHotkeyModifiers;
                uint vk = (uint)SettingsManager.Current.CompanionHotkeyVk;
                yield return (mod, vk, GlobalHotkeyCapture.FormatHotkey(mod, vk));
            }
            foreach (var c in _defaultCandidates)
                yield return c;
        }

        /// <summary>在主窗口句柄上注册全局热键（由 App 在启动后调用）</summary>
        public static void RegisterGlobalHotkey(IntPtr mainWindowHandle)
        {
            if (_hwnd != IntPtr.Zero) return;
            _hwnd = mainWindowHandle;
            _hwndSource = HwndSource.FromHwnd(mainWindowHandle);
            _hwndSource?.AddHook(WndProc);

            bool ok = false;
            foreach (var (mod, vk, label) in BuildCandidates())
            {
                try
                {
                    if (RegisterHotKey(mainWindowHandle, HOTKEY_ID, mod | MOD_NOREPEAT, vk))
                    {
                        ok = true;
                        CurrentModifiers = mod;
                        CurrentVk = vk;
                        HotkeyLabel = label;
                        break;
                    }
                }
                catch { }
            }
            HotkeyRegistered = ok;
        }

        /// <summary>注册指定组合（用于用户自定义录制后生效）</summary>
        public static bool RegisterSpecific(IntPtr hwnd, uint mod, uint vk, string label)
        {
            bool ok = false;
            try { ok = RegisterHotKey(hwnd, HOTKEY_ID, mod | MOD_NOREPEAT, vk); } catch { }
            if (ok)
            {
                CurrentModifiers = mod;
                CurrentVk = vk;
                HotkeyLabel = label;
            }
            HotkeyRegistered = ok;
            return ok;
        }

        /// <summary>仅注销热键（保留窗口钩子与句柄，供录制时临时释放）</summary>
        private static void UnregisterHotKeyOnly()
        {
            if (_hwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hwnd, HOTKEY_ID); } catch { }
            }
        }

        /// <summary>卸载热键（应用退出时调用）</summary>
        public static void UnregisterGlobalHotkey()
        {
            if (_hwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hwnd, HOTKEY_ID); } catch { }
            }
            _hwndSource?.RemoveHook(WndProc);
            _hwnd = IntPtr.Zero;
            _hwndSource = null;
        }

        /// <summary>开始让用户录制新的热键组合（需含 Ctrl/Alt/Shift 之一）</summary>
        /// <param name="onResult">回调：成功返回可读标签，失败/占用返回空字符串</param>
        public static void BeginHotkeyCapture(Action<string>? onResult)
        {
            if (_hwnd == IntPtr.Zero) return;
            _prevMod = CurrentModifiers;
            _prevVk = CurrentVk;
            UnregisterHotKeyOnly();
            GlobalHotkeyCapture.Start((mod, vk, label) =>
            {
                SettingsManager.Current.CompanionHotkeyModifiers = (int)mod;
                SettingsManager.Current.CompanionHotkeyVk = (int)vk;
                SettingsManager.Save();
                bool ok = RegisterSpecific(_hwnd, mod, vk, label);
                if (!ok)
                {
                    // 被占用：回退到原热键并通知失败
                    RegisterSpecific(_hwnd, _prevMod, _prevVk,
                        GlobalHotkeyCapture.FormatHotkey(_prevMod, _prevVk));
                    onResult?.Invoke("");
                }
                else
                {
                    onResult?.Invoke(label);
                }
            });
        }

        /// <summary>取消热键录制，恢复原热键</summary>
        public static void CancelHotkeyCapture()
        {
            GlobalHotkeyCapture.Stop();
            if (_hwnd != IntPtr.Zero)
            {
                RegisterSpecific(_hwnd, _prevMod, _prevVk,
                    GlobalHotkeyCapture.FormatHotkey(_prevMod, _prevVk));
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(Toggle));
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>切换：在小窗模式与正常模式之间</summary>
        public static void Toggle()
        {
            if (_inMode) Exit(); else Enter();
        }

        /// <summary>进入小窗模式：隐藏同程序其他所有窗口，弹出陪伴窗口</summary>
        public static void Enter()
        {
            if (_inMode || _window != null) return;

            _hiddenWindows.Clear();
            foreach (var w in Application.Current.Windows.Cast<Window>().ToList())
            {
                if (ReferenceEquals(w, _window)) continue;
                if (!w.IsVisible) continue;
                _hiddenWindows.Add(w);
                try { w.Hide(); } catch { }
            }

            _window = new CompanionWindow();
            _window.Closed += OnCompanionClosed;
            _inMode = true;
            _window.Show();
            ModeChanged?.Invoke();
        }

        /// <summary>退出小窗模式：关闭陪伴窗口并恢复此前隐藏的窗口</summary>
        public static void Exit()
        {
            if (!_inMode) return;
            var w = _window;
            _window = null;
            _inMode = false;            // 提前置位，使 OnCompanionClosed 不再重复恢复
            RestoreWindows();
            ModeChanged?.Invoke();
            try { w?.Close(); } catch { }
        }

        private static void OnCompanionClosed(object? sender, EventArgs e)
        {
            _window = null;
            if (!_inMode) return;
            _inMode = false;
            RestoreWindows();
            ModeChanged?.Invoke();
        }

        private static void RestoreWindows()
        {
            foreach (var w in _hiddenWindows)
            {
                try { w.Show(); w.Activate(); } catch { }
            }
            _hiddenWindows.Clear();
        }
    }
}
