using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SmokeTest;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    static List<(IntPtr h, bool vis, string title)> Titles(uint pid)
    {
        var list = new List<(IntPtr, bool, string)>();
        EnumWindows((h, lp) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid)
            {
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                var t = sb.ToString();
                if (!string.IsNullOrEmpty(t)) list.Add((h, IsWindowVisible(h), t));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static bool HasVisible(uint pid, string contains)
        => Titles(pid).Any(x => x.vis && x.title.Contains(contains));
    static bool HasHidden(uint pid, string contains)
        => Titles(pid).Any(x => !x.vis && x.title.Contains(contains));

    static bool WaitFor(Func<bool> pred, int timeoutMs, int intervalMs = 250)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (pred()) return true;
            Thread.Sleep(intervalMs);
        }
        return pred();
    }

    static void Print(string stage, uint pid)
    {
        Console.WriteLine($"=== {stage} ===");
        foreach (var (h, vis, t) in Titles(pid))
            Console.WriteLine($"{(vis ? "[VIS] " : "[hid] ")}{t}");
    }

    static int Main()
    {
        string exe = @"F:\new\WINHELP\bin\Release\net10.0-windows\win-x64\publish\司南工具箱.exe";
        var marker = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winhelp_hotkey_debug.txt");
        try { if (System.IO.File.Exists(marker)) System.IO.File.Delete(marker); } catch { }

        Console.WriteLine("Starting app (auto enter-exit)...");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.EnvironmentVariables["WINHELP_HOTKEY_DEBUG"] = "1";
        psi.EnvironmentVariables["WINHELP_COMPANION_AUTO"] = "enter-exit";
        var p = Process.Start(psi)!;
        uint pid = (uint)p.Id;

        // 轮询等待主窗口可见（自包含单文件 exe 冷启动较慢，最多等待 60s）
        bool mainVis0 = WaitFor(() => HasVisible(pid, "司南工具箱"), 60000);
        Print("after main visible (baseline)", pid);

        // 等待陪伴窗口出现且主窗口隐藏
        bool companionVis = false;
        bool mainHidden = false;
        if (mainVis0)
        {
            companionVis = WaitFor(() => HasVisible(pid, "陪伴运行"), 12000);
            mainHidden = WaitFor(() => HasHidden(pid, "司南工具箱"), 12000);
        }
        Print("after waiting companion enter", pid);

        // 等待主窗口再次可见（退出陪伴模式）
        bool mainVisBack = WaitFor(() => HasVisible(pid, "司南工具箱"), 12000);
        Print("after waiting companion exit", pid);

        string hk = "unread";
        try { hk = System.IO.File.Exists(marker) ? System.IO.File.ReadAllText(marker) : "no-marker"; } catch { }

        Console.WriteLine($"RESULT baseline_main_visible={mainVis0} companion_visible={companionVis} main_hidden={mainHidden} main_visible_after_exit={mainVisBack} hotkey_registered={hk}");

        try { p.Kill(); } catch { }
        foreach (var x in Process.GetProcessesByName("司南工具箱")) try { x.Kill(); } catch { }

        bool pass = mainVis0 && companionVis && mainHidden && mainVisBack;
        Console.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }
}
