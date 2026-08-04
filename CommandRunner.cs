// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WINHELP
{
    /// <summary>
    /// 修复动作的风险等级：决定确认框的图标、默认按钮与卡片徽标颜色。
    /// </summary>
    public enum RiskLevel
    {
        /// <summary>只读诊断或无副作用的操作。</summary>
        Safe,
        /// <summary>有副作用但可恢复（如重启服务、清缓存）。</summary>
        Caution,
        /// <summary>有明显破坏性（如清空防火墙规则、清空打印队列）。</summary>
        Danger
    }

    /// <summary>
    /// 命令执行结果。<see cref="ExitCode"/> 负值为本框架自定义的失败语义。
    /// </summary>
    public sealed class CommandResult
    {
        /// <summary>进程退出码；-1 启动失败 / -2 超时 / -3 用户取消或拒绝 UAC / -4 未通过白名单。</summary>
        public int ExitCode { get; init; }
        /// <summary>是否因超时被终止。</summary>
        public bool TimedOut { get; init; }
        /// <summary>是否被用户主动取消（含拒绝 UAC 授权）。</summary>
        public bool Canceled { get; init; }
        /// <summary>本次执行是否走了管理员提权路径。</summary>
        public bool Elevated { get; init; }
        /// <summary>合并后的标准输出与错误输出。</summary>
        public string Output { get; init; } = "";
        /// <summary>框架层面的错误说明（已本地化），成功时为 null。</summary>
        public string? Error { get; init; }
        /// <summary>命令是否被视为执行成功。</summary>
        public bool Success => ExitCode == 0;
    }

    /// <summary>
    /// 统一的系统命令执行器：白名单校验 + UTF-8 防乱码 + 超时终止 + 流式回显 + 按需 UAC 提权。
    /// <para>
    /// 设计要点：<c>Verb="runas"</c> 提权要求 <c>UseShellExecute=true</c>，而它与
    /// <c>RedirectStandardOutput</c> 互斥，无法直接拿到子进程输出。因此这里分两条路径：
    /// </para>
    /// <list type="bullet">
    /// <item>无需管理员（或本进程已提权）：管道重定向，逐行真流式回显。</item>
    /// <item>需要管理员且未提权：以 runas 启动并让子进程把输出重定向到临时日志，
    /// 主进程按 300ms 轮询 tail 该日志，实现准流式回显，同时仍能拿到真实退出码。</item>
    /// </list>
    /// <para>
    /// 不采用「给整个程序加 requireAdministrator 清单」的做法——那会让每次启动都弹 UAC，
    /// 破坏普通浏览场景的体验；也不采用「以管理员身份重启自身」——会丢失当前页面状态。
    /// </para>
    /// </summary>
    public static class CommandRunner
    {
        /// <summary>允许执行的命令白名单（精确全串匹配，杜绝参数拼接注入）。</summary>
        private static readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>当前进程是否以管理员身份运行（启动时判定一次并缓存）。</summary>
        public static bool IsElevated { get; }

        static CommandRunner()
        {
            bool elevated = false;
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                elevated = new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { /* 判定失败按非管理员处理，走提权路径更安全 */ }
            IsElevated = elevated;
        }

        /// <summary>把一批命令登记进白名单（幂等，可重复调用）。</summary>
        public static void RegisterAllowed(IEnumerable<string> commands)
        {
            if (commands == null) return;
            lock (_lock)
            {
                foreach (var c in commands)
                    if (!string.IsNullOrWhiteSpace(c)) _allowed.Add(c.Trim());
            }
        }

        /// <summary>判断命令是否在白名单内。</summary>
        public static bool IsAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            lock (_lock) return _allowed.Contains(command.Trim());
        }

        /// <summary>
        /// 执行一条白名单命令。
        /// </summary>
        /// <param name="command">命令原文，必须已通过 <see cref="RegisterAllowed"/> 登记。</param>
        /// <param name="requireAdmin">是否需要管理员权限；未提权时会弹出 UAC。</param>
        /// <param name="onLine">逐行回显回调。用 <c>new Progress&lt;string&gt;(...)</c> 构造即可自动回到 UI 线程。</param>
        /// <param name="timeoutSec">超时秒数，超时后终止整个进程树。</param>
        /// <param name="ct">外部取消令牌（页面卸载 / 用户点取消）。</param>
        public static async Task<CommandResult> RunAsync(
            string command,
            bool requireAdmin = false,
            IProgress<string>? onLine = null,
            int timeoutSec = 120,
            CancellationToken ct = default)
        {
            if (!IsAllowed(command))
            {
                return new CommandResult
                {
                    ExitCode = -4,
                    Error = UiLanguage.L("该命令不在安全白名单内，已拒绝执行。",
                                         "This command is not whitelisted and was rejected.")
                };
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutSec > 0) cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            bool elevate = requireAdmin && !IsElevated;
            try
            {
                return elevate
                    ? await RunElevatedAsync(command, onLine, cts.Token, ct).ConfigureAwait(false)
                    : await RunRedirectedAsync(command, onLine, cts.Token, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    Elevated = elevate,
                    Error = UiLanguage.L("命令启动失败：", "Failed to start command: ") + ex.Message
                };
            }
        }

        // ── 路径 A：管道重定向，真流式 ────────────────────────────────────────────

        private static async Task<CommandResult> RunRedirectedAsync(
            string cmd, IProgress<string>? onLine, CancellationToken token, CancellationToken userCt)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c chcp 65001 >nul && " + cmd)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    Error = UiLanguage.L("无法创建命令进程。", "Could not create the command process.")
                };
            }

            var sb = new StringBuilder();

            async Task Pump(StreamReader reader)
            {
                while (true)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
                    catch { break; }
                    if (line == null) break;

                    var s = Clean(line);
                    lock (sb) sb.AppendLine(s);
                    onLine?.Report(s);
                }
            }

            var pumps = Task.WhenAll(Pump(p.StandardOutput), Pump(p.StandardError));

            try
            {
                await p.WaitForExitAsync(token).ConfigureAwait(false);
                await pumps.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillTree(p);
                bool userCancel = userCt.IsCancellationRequested;
                return new CommandResult
                {
                    ExitCode = userCancel ? -3 : -2,
                    TimedOut = !userCancel,
                    Canceled = userCancel,
                    Output = Snapshot(sb),
                    Error = userCancel
                        ? UiLanguage.L("已取消执行。", "Execution canceled.")
                        : UiLanguage.L("命令执行超时，已强制终止。", "Command timed out and was terminated.")
                };
            }

            return new CommandResult { ExitCode = p.ExitCode, Output = Snapshot(sb) };
        }

        // ── 路径 B：runas 提权 + 临时日志 tail，准流式 ────────────────────────────

        private static async Task<CommandResult> RunElevatedAsync(
            string cmd, IProgress<string>? onLine, CancellationToken token, CancellationToken userCt)
        {
            string log = Path.Combine(Path.GetTempPath(), "sinan_fix_" + Guid.NewGuid().ToString("N") + ".log");

            // 用括号包住原命令，保证含 && 的复合命令整体被重定向到日志
            var psi = new ProcessStartInfo("cmd.exe",
                "/c chcp 65001 >nul && (" + cmd + ") > \"" + log + "\" 2>&1")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            Process? p;
            try
            {
                p = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED：用户点了「否」
            {
                TryDelete(log);
                return new CommandResult
                {
                    ExitCode = -3,
                    Canceled = true,
                    Elevated = true,
                    Error = UiLanguage.L("你取消了管理员授权，修复未执行。",
                                         "Admin elevation was canceled, the fix did not run.")
                };
            }

            if (p == null)
            {
                TryDelete(log);
                return new CommandResult
                {
                    ExitCode = -1,
                    Elevated = true,
                    Error = UiLanguage.L("无法以管理员身份启动命令。",
                                         "Could not start the command as administrator.")
                };
            }

            var sb = new StringBuilder();
            var carry = new StringBuilder();
            long pos = 0;

            using (p)
            {
                try
                {
                    while (!p.HasExited)
                    {
                        pos = Tail(log, pos, carry, sb, onLine);
                        await Task.Delay(300, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    KillTree(p);
                    Tail(log, pos, carry, sb, onLine);
                    TryDelete(log);
                    bool userCancel = userCt.IsCancellationRequested;
                    return new CommandResult
                    {
                        ExitCode = userCancel ? -3 : -2,
                        TimedOut = !userCancel,
                        Canceled = userCancel,
                        Elevated = true,
                        Output = Snapshot(sb),
                        Error = userCancel
                            ? UiLanguage.L("已取消执行。", "Execution canceled.")
                            : UiLanguage.L("命令执行超时，已强制终止。", "Command timed out and was terminated.")
                    };
                }

                // 收尾：进程已退出，把日志剩余内容读干净
                pos = Tail(log, pos, carry, sb, onLine);
                if (carry.Length > 0)
                {
                    var rest = Clean(carry.ToString());
                    if (rest.Length > 0) { sb.AppendLine(rest); onLine?.Report(rest); }
                    carry.Clear();
                }

                int code = 0;
                try { code = p.ExitCode; } catch { }
                TryDelete(log);
                return new CommandResult { ExitCode = code, Elevated = true, Output = Snapshot(sb) };
            }
        }

        /// <summary>
        /// 从 <paramref name="pos"/> 起增量读取日志，按行推送；不完整的半行留在 <paramref name="carry"/> 等下一轮。
        /// 返回新的读取位置。
        /// </summary>
        private static long Tail(string path, long pos, StringBuilder carry, StringBuilder sink, IProgress<string>? onLine)
        {
            try
            {
                if (!File.Exists(path)) return pos;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length <= pos) return pos;

                fs.Seek(pos, SeekOrigin.Begin);
                var buf = new byte[fs.Length - pos];
                int read = fs.Read(buf, 0, buf.Length);
                if (read <= 0) return pos;

                carry.Append(Encoding.UTF8.GetString(buf, 0, read));

                string all = carry.ToString();
                int last = all.LastIndexOf('\n');
                if (last >= 0)
                {
                    string complete = all.Substring(0, last);
                    carry.Clear();
                    carry.Append(all.Substring(last + 1));

                    foreach (var raw in complete.Split('\n'))
                    {
                        var s = Clean(raw);
                        lock (sink) sink.AppendLine(s);
                        onLine?.Report(s);
                    }
                }
                return pos + read;
            }
            catch
            {
                return pos; // 日志被独占或尚未创建，下一轮再试
            }
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 清理命令输出：丢弃 UTF-8 解码失败的替换字符与控制字符（保留 Tab）。
        /// <para>注意：sfc /scannow 的重定向输出实为 UTF-16LE，按 UTF-8 解码后每个字符间会夹 \0，
        /// 不剥离的话界面上会显示成「s.f.c.」。\0 属控制字符，已被此处过滤覆盖。</para>
        /// </summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\uFFFD') continue;
                if (c == '\r' || c == '\n') continue;                 // 行已切分，去掉残留换行符
                if (char.IsControl(c) && c != '\t') continue;          // 含 \0
                sb.Append(c);
            }
            return sb.ToString().TrimEnd();
        }

        private static string Snapshot(StringBuilder sb)
        {
            lock (sb) return sb.ToString();
        }

        private static void KillTree(Process p)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
