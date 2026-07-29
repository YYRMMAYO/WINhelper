using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WINHELP
{
    /// <summary>
    /// 低层键盘钩子（WH_KEYBOARD_LL）捕获下一次按键组合，用于让用户"录制"自定义热键。
    /// 捕获要求至少含一个修饰键（Ctrl/Alt/Shift），避免误占用普通字符键。
    /// 捕获完成后自动卸载钩子并通过回调返回 修饰键位掩码 + 虚拟键码 + 可读标签。
    /// </summary>
    internal static class GlobalHotkeyCapture
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOD_* 与 RegisterHotKey 保持一致
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private static LowLevelKeyboardProc? _proc;
        private static IntPtr _hookId = IntPtr.Zero;
        private static Action<uint, uint, string>? _callback;

        /// <summary>是否正在捕获</summary>
        public static bool IsCapturing => _hookId != IntPtr.Zero;

        /// <summary>开始捕获下一次热键组合</summary>
        public static void Start(Action<uint, uint, string> onCaptured)
        {
            if (IsCapturing) return;
            _callback = onCaptured;
            _proc = HookCallback;
            // 低层键盘钩子运行在调用线程上下文中，使用当前模块句柄（GetModuleHandle(null) 返回宿主 exe 句柄）即可
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        /// <summary>取消捕获（恢复由调用方负责重新注册原热键）</summary>
        public static void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _callback = null;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                uint vk = kb.vkCode;

                // 纯修饰键不作为主键，等待真正的键
                bool isModifier = vk is 0x11 or 0x12 or 0x10 or 0x5B or 0x5C;
                if (!isModifier)
                {
                    var mods = Control.ModifierKeys;
                    uint mod = 0;
                    if ((mods & Keys.Control) != 0) mod |= MOD_CONTROL;
                    if ((mods & Keys.Alt) != 0) mod |= MOD_ALT;
                    if ((mods & Keys.Shift) != 0) mod |= MOD_SHIFT;

                    // 必须至少包含一个修饰键，避免占用普通字符输入
                    if (mod != 0)
                    {
                        string label = FormatHotkey(mod, vk);
                        Stop();
                        _callback?.Invoke(mod, vk, label);
                        return IntPtr.Zero;
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>把 MOD 掩码 + 虚拟键码格式化为可读标签（如 Ctrl+Shift+`）</summary>
        public static string FormatHotkey(uint mod, uint vk)
        {
            var parts = new List<string>();
            if ((mod & MOD_ALT) != 0) parts.Add("Alt");
            if ((mod & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mod & MOD_SHIFT) != 0) parts.Add("Shift");

            string key = vk switch
            {
                0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3", 0x34 => "4",
                0x35 => "5", 0x36 => "6", 0x37 => "7", 0x38 => "8", 0x39 => "9",
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),
                0xC0 => "`",
                0xBB => "=",
                0xBD => "-",
                0xDC => "\\",
                0xDB => "[",
                0xDD => "]",
                0xBA => ";",
                0xDE => "'",
                0xBC => ",",
                0xBE => ".",
                0xBF => "/",
                _ => ((Keys)vk).ToString()
            };
            parts.Add(key);
            return string.Join("+", parts);
        }
    }
}
