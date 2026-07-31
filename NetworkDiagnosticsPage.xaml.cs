using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>网络诊断页：网络连通性检测与网速测试。</summary>
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

    private async void BtnDiag_Click(object sender, RoutedEventArgs e)
    {
        ListPanel.Children.Clear();
        TxtStatus.Text = "诊断中…";
        BtnDiag.IsEnabled = false;

        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                bool online = NetworkInterface.GetIsNetworkAvailable();
                AddRow("网络可用性", online ? "已连接" : "未连接", online ? "#27AE60" : "#E74C3C");

                if (!online)
                {
                    AddFaultTips("网络未连接", new[]
                    {
                        "检查网线是否插好，或 Wi-Fi 是否已连接。",
                        "确认飞行模式已关闭。",
                        "尝试重启路由器或光猫。"
                    });
                    TxtStatus.Text = "诊断完成：当前未连接到网络";
                    BtnDiag.IsEnabled = true;
                    return;
                }

                // 网卡列表
                int up = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    up++;
                    string speed = ni.Speed >= 1_000_000_000 ? $"{ni.Speed / 1_000_000_000} Gbps"
                        : ni.Speed >= 1_000_000 ? $"{ni.Speed / 1_000_000} Mbps" : $"{ni.Speed} bps";
                    AddRow(ni.Name, $"{ni.NetworkInterfaceType} · {speed}", "#2C3E50");
                }
                AddRow("可用网卡", $"{up} 个已启用", up > 0 ? "#27AE60" : "#E74C3C");

                // Ping 测试
                var pingTargets = new[] { "223.5.5.5", "119.29.29.29", "www.baidu.com" };
                int okCount = 0;
                foreach (var target in pingTargets)
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = ping.Send(target, 2000);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            AddRow($"Ping {target}", $"成功 · 延迟 {reply.RoundtripTime} ms", "#27AE60");
                            okCount++;
                        }
                        else
                        {
                            AddRow($"Ping {target}", $"失败：{reply?.Status}", "#E74C3C");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRow($"Ping {target}", "失败：" + ex.Message, "#E74C3C");
                    }
                }

                // DNS 解析
                try
                {
                    var addresses = Dns.GetHostAddresses("www.baidu.com");
                    AddRow("DNS 解析", addresses.Length > 0 ? $"成功 · {addresses[0]}" : "无结果", "#27AE60");
                }
                catch (Exception ex)
                {
                    AddRow("DNS 解析", "失败：" + ex.Message, "#E74C3C");
                }

                // 根据结果给出提示
                if (okCount == 0)
                {
                    AddFaultTips("网络连通性差", new[]
                    {
                        "检查路由器、光猫是否正常工作。",
                        "尝试右键任务栏网络图标 → 疑难解答。",
                        "运行命令：netsh winsock reset，然后重启电脑。"
                    });
                    TxtStatus.Text = "诊断完成：网络连通性较差";
                }
                else if (okCount < pingTargets.Length)
                {
                    AddFaultTips("部分网络访问异常", new[]
                    {
                        "部分目标无法 Ping 通，可能是 DNS 或防火墙限制。",
                        "尝试将 DNS 修改为 223.5.5.5 或 119.29.29.29。",
                        "检查是否开启了代理或 VPN。"
                    });
                    TxtStatus.Text = "诊断完成：部分网络访问异常";
                }
                else
                {
                    AddFaultTips("网络状况良好", new[]
                    {
                        "所有测试均通过，网络连接正常。",
                        "若个别网页仍打不开，可尝试清除浏览器缓存或更换 DNS。",
                        "有线连接比无线更稳定，重要任务建议优先使用网线。"
                    }, true);
                    TxtStatus.Text = "诊断完成：网络正常";
                }

                BtnDiag.IsEnabled = true;
            });
        });
    }

    private async void BtnSpeed_Click(object sender, RoutedEventArgs e)
    {
        ListPanel.Children.Clear();
        TxtStatus.Text = UiLanguage.L("测速中…", "Testing…");
        BtnSpeed.IsEnabled = false;
        BtnDiag.IsEnabled = false;
        try
        {
            // 1) 延迟测试：对多个节点 Ping，统计平均 / 最小 / 最大延迟
            var pingTargets = new[] { "223.5.5.5", "119.29.29.29", "www.baidu.com" };
            long total = 0, count = 0, min = long.MaxValue, max = 0;
            foreach (var target in pingTargets)
            {
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
                        AddRow($"Ping {target}", $"成功 · 延迟 {reply.RoundtripTime} ms", "#27AE60");
                    }
                    else
                    {
                        AddRow($"Ping {target}", $"失败：{reply?.Status}", "#E74C3C");
                    }
                }
                catch (Exception ex)
                {
                    AddRow($"Ping {target}", "失败：" + ex.Message, "#E74C3C");
                }
            }

            if (count > 0)
            {
                double avg = total / (double)count;
                AddRow("平均延迟 (Ping)", $"{avg:F1} ms（最小 {min} / 最大 {max}，{count} 个节点）", "#27AE60");
            }
            else
            {
                AddRow("平均延迟 (Ping)", "无法 Ping 通任何测试节点", "#E74C3C");
            }

            // 2) 下行带宽测试：下载固定大小文件，按字节/耗时换算 Mbps
            AddRow("下行测速", UiLanguage.L("正在下载测试文件…", "Downloading test file…"), "#2C3E50");
            double? mbps = await MeasureDownloadSpeed();
            if (mbps.HasValue)
            {
                string grade = mbps.Value >= 100 ? UiLanguage.L("极快", "Excellent")
                    : mbps.Value >= 50 ? UiLanguage.L("良好", "Good")
                    : mbps.Value >= 20 ? UiLanguage.L("一般", "Fair")
                    : UiLanguage.L("偏慢", "Slow");
                AddRow("下行速率", $"{mbps.Value:F1} Mbps（{grade}）", mbps.Value >= 20 ? "#27AE60" : "#E67E22");
                AddFaultTips("测速说明", new[]
                {
                    UiLanguage.L("测速结果受服务器距离、时段和局域网负载影响，仅供参考。", "Results vary with server distance, time of day and LAN load; for reference only."),
                    UiLanguage.L("测速时请关闭占用带宽的下载或视频，有线连接通常优于无线。", "Close bandwidth-heavy apps while testing; wired is usually better than Wi-Fi."),
                    UiLanguage.L("如需更精确的结果，可多次测速取平均值。", "Run several times and average for a more accurate number.")
                }, true);
                TxtStatus.Text = UiLanguage.L("测速完成", "Speed test done");
            }
            else
            {
                AddRow("下行速率", UiLanguage.L("测速失败：无法连接测速服务器（可能无外网或被防火墙拦截）", "Failed: cannot reach speed-test server (no internet or blocked by firewall)"), "#E74C3C");
                TxtStatus.Text = UiLanguage.L("测速完成（部分失败）", "Speed test done (partial failure)");
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = UiLanguage.L("测速出错：", "Speed test error: ") + ex.Message;
        }
        finally
        {
            BtnSpeed.IsEnabled = true;
            BtnDiag.IsEnabled = true;
        }
    }

    /// <summary>依次尝试多个测速端点，返回下行速率(Mbps)；全部失败返回 null。</summary>
    private async Task<double?> MeasureDownloadSpeed()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try { client.DefaultRequestHeaders.UserAgent.ParseAdd("司南工具箱"); } catch { }
        var urls = new[]
        {
            "https://speed.cloudflare.com/__down?bytes=8000000",
            "https://download.thinkbroadband.com/5MB.zip"
        };
        foreach (var url in urls)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var data = await client.GetByteArrayAsync(url);
                sw.Stop();
                double sec = sw.Elapsed.TotalSeconds;
                if (sec <= 0 || data.Length == 0) continue;
                return (data.Length * 8.0) / (sec * 1_000_000.0);
            }
            catch
            {
                // 该端点不可用，尝试下一个
            }
        }
        return null;
    }

    private void AddRow(string title, string sub, string color)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF0F3")),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
        });
        sp.Children.Add(new TextBlock
        {
            Text = sub,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        row.Child = sp;
        ListPanel.Children.Add(row);
    }

    private void AddFaultTips(string title, string[] tips, bool isGood = false)
    {
        var head = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isGood ? "#E8F5E9" : "#FFF3E0")),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = (isGood ? "✅ " : "💡 ") + title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
        });
        foreach (var tip in tips)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "• " + tip,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isGood ? "#2E7D32" : "#BF360C")),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        head.Child = sp;
        ListPanel.Children.Add(head);
    }
}
