using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

public partial class Window12 : UserControl
{
    public Window12()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnDiag, Color.FromRgb(0x00, 0x96, 0x88),
            hoverColor: Color.FromRgb(0x00, 0x79, 0x6E));
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
