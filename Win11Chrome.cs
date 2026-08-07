// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WINHELP
{
    /// <summary>
    /// Windows 11 窗口适配（v5.6.0）：
    /// 通过 DWM 为自绘标题栏（WindowStyle=None + WindowChrome）窗口启用圆角，
    /// 对齐 Win11 设计语言。仅 Win11（build ≥ 22000）生效；Win10 及以下静默跳过。
    /// 注意：AllowsTransparency=True 的窗口无法被 DWM 圆角，本项目均为不透明窗口，无冲突。
    /// </summary>
    public static class Win11Chrome
    {
        // DWMWA_WINDOW_CORNER_PREFERENCE = 33（Win11 22000+）
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;

        /// <summary>PreserveSig=false：调用失败（如 Win10）直接抛异常，便于统一 catch。</summary>
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute,
            ref int pvAttribute, uint cbAttribute);

        private static readonly bool IsWin11 = Environment.OSVersion.Version.Build >= 22000;

        /// <summary>
        /// 应用/恢复窗口圆角。round=false 用于最大化时恢复方角（避免圆角贴边漏缝）。
        /// 幂等；窗口句柄未创建时自动 EnsureHandle。
        /// </summary>
        public static void Apply(Window window, bool round)
        {
            if (window == null || !IsWin11) return;
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                int pref = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, (uint)sizeof(int));
            }
            catch { /* Win10 / DWM 不可用时静默跳过 */ }
        }
    }
}
