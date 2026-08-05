// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace WINHELP
{
    /// <summary>
    /// 沙盒化的 AI 代操作系统 — 工具注册表。
    /// 设计原则：
    /// 1) 白名单：AI 只能调用这里预先定义、且参数被严格限定（枚举/固定命令）的工具，拿不到裸 shell。
    /// 2) 人工确认：每次调用都会弹出确认框，只有用户点「允许」才真正执行（Human-in-the-loop）。
    /// 3) 只读优先：诊断命令均为只读，绝不提供删除/格式化/卸载等高危动作。
    /// 4) 隐私保护：截图发送前征得用户同意，且会缩放以降低外传数据量。
    /// </summary>
    public static class ToolRegistry
    {
        // 允许打开的程序（中文名 → 启动目标）。均为系统内置/常见程序，无危险可执行文件。
        private static readonly Dictionary<string, string> AppTargets = new()
        {
            ["记事本"] = "notepad",
            ["计算器"] = "calc",
            ["画图"] = "mspaint",
            ["文件资源管理器"] = "explorer",
            ["设置"] = "ms-settings:",
            ["控制面板"] = "control",
            ["浏览器 Edge"] = "microsoft-edge:",
            ["命令提示符"] = "cmd",
            ["任务管理器"] = "taskmgr",
            ["截图工具"] = "snippingtool",
            ["服务"] = "services.msc",
            ["此电脑"] = "explorer shell:MyComputerFolder"
        };

        // 允许打开的系统设置页（中文名 → ms-settings URI）
        private static readonly Dictionary<string, string> SettingsTargets = new()
        {
            ["显示"] = "ms-settings:display",
            ["网络"] = "ms-settings:network",
            ["系统"] = "ms-settings:",
            ["应用"] = "ms-settings:appsfeatures",
            ["Windows 更新"] = "ms-settings:windowsupdate",
            ["蓝牙和其他设备"] = "ms-settings:bluetooth",
            ["电源和睡眠"] = "ms-settings:powersleep",
            ["关于"] = "ms-settings:about"
        };

        // 允许运行的只读诊断命令（固定字符串，杜绝参数注入）
        private static readonly HashSet<string> AllowedCommands = new()
        {
            "ipconfig /all",
            "systeminfo",
            "netstat -an",
            "ping -n 4 127.0.0.1",
            "getmac /v",
            "tasklist",
            "ver",
            "powercfg /list"
        };

        private static readonly string[] OpenApps = AppTargets.Keys.ToArray();
        private static readonly string[] OpenSettings = SettingsTargets.Keys.ToArray();
        private static readonly string[] Diagnostics = AllowedCommands.ToArray();

        // 允许打开的常用文件夹（Shell 特殊文件夹，绝不以字符串拼接命令）
        private static readonly Dictionary<string, string> FolderTargets = new()
        {
            ["文档"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["下载"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            ["桌面"] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            ["图片"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["音乐"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["视频"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        private static readonly string[] OpenFolders = FolderTargets.Keys.ToArray();

        /// <summary>工具执行结果</summary>
        public class ToolResult
        {
            public string Description { get; set; } = "";
            public string Text { get; set; } = "";
            public string? ImageBase64 { get; set; }
        }

        /// <summary>
        /// OpenAI 兼容的 tools 描述（function calling 模式）。
        /// 模型据此决定调用哪个工具；所有参数均通过枚举/固定集合限定。
        /// </summary>
        public static List<object> Tools => new()
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "open_app",
                    description = "打开一个常用 Windows 程序。只能打开下列白名单中的应用，不能运行任意命令。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            app = new
                            {
                                type = "string",
                                @enum = OpenApps,
                                description = "要打开的程序名称，必须是下列之一：" + string.Join("、", OpenApps)
                            }
                        },
                        required = new[] { "app" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "open_settings",
                    description = "打开 Windows 系统设置中的某个页面（如网络、显示、Windows 更新等）。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            page = new
                            {
                                type = "string",
                                @enum = OpenSettings,
                                description = "要打开的设置页面，必须是下列之一：" + string.Join("、", OpenSettings)
                            }
                        },
                        required = new[] { "page" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "run_diagnostic",
                    description = "运行一个只读的系统诊断命令，用于排查问题（不会修改系统）。只能运行下列固定命令。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            command = new
                            {
                                type = "string",
                                @enum = Diagnostics,
                                description = "要运行的诊断命令，必须是下列之一：" + string.Join("、", Diagnostics)
                            }
                        },
                        required = new[] { "command" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "take_screenshot",
                    description = "截取当前屏幕画面，便于你（AI）分析用户当前看到的界面。注意：截图可能包含隐私信息，会发送给 AI 服务。",
                    parameters = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[0]
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "kill_process",
                    description = "结束一个正在运行的、卡死的进程（需要用户确认）。只能按进程名结束，不能运行任意命令。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            process_name = new
                            {
                                type = "string",
                                description = "要结束的进程名（不含 .exe，如 notepad、chrome），必须是正在运行的进程"
                            }
                        },
                        required = new[] { "process_name" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "open_folder",
                    description = "打开一个常用系统文件夹（如文档、下载、桌面、图片、音乐、视频）。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            folder = new
                            {
                                type = "string",
                                @enum = OpenFolders,
                                description = "要打开的文件夹，必须是下列之一：" + string.Join("、", OpenFolders)
                            }
                        },
                        required = new[] { "folder" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "toggle_setting",
                    description = "切换一个受控的本软件设置（如开机自动启动）。只能切换预定义的少数安全设置。",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            setting = new
                            {
                                type = "string",
                                @enum = new[] { "autostart" },
                                description = "要切换的设置，目前仅支持 autostart（开机自动启动）"
                            }
                        },
                        required = new[] { "setting" }
                    }
                }
            }
        };

        /// <summary>
        /// 执行一个工具调用。会先向用户弹出确认框（沙盒关键防线），用户允许后才执行。
        /// 在后台线程调用，内部通过 owner.Dispatcher 在 UI 线程弹出确认对话框。
        /// </summary>
        public static async Task<ToolResult> ExecuteToolAsync(string name, JsonElement args, Window owner)
        {
            var desc = Describe(name, args);

            // —— 沙盒防线：人工确认 ——
            bool allow = false;
            await owner.Dispatcher.InvokeAsync(() =>
            {
                var r = MessageBox.Show(owner,
                    $"AI 助手希望执行以下操作：\n\n{desc}\n\n是否允许？\n（随时可拒绝；拒绝后 AI 会改用其他方式）",
                    "AI 代操作确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                allow = (r == MessageBoxResult.Yes);
            });

            if (!allow)
                return new ToolResult { Description = desc, Text = "用户拒绝了该操作。" };

            try
            {
                return                 name switch
                {
                    "open_app" => RunOpenApp(args),
                    "open_settings" => RunOpenSettings(args),
                    "run_diagnostic" => await RunDiagnosticAsync(args),
                    "take_screenshot" => RunScreenshot(false),
                    "kill_process" => RunKillProcess(args),
                    "open_folder" => RunOpenFolder(args),
                    "toggle_setting" => RunToggleSetting(args),
                    _ => new ToolResult { Description = desc, Text = "未知工具：" + (name ?? "") }
                };
            }
            catch (Exception ex)
            {
                return new ToolResult { Description = desc, Text = "执行出错：" + ex.Message };
            }
        }

        // ===== 各工具实现 =====

        private static ToolResult RunOpenApp(JsonElement args)
        {
            var app = Arg(args, "app");
            if (string.IsNullOrEmpty(app) || !AppTargets.TryGetValue(app, out var target))
                return new ToolResult { Description = "打开程序", Text = $"不支持的程序：「{app}」。" };
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return new ToolResult { Description = $"打开程序「{app}」", Text = $"已尝试打开「{app}」。" };
        }

        private static ToolResult RunOpenSettings(JsonElement args)
        {
            var page = Arg(args, "page");
            if (string.IsNullOrEmpty(page) || !SettingsTargets.TryGetValue(page, out var uri))
                return new ToolResult { Description = "打开设置", Text = $"不支持的设置页：「{page}」。" };
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            return new ToolResult { Description = $"打开设置「{page}」", Text = $"已打开系统设置「{page}」页面。" };
        }

        private static async Task<ToolResult> RunDiagnosticAsync(JsonElement args)
        {
            var cmd = Arg(args, "command");
            if (string.IsNullOrEmpty(cmd) || !AllowedCommands.Contains(cmd))
                return new ToolResult { Description = "运行诊断", Text = $"不允许的命令：「{cmd}」。" };

            var psi = new ProcessStartInfo("cmd.exe", "/c chcp 65001 >nul && " + cmd)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // 以 UTF-8 读取控制台输出：先 chcp 65001 让命令以 UTF-8 输出，规避 zh-CN 下 GBK/CP936 被错误解码导致的乱码
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            // v5.4.0：改为异步等待（不阻塞 UI 线程，修复 AI 代操作卡死 12s）
            var waitTask = p.WaitForExitAsync();
            var done = await Task.WhenAny(waitTask, Task.Delay(12000));
            if (done != waitTask)
            {
                try { p.Kill(); } catch { }
            }
            var stdout = await outTask;
            var stderr = await errTask;

            var output = CleanCli(stdout + stderr);
            if (output.Length > 2000) output = output[..2000] + "\n…（输出已截断）";
            return new ToolResult { Description = $"运行诊断：{cmd}", Text = string.IsNullOrEmpty(output) ? "（无输出）" : output };
        }

        /// <summary>清理诊断命令输出：丢弃已损坏的替换字符与控制字符（保留换行/制表），避免残留乱码。</summary>
        private static string CleanCli(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\uFFFD') continue;                       // 已损坏的替换字符
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static ToolResult RunScreenshot(bool allScreens = false)
        {
            System.Drawing.Rectangle bounds;
            if (allScreens && System.Windows.Forms.Screen.AllScreens.Length > 1)
            {
                // 多显示器：拼接所有屏幕为一张大图
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                {
                    minX = Math.Min(minX, s.Bounds.X);
                    minY = Math.Min(minY, s.Bounds.Y);
                    maxX = Math.Max(maxX, s.Bounds.Right);
                    maxY = Math.Max(maxY, s.Bounds.Bottom);
                }
                bounds = System.Drawing.Rectangle.FromLTRB(minX, minY, maxX, maxY);
            }
            else
            {
                bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
            }

            // 缩放以降低外传数据量（最大 1280x720）
            float scale = Math.Min(1f, Math.Min(1280f / bounds.Width, 720f / bounds.Height));
            int w = (int)(bounds.Width * scale);
            int h = (int)(bounds.Height * scale);

            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            var b64 = Convert.ToBase64String(ms.ToArray());

            return new ToolResult
            {
                Description = "截取当前屏幕画面",
                Text = "已截取当前屏幕并保存，可用于 AI 分析（截图可能包含隐私信息）。",
                ImageBase64 = b64
            };
        }

        // ===== 受控写操作（白名单 + 人工确认，绝不暴露裸 shell） =====

        /// <summary>受保护的关键系统进程名（禁止结束，避免系统崩溃/安全软件失效）。</summary>
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows 核心
            "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "smss",
            "dwm", "explorer", "taskhost", "taskhostw", "fontdrvhost", "spoolsv",
            "system", "registry", "memory compression", "SearchIndexer", "SearchHost",
            // 安全软件（结束杀毒 = 解除系统防护）
            "MsMpEng", "MsSense", "SecurityHealthService", "NisSrv",
            "avp", "avgnt", "avguard", "ekrn", "bdagent", "ccSvcHst",
            "360tray", "360Safe", "QQPCTray", "kxescore", "kxetray",
            "Defender", "Mcshield", "NortonSecurity", "Kaspersky"
        };

        private static ToolResult RunKillProcess(JsonElement args)
        {
            var name = Arg(args, "process_name").Trim();
            if (string.IsNullOrEmpty(name))
                return new ToolResult { Description = "结束进程", Text = "进程名为空。" };

            // v5.4.0 安全加固：进程名只允许 [A-Za-z0-9_.-]（Windows 进程名合法字符集），
            // 杜绝命令分隔符 / 路径 / 引号等注入（安全审计 P2 修复）
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_.-]+$"))
                return new ToolResult { Description = "结束进程", Text = $"进程名「{name}」含有非法字符，已拒绝。" };

            // 自保护：禁止结束本程序
            var self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            if (name.Equals(self, StringComparison.OrdinalIgnoreCase))
                return new ToolResult { Description = "结束进程", Text = "出于安全考虑，不能结束本程序自身。" };

            // 系统关键进程 / 安全软件保护：结束会造成系统崩溃或安全防护失效（安全审计建议 P1）
            if (ProtectedProcesses.Contains(name))
                return new ToolResult
                {
                    Description = $"结束进程「{name}」",
                    Text = $"出于安全考虑，不能结束系统关键进程或安全软件「{name}」。"
                };

            var procs = System.Diagnostics.Process.GetProcessesByName(name);
            if (procs.Length == 0)
                return new ToolResult { Description = $"结束进程「{name}」", Text = $"未找到名为「{name}」的运行中进程。" };

            // 附加防护：逐进程判断 —— 仅跳过系统目录（System32）中的实例，
            // 其余正常结束；读不到模块路径的进程也保守跳过（安全审计建议 P1）。
            var sysDir = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System))
                .TrimEnd('\\') + "\\";
            int killed = 0, skipped = 0;
            foreach (var p in procs)
            {
                using (p)   // v5.4.0：进程对象释放（GetProcessesByName 返回的句柄需 Dispose）
                {
                    bool skip = false;
                    try
                    {
                        var exe = p.MainModule?.FileName;
                        if (string.IsNullOrEmpty(exe))
                        {
                            // 已退出或无权限读取模块路径 → 保守跳过，不 Kill
                            skip = true;
                        }
                        else
                        {
                            // Path.GetFullPath 规范化（防 "C:\Windows\System32..\X" 等变体）+ "\" 分隔符边界（防 System32X 误判）
                            var full = Path.GetFullPath(exe);
                            if (full.StartsWith(sysDir, StringComparison.OrdinalIgnoreCase))
                                skip = true;
                        }
                    }
                    catch { skip = true; } // 读不到模块路径 → 保守跳过该进程

                    if (skip) { skipped++; continue; }
                    try { p.Kill(); killed++; }
                    catch { /* 部分进程无权限，跳过 */ }
                }
            }
            return new ToolResult
            {
                Description = $"结束进程「{name}」",
                Text = $"已结束 {killed} 个「{name}」进程" +
                       (skipped > 0 ? $"，跳过 {skipped} 个系统目录实例" : "") + "。"
            };
        }

        private static ToolResult RunOpenFolder(JsonElement args)
        {
            var folder = Arg(args, "folder");
            if (string.IsNullOrEmpty(folder) || !FolderTargets.TryGetValue(folder, out var path))
                return new ToolResult { Description = "打开文件夹", Text = $"不支持的文件夹：「{folder}」。" };
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return new ToolResult { Description = $"打开文件夹「{folder}」", Text = $"已打开「{folder}」：{path}" };
        }

        private static ToolResult RunToggleSetting(JsonElement args)
        {
            var setting = Arg(args, "setting");
            if (setting != "autostart")
                return new ToolResult { Description = "切换设置", Text = $"不支持的设置：「{setting}」。" };
            bool next = !SettingsManager.Current.AutoStart;
            SettingsManager.Current.AutoStart = next;
            SettingsManager.SetAutoStart(next);
            SettingsManager.Save();
            return new ToolResult
            {
                Description = "切换开机自动启动",
                Text = next ? "已开启开机自动启动。" : "已关闭开机自动启动。"
            };
        }

        // ===== 辅助 =====

        private static string Arg(JsonElement args, string key)
            => args.TryGetProperty(key, out var v) ? (v.GetString() ?? "") : "";

        private static string Describe(string name, JsonElement args)
        {
            return name switch
            {
                "open_app" => $"打开程序「{Arg(args, "app")}」",
                "open_settings" => $"打开系统设置「{Arg(args, "page")}」页面",
                "run_diagnostic" => $"运行只读诊断命令：{Arg(args, "command")}",
                "take_screenshot" => "截取当前屏幕画面（截图可能包含隐私信息，将发送给 AI 服务用于分析）",
                "kill_process" => $"结束进程「{Arg(args, "process_name")}」",
                "open_folder" => $"打开文件夹「{Arg(args, "folder")}」",
                "toggle_setting" => $"切换设置「{Arg(args, "setting")}」",
                _ => "执行操作：" + (name ?? "")
            };
        }
    }
}
