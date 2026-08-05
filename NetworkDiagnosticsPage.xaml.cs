// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
//
// 网络诊断页（导航 key="net"）v5.4.0 重构：
// 1. 全部检测（网卡枚举 / Ping / DNS / HTTP 探测 / WiFi 状态）在后台线程执行，UI 零卡顿；
// 2. 以「默认路由出口接口」为核心判定（不再用 GetIsNetworkAvailable 误导性 API），
//    检查 IPv4 是否有效（拒绝 169.254 APIPA）、默认网关是否存在且可达；
// 3. WiFi 专查（netsh wlan 解析 SSID / 信号 / 连接状态），直观回答「WiFi 到底连没连上」；
// 4. 外网探测双通道：ICMP + HTTP 204（gstatic / 微软 / 腾讯），避免 ICMP 被运营商拦截导致误判；
// 5. 结论分级（未连接 / 本地无网络 / 路由器外网故障 / 网络正常），每条结果配白话解释 + 修复建议；
// 6. 输出全部本地化（中 / 英），术语降级为白话（延迟、网速、网址翻译服务…）。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>网络诊断页（导航 key="net"）。由 MainWindow._factories 懒加载；依赖 ThemeManager 画刷与 UiLanguage 多语言。</summary>
public partial class NetworkDiagnosticsPage : UserControl
{
    public NetworkDiagnosticsPage()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnDiag, Color.FromRgb(0x00, 0x96, 0x88),
            hoverColor: Color.FromRgb(0x00, 0x79, 0x6E));
        ThemeManager.ApplyButtonTheme(BtnSpeed, Color.FromRgb(0x29, 0x80, 0xB9),
            hoverColor: Color.FromRgb(0x21, 0x66, 0x99));
    }

    // ── 结果模型 ────────────────────────────────────────────────────────────

    private sealed class Row
    {
        public string Label = "";       // 白话标签（已本地化）
        public string Value = "";       // 值（已本地化）
        public Brush Color = Brushes.Gray;
    }

    private sealed class NetResult
    {
        public string VerdictZh = "";   // 结论（中文）
        public string VerdictEn = "";   // 结论（英文）
        public bool IsGood = false;     // 是否为正常结论（绿色）
        public bool IsWarn = false;     // 是否警告级（橙色）
        public readonly List<Row> Rows = new();
        public readonly List<string> TipsZh = new();
        public readonly List<string> TipsEn = new();
    }

    // 复用画刷，避免每次检测创建大量 Brush
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));

    private static readonly string[] ProbeHttpUrls =
    {
        "https://connectivitycheck.gstatic.com/generate_204",
        "https://www.msftconnecttest.com/connecttest.txt",
        "https://connect.rom.miui.com/generate_204"
    };

    // v5.4.0：测速取消令牌（长下载可中断）
    private System.Threading.CancellationTokenSource? _speedCts;

    private void BtnSpeedCancel_Click(object sender, RoutedEventArgs e)
    {
        _speedCts?.Cancel();
        BtnSpeedCancel.IsEnabled = false;
        TxtStatus.Text = UiLanguage.L("正在取消测速…", "Canceling speed test…");
    }

    // ── 诊断 ────────────────────────────────────────────────────────────────

    private async void BtnDiag_Click(object sender, RoutedEventArgs e)
    {
        ListPanel.Children.Clear();
        TxtStatus.Text = UiLanguage.L("诊断中…", "Diagnosing…");
        BtnDiag.IsEnabled = false;
        BtnSpeed.IsEnabled = false;
        try
        {
            var result = await Task.Run(DetectNetwork);
            RenderResult(result);
            TxtStatus.Text = UiLanguage.L("诊断完成", "Diagnosis done");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("诊断出错：", "Diagnosis error: ") + ex.Message;
        }
        finally
        {
            BtnDiag.IsEnabled = true;
            BtnSpeed.IsEnabled = true;
        }
    }

    /// <summary>核心检测（后台线程执行，不做任何 UI 操作）。</summary>
    private static NetResult DetectNetwork()
    {
        var r = new NetResult();
        var upIfs = new List<NetAdapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            upIfs.Add(CollectAdapter(ni));
        }

        // 1) 活跃出口：优先选「有有效 IPv4 + 有默认网关」的物理接口（有线/无线），
        //    排除仅 APIPA 或仅隧道地址的虚拟接口（VPN/WSL 之类会被网关检测进一步确认）。
        var active = upIfs
            .Where(a => a.HasValidIpv4 && a.Gateways.Count > 0)
            .OrderByDescending(a => a.IsWifi)
            .FirstOrDefault();

        // 2) 逐个接口展示（只展示有真实连接意义的：有效 IP 或网关）
        foreach (var a in upIfs.Where(a => a.HasValidIpv4 || a.Gateways.Count > 0))
        {
            string typeLabel = a.IsWifi ? UiLanguage.L("无线", "Wireless")
                              : a.IsVirtual ? UiLanguage.L("虚拟网卡", "Virtual")
                              : UiLanguage.L("有线", "Wired");
            string mark = (active != null && a.Name == active.Name)
                ? UiLanguage.L("（当前上网通道）", " (active path)")
                : "";
            r.Rows.Add(new Row
            {
                Label = $"{a.Name} {mark}",
                Value = $"{typeLabel} · {a.IPv4Summary}",
                Color = (active != null && a.Name == active.Name) ? Green : Dark
            });
        }

        // 3) WiFi 状态（用户最关心的「WiFi 到底连没连上」）
        var wifi = QueryWifiState();   // (connected, ssid, signal)
        if (wifi.connected)
        {
            r.Rows.Add(new Row
            {
                Label = UiLanguage.L("WiFi 已连接", "Wi-Fi connected"),
                Value = $"{wifi.ssid} · {UiLanguage.L("信号", "signal")} {wifi.signal}%",
                Color = Green
            });
        }
        else if (wifi.hasAdapter)
        {
            r.Rows.Add(new Row
            {
                Label = UiLanguage.L("WiFi 状态", "Wi-Fi status"),
                Value = UiLanguage.L("未连接任何无线网络", "Not connected to any Wi-Fi"),
                Color = Red
            });
        }

        // 4) 分级判定与深度检测
        if (active == null)
        {
            r.VerdictZh = "未连接到网络";
            r.VerdictEn = "Not connected to any network";
            r.TipsZh.Add("请检查网线是否插好，或点击系统右下角的 Wi-Fi 图标连接网络。");
            r.TipsZh.Add("确认飞行模式已关闭。");
            r.TipsZh.Add("尝试重启路由器或光猫。");
            r.TipsEn.Add("Check the network cable, or click the Wi-Fi icon and connect to a network.");
            r.TipsEn.Add("Make sure Airplane mode is off.");
            r.TipsEn.Add("Try restarting your router or modem.");
            return r;
        }

        r.Rows.Add(new Row
        {
            Label = UiLanguage.L("IP 地址", "IP address"),
            Value = active.IPv4,
            Color = active.HasValidIpv4 ? Dark : Red
        });
        r.Rows.Add(new Row
        {
            Label = UiLanguage.L("默认网关", "Default gateway"),
            Value = active.Gateways.Count > 0 ? active.Gateways[0] : UiLanguage.L("未分配", "none"),
            Color = active.Gateways.Count > 0 ? Dark : Red
        });

        // 4a) 无有效 IP（APIPA）
        if (!active.HasValidIpv4)
        {
            r.VerdictZh = "已连接但无法上网（未获取到有效 IP）";
            r.VerdictEn = "Connected but no valid IP address";
            r.TipsZh.Add("电脑没有从路由器拿到有效 IP（当前是 169.254 自动地址）。");
            r.TipsZh.Add("尝试在「网络设置 → 更改适配器选项 → 右键你的网卡 → 诊断」让系统修复。");
            r.TipsZh.Add("或运行命令：ipconfig /release 后 ipconfig /renew（见问题解决模块）。");
            r.TipsEn.Add("Your PC did not get a valid IP from the router (169.254 auto-address).");
            r.TipsEn.Add("Open Network Settings → Change adapter options → right-click your adapter → Diagnose.");
            r.TipsEn.Add("Or run: ipconfig /release then ipconfig /renew (see Issue Solver).");
            return r;
        }

        // 4b) 无默认网关 → 本地网络不完整
        if (active.Gateways.Count == 0)
        {
            r.VerdictZh = "已连接但无法上网（缺少默认网关）";
            r.VerdictEn = "Connected but no default gateway";
            r.TipsZh.Add("已获取 IP 但没有默认网关，通常是无线路由器未分配网关或网络配置异常。");
            r.TipsZh.Add("重启路由器后重新连接试试。");
            r.TipsZh.Add("检查是否开启了 VPN 或代理，干扰了路由表。");
            r.TipsEn.Add("You have an IP but no gateway — usually the router failed to assign one.");
            r.TipsEn.Add("Restart the router and reconnect.");
            r.TipsEn.Add("Check whether a VPN or proxy is interfering with routing.");
            return r;
        }

        // 4c) 网关可达性
        bool gwOk = PingHost(active.Gateways[0]);
        r.Rows.Add(new Row
        {
            Label = UiLanguage.L("路由器 / 网关", "Router / gateway"),
            Value = gwOk ? UiLanguage.L("可以访问", "reachable") : UiLanguage.L("无法访问", "unreachable"),
            Color = gwOk ? Green : Red
        });

        // 4d) DNS 解析（网址翻译服务）
        bool dnsOk = ResolveDns();
        r.Rows.Add(new Row
        {
            Label = UiLanguage.L("网址翻译（DNS）", "Domain name service (DNS)"),
            Value = dnsOk ? UiLanguage.L("正常", "OK") : UiLanguage.L("失败（网页可能打不开）", "Failed (web pages may not open)"),
            Color = dnsOk ? Green : Red
        });

        // 4e) 外网连通性：ICMP + HTTP 双通道
        var pingOk = PingHost("223.5.5.5");
        var httpOk = HttpProbeOk();
        r.Rows.Add(new Row
        {
            Label = UiLanguage.L("互联网连通", "Internet reachable"),
            Value = httpOk ? UiLanguage.L("正常（可访问外网）", "OK (internet reachable)")
                  : pingOk ? UiLanguage.L("部分受限（ICMP 通但网页通道异常）", "Partial (ping works but web is blocked)")
                  : UiLanguage.L("不可达", "unreachable"),
            Color = httpOk ? Green : (pingOk ? Orange : Red)
        });

        // 4f) 网速分级（仅当外网通时给出简单参考）
        if (httpOk)
        {
            long? kbps = QuickDownloadKbps();
            if (kbps.HasValue)
            {
                string grade = kbps >= 100_000 ? UiLanguage.L("极快", "Excellent")
                    : kbps >= 30_000 ? UiLanguage.L("良好", "Good")
                    : kbps >= 8_000 ? UiLanguage.L("一般", "Fair")
                    : UiLanguage.L("偏慢", "Slow");
                r.Rows.Add(new Row
                {
                    Label = UiLanguage.L("网速参考", "Speed reference"),
                    Value = $"{kbps / 1000.0:F1} Mbps（{grade}）",
                    Color = kbps >= 8_000 ? Green : Orange
                });
            }
        }

        // 5) 结论分级
        if (!gwOk)
        {
            r.VerdictZh = "本地网络异常（路由器不可达）";
            r.VerdictEn = "Local network problem (router unreachable)";
            r.IsWarn = true;
            r.TipsZh.Add("电脑连不上路由器：检查网线 / Wi-Fi 是否真的连接，或重启路由器。");
            r.TipsZh.Add("若刚断网，可稍等片刻让路由器重新分配地址。");
            r.TipsEn.Add("Your PC cannot reach the router: check the cable / Wi-Fi link or restart the router.");
            r.TipsEn.Add("If you just disconnected, wait a moment for the router to reassign addresses.");
        }
        else if (!httpOk && !pingOk)
        {
            r.VerdictZh = "路由器正常，但外网中断";
            r.VerdictEn = "Router is fine but the internet is down";
            r.IsWarn = true;
            r.TipsZh.Add("能连上路由器但上不了网，通常是宽带 / 光猫故障。");
            r.TipsZh.Add("重启光猫和路由器（先光猫后路由器，各等 1 分钟）。");
            r.TipsZh.Add("检查是否欠费，或致电宽带运营商。");
            r.TipsEn.Add("You reach the router but not the internet — usually a WAN / modem fault.");
            r.TipsEn.Add("Restart the modem first, then the router (1 minute each).");
            r.TipsEn.Add("Check for unpaid bills or contact your ISP.");
        }
        else if (!dnsOk)
        {
            r.VerdictZh = "网络可通，但网址解析异常";
            r.VerdictEn = "Network is up but DNS is failing";
            r.IsWarn = true;
            r.TipsZh.Add("能上网但打不开网页，通常是 DNS 配置问题。");
            r.TipsZh.Add("可将 DNS 改为 223.5.5.5（阿里）或 119.29.29.29（腾讯）。");
            r.TipsEn.Add("Internet works but pages do not open — usually a DNS issue.");
            r.TipsEn.Add("Switch DNS to 223.5.5.5 (Ali) or 119.29.29.29 (Tencent).");
        }
        else
        {
            r.VerdictZh = "网络正常";
            r.VerdictEn = "Network is normal";
            r.IsGood = true;
            r.TipsZh.Add("所有检测通过，可以正常上网。");
            r.TipsZh.Add("若个别网页打不开，可尝试清除浏览器缓存或更换 DNS。");
            r.TipsZh.Add("有线连接通常比无线更稳定。");
            r.TipsEn.Add("All checks passed. You are online.");
            r.TipsEn.Add("If a specific site fails, clear the browser cache or change DNS.");
            r.TipsEn.Add("A wired connection is usually more stable than Wi-Fi.");
        }
        return r;
    }

    // ── 检测原语 ────────────────────────────────────────────────────────────

    private sealed class NetAdapter
    {
        public string Name = "";
        public bool IsWifi;
        public bool IsVirtual;
        public string IPv4 = "";
        public bool HasValidIpv4;          // 非 APIPA(169.254.*)
        public string IPv4Summary = "";    // "192.168.1.5" 或 "无有效 IP"
        public readonly List<string> Gateways = new();
    }

    private static NetAdapter CollectAdapter(NetworkInterface ni)
    {
        var a = new NetAdapter { Name = ni.Name };
        a.IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        a.IsVirtual = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp
                   || ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                   || ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase)
                   || ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase)
                   || ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

        var props = ni.GetIPProperties();
        foreach (var addr in props.UnicastAddresses)
        {
            if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
            var ip = addr.Address.ToString();
            if (a.IPv4 == "") a.IPv4 = ip;
            if (!IsApipa(ip)) { a.HasValidIpv4 = true; }
        }
        foreach (var g in props.GatewayAddresses)
            if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                a.Gateways.Add(g.Address.ToString());

        a.IPv4Summary = a.IPv4 == "" ? UiLanguage.L("无 IP", "no IP") : a.IPv4;
        return a;
    }

    /// <summary>APIPA：Windows 拿不到 DHCP 时的自动地址段 169.254.x.x。</summary>
    private static bool IsApipa(string ip)
        => ip.StartsWith("169.254.", StringComparison.Ordinal);

    private static bool PingHost(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, 2500);
            return reply != null && reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    private static bool ResolveDns()
    {
        try
        {
            var addrs = Dns.GetHostAddresses("www.baidu.com");
            return addrs.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>HTTP 204 探测：任一端点返回 2xx 即视为外网可达（比 ICMP 更接近真实网页体验）。</summary>
    private static bool HttpProbeOk()
    {
        foreach (var url in ProbeHttpUrls)
        {
            try
            {
                using var cts = HttpClientProvider.Timeout(4);
                using var resp = HttpClientProvider.Shared.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                int code = (int)resp.StatusCode;
                if (code is >= 200 and < 400) return true;
            }
            catch { /* 换下一个端点 */ }
        }
        return false;
    }

    /// <summary>快速带宽参考：下载 2MB 测试文件换算 Mbps；失败返回 null（不影响主结论）。</summary>
    private static long? QuickDownloadKbps()
    {
        try
        {
            using var cts = HttpClientProvider.Timeout(6);
            var sw = Stopwatch.StartNew();
            var data = HttpClientProvider.Shared.GetByteArrayAsync(
                "https://speed.cloudflare.com/__down?bytes=2000000", cts.Token).GetAwaiter().GetResult();
            sw.Stop();
            double sec = sw.Elapsed.TotalSeconds;
            if (sec <= 0.3 || data.Length == 0) return null;
            return (long)((data.Length * 8.0) / (sec * 1000.0)); // kbps
        }
        catch { return null; }
    }

    /// <summary>WiFi 状态：解析 netsh wlan show interfaces 输出（后台线程，150ms 内返回）。</summary>
    private static (bool hasAdapter, bool connected, string ssid, int signal) QueryWifiState()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return (false, false, "", 0);
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            if (string.IsNullOrWhiteSpace(outp)) return (false, false, "", 0);

            // 无无线网卡：中/英文系统都会输出明确提示
            if (outp.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase)
                || outp.Contains("没有无线接口", StringComparison.Ordinal))
                return (false, false, "", 0);

            var mState = Regex.Match(outp, @"(?:状态|State)\s*:\s*(.+)\r?$", RegexOptions.Multiline);
            var mSsid = Regex.Match(outp, @"SSID\s*:\s*(.+)\r?$", RegexOptions.Multiline);
            var mSignal = Regex.Match(outp, @"(?:信号|Signal)\s*:\s*(\d+)%", RegexOptions.Multiline);

            // 有状态行 / SSID 行即认为存在无线网卡
            bool hasAdapter = mState.Success || mSsid.Success;
            string stateText = mState.Success ? mState.Groups[1].Value.Trim() : "";
            bool connected = stateText.Contains("已连接", StringComparison.Ordinal)
                          || stateText.StartsWith("connected", StringComparison.OrdinalIgnoreCase);

            string ssid = mSsid.Success ? mSsid.Groups[1].Value.Trim() : "";
            int signal = mSignal.Success && int.TryParse(mSignal.Groups[1].Value, out var s) ? s : 0;
            return (hasAdapter, connected, ssid, signal);
        }
        catch { return (false, false, "", 0); }
    }

    // ── 测速 ────────────────────────────────────────────────────────────────

    private async void BtnSpeed_Click(object sender, RoutedEventArgs e)
    {
        ListPanel.Children.Clear();
        TxtStatus.Text = UiLanguage.L("测速中…", "Testing…");
        BtnSpeed.IsEnabled = false;
        BtnDiag.IsEnabled = false;
        BtnSpeedCancel.IsEnabled = true;
        _speedCts = new System.Threading.CancellationTokenSource();
        var speedToken = _speedCts.Token;
        try
        {
            var result = await Task.Run(() =>
            {
                var r = new NetResult();
                // 1) 延迟测试（后台线程，不卡 UI）
                var pingTargets = new[] { "223.5.5.5", "119.29.29.29", "www.baidu.com" };
                long total = 0, count = 0, min = long.MaxValue, max = 0;
                foreach (var target in pingTargets)
                {
                    speedToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var ping = new Ping();
                        var reply = ping.Send(target, 2000);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            total += reply.RoundtripTime;
                            count++;
                            if (reply.RoundtripTime < min) min = reply.RoundtripTime;
                            if (reply.RoundtripTime > max) max = reply.RoundtripTime;
                            r.Rows.Add(new Row
                            {
                                Label = Glossary.Hint($"Ping {target}"),
                                Value = $"{UiLanguage.L("延迟", "latency")} {reply.RoundtripTime} ms",
                                Color = Green
                            });
                        }
                        else
                        {
                            r.Rows.Add(new Row
                            {
                                Label = Glossary.Hint($"Ping {target}"),
                                Value = UiLanguage.L("失败：", "Failed: ") + reply?.Status,
                                Color = Red
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        r.Rows.Add(new Row
                        {
                            Label = Glossary.Hint($"Ping {target}"),
                            Value = UiLanguage.L("失败：", "Failed: ") + ex.Message,
                            Color = Red
                        });
                    }
                }

                if (count > 0)
                {
                    double avg = total / (double)count;
                    r.Rows.Add(new Row
                    {
                        Label = Glossary.Hint("平均延迟 (Ping)"),
                        Value = $"{avg:F1} ms（{UiLanguage.L("最小", "min")} {min} / {UiLanguage.L("最大", "max")} {max}，{count} {UiLanguage.L("个节点", "nodes")}）",
                        Color = Green
                    });
                }
                else
                {
                    r.Rows.Add(new Row
                    {
                        Label = Glossary.Hint("平均延迟 (Ping)"),
                        Value = UiLanguage.L("无法 Ping 通任何测试节点", "No test node reachable"),
                        Color = Red
                    });
                }

                // 2) 下行带宽测速（HTTP 下载）
                double? mbps = MeasureDownloadSpeed(speedToken);
                if (mbps.HasValue)
                {
                    string grade = mbps.Value >= 100 ? UiLanguage.L("极快", "Excellent")
                        : mbps.Value >= 50 ? UiLanguage.L("良好", "Good")
                        : mbps.Value >= 20 ? UiLanguage.L("一般", "Fair")
                        : UiLanguage.L("偏慢", "Slow");
                    r.Rows.Add(new Row
                    {
                        Label = UiLanguage.L("下行速率", "Download speed"),
                        Value = $"{mbps.Value:F1} Mbps（{grade}）",
                        Color = mbps.Value >= 20 ? Green : Orange
                    });
                    r.IsGood = true;
                    r.VerdictZh = UiLanguage.L("测速完成", "Speed test done");
                    r.VerdictEn = r.VerdictZh;
                    r.TipsZh.Add("测速结果受服务器距离、时段和局域网负载影响，仅供参考。");
                    r.TipsZh.Add("测速时请关闭占用带宽的下载或视频，有线连接通常优于无线。");
                    r.TipsZh.Add("如需更精确的结果，可多次测速取平均值。");
                    r.TipsEn.Add("Results vary with server distance, time of day and LAN load; for reference only.");
                    r.TipsEn.Add("Close bandwidth-heavy apps while testing; wired is usually better than Wi-Fi.");
                    r.TipsEn.Add("Run several times and average for a more accurate number.");
                }
                else
                {
                    r.VerdictZh = UiLanguage.L("测速失败：无法连接测速服务器（可能无外网或被防火墙拦截）", "Failed: cannot reach speed-test server (no internet or blocked by firewall)");
                    r.VerdictEn = r.VerdictZh;
                    r.Rows.Add(new Row
                    {
                        Label = UiLanguage.L("下行速率", "Download speed"),
                        Value = UiLanguage.L("不可用", "unavailable"),
                        Color = Red
                    });
                }
                return r;
            });

            RenderResult(result);
            TxtStatus.Text = UiLanguage.L("测速完成", "Speed test done");
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = UiLanguage.L("已取消测速", "Speed test canceled");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("测速出错：", "Speed test error: ") + ex.Message;
        }
        finally
        {
            _speedCts?.Dispose();
            _speedCts = null;
            BtnSpeed.IsEnabled = true;
            BtnDiag.IsEnabled = true;
            BtnSpeedCancel.IsEnabled = false;
        }
    }

    /// <summary>依次尝试多个测速端点，返回下行速率(Mbps)；全部失败返回 null（后台线程）。支持取消。</summary>
    private static double? MeasureDownloadSpeed(CancellationToken ct)
    {
        var urls = new[]
        {
            "https://speed.cloudflare.com/__down?bytes=8000000",
            "https://download.thinkbroadband.com/5MB.zip"
        };
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var cts = HttpClientProvider.Timeout(20);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var sw = Stopwatch.StartNew();
                var data = HttpClientProvider.Shared.GetByteArrayAsync(url, linked.Token).GetAwaiter().GetResult();
                sw.Stop();
                double sec = sw.Elapsed.TotalSeconds;
                if (sec <= 0 || data.Length == 0) continue;
                return (data.Length * 8.0) / (sec * 1_000_000.0);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 该端点不可用，尝试下一个 */ }
        }
        return null;
    }

    // ── 渲染 ────────────────────────────────────────────────────────────────

    private void RenderResult(NetResult r)
    {
        ListPanel.Children.Clear();

        // 结论横幅（大字号 + 醒目底色）
        var verdict = new Border
        {
            Background = new SolidColorBrush(r.IsGood ? Color.FromRgb(0xE8, 0xF5, 0xE9)
                            : r.IsWarn ? Color.FromRgb(0xFF, 0xF3, 0xE0)
                            : Color.FromRgb(0xFD, 0xED, 0xEC)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var vsp = new StackPanel();
        vsp.Children.Add(new TextBlock
        {
            Text = (r.IsGood ? "✓ " : r.IsWarn ? "! " : "✗ ") + UiLanguage.L(r.VerdictZh, r.VerdictEn),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(r.IsGood ? Color.FromRgb(0x2E, 0x7D, 0x32)
                            : r.IsWarn ? Color.FromRgb(0xBF, 0x36, 0x0C)
                            : Color.FromRgb(0xC0, 0x39, 0x2B))
        });
        verdict.Child = vsp;
        ListPanel.Children.Add(verdict);

        // 详细检测行
        foreach (var row in r.Rows)
            ListPanel.Children.Add(BuildRow(row));

        // 修复建议
        if (r.TipsZh.Count > 0)
        {
            var tips = new Border
            {
                Background = new SolidColorBrush(r.IsGood ? Color.FromRgb(0xE8, 0xF5, 0xE9) : Color.FromRgb(0xFF, 0xF3, 0xE0)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var tsp = new StackPanel();
            tsp.Children.Add(new TextBlock
            {
                Text = UiLanguage.L("建议", "Suggestions"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Dark
            });
            for (int i = 0; i < r.TipsZh.Count; i++)
            {
                tsp.Children.Add(new TextBlock
                {
                    Text = "• " + Glossary.Hint(UiLanguage.L(r.TipsZh[i], r.TipsEn[i])),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(r.IsGood ? Color.FromRgb(0x2E, 0x7D, 0x32) : Color.FromRgb(0xBF, 0x36, 0x0C)),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            tips.Child = tsp;
            ListPanel.Children.Add(tips);
        }
    }

    private static Border BuildRow(Row row)
    {
        var b = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xF0, 0xF3)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = row.Label,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Dark
        });
        sp.Children.Add(new TextBlock
        {
            Text = row.Value,
            FontSize = 12,
            Foreground = row.Color,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        b.Child = sp;
        return b;
    }
}
