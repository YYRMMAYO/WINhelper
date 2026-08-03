using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WINHELP;

/// <summary>
/// 录音录像模块（导航 key="recorder"，V4.5.0 重写）：
/// 本软件不自带录音录像功能。该页面在加载时扫描电脑中已安装的录屏 / 录音软件，
/// 若检测到则提供「打开」按钮一键启动；若未安装则提示用户前往官网下载。
/// 由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。
/// </summary>
public partial class WindowRecorder : UserControl
{
    /// <summary>RecApp 类。</summary>
    private sealed class RecApp
    {
        public string Name = "";
        public string Emoji = "";
        public string Url = "";
        public List<string> ExeNames = new();
        public List<string> RelativePaths = new();
        public string RegKeyword = "";
        public string? ResolvedExe;
    }

    // 已知录屏 / 录音软件目录（与「网站与官网」模块保持一致）
    private static readonly List<RecApp> Catalog = new()
    {
        new RecApp { Name = "OBS Studio", Emoji = "🎥", Url = "https://obsproject.com/",
            ExeNames = new(){"obs64.exe","obs32.exe"},
            RelativePaths = new(){"obs-studio\\bin\\64bit","obs-studio\\bin\\32bit"},
            RegKeyword = "OBS Studio" },
        new RecApp { Name = "Bandicam", Emoji = "🎬", Url = "https://www.bandicam.com/",
            ExeNames = new(){"bandicam.exe"}, RelativePaths = new(){""}, RegKeyword = "Bandicam" },
        new RecApp { Name = "ShareX", Emoji = "📸", Url = "https://getsharex.com/",
            ExeNames = new(){"ShareX.exe"}, RelativePaths = new(){""}, RegKeyword = "ShareX" },
        new RecApp { Name = "Camtasia", Emoji = "🎞️", Url = "https://www.techsmith.com/camtasia.html",
            ExeNames = new(){"CamtasiaStudio.exe"},
            RelativePaths = new(){"TechSmith\\Camtasia 2024","TechSmith\\Camtasia 2023","TechSmith\\Camtasia 2022","TechSmith\\Camtasia 2021","TechSmith\\Camtasia 2020","TechSmith\\Camtasia 2019"},
            RegKeyword = "Camtasia" },
        new RecApp { Name = "ScreenToGif", Emoji = "🎞️", Url = "https://www.screentogif.com/",
            ExeNames = new(){"ScreenToGif.exe"}, RelativePaths = new(){""}, RegKeyword = "ScreenToGif" },
        new RecApp { Name = "Audacity", Emoji = "🎙️", Url = "https://www.audacityteam.org/",
            ExeNames = new(){"audacity.exe"}, RelativePaths = new(){""}, RegKeyword = "Audacity" },
        new RecApp { Name = "Ocenaudio", Emoji = "🎧", Url = "https://www.ocenaudio.com/",
            ExeNames = new(){"ocenaudio.exe","ocenaudio-64.exe"}, RelativePaths = new(){""}, RegKeyword = "ocenaudio" },
        new RecApp { Name = "Adobe Audition", Emoji = "🎚️", Url = "https://www.adobe.com/products/audition.html",
            ExeNames = new(){"Audition.exe"},
            RelativePaths = new(){"Adobe\\Adobe Audition 2025","Adobe\\Adobe Audition 2024","Adobe\\Adobe Audition 2023","Adobe\\Adobe Audition 2022","Adobe\\Adobe Audition 2021","Adobe\\Adobe Audition 2020"},
            RegKeyword = "Adobe Audition" },
    };

    public WindowRecorder()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var installed = DetectInstalled();
        if (installed.Count == 0)
        {
            TxtNone.Visibility = Visibility.Visible;
        }
        else
        {
            TxtNone.Visibility = Visibility.Collapsed;
            foreach (var app in installed)
                FoundPanel.Children.Add(BuildCard(app, true));
        }
        // 推荐下载区始终展示全部已知软件，方便用户补充安装
        foreach (var app in Catalog)
            RecommendPanel.Children.Add(BuildCard(app, false));
    }

    private Border BuildCard(RecApp app, bool installed)
    {
        var border = new Border { Style = (Style)FindResource("RecCard") };
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = $"{app.Emoji} {app.Name}",
            FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
        });
        sp.Children.Add(new TextBlock
        {
            Text = installed
                ? UiLanguage.L("已安装 · 可一键打开", "Installed · launch now")
                : UiLanguage.L("未安装 · 前往官网下载", "Not installed · get it"),
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
            Margin = new Thickness(0, 2, 0, 10)
        });
        var btn = new Button
        {
            Height = 32, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Content = installed ? UiLanguage.L("打开", "Open") : UiLanguage.L("前往官网", "Official Site"),
            Cursor = Cursors.Hand
        };
        if (installed)
        {
            btn.Tag = app.ResolvedExe;
            btn.Click += OpenApp_Click;
        }
        else
        {
            btn.Tag = app.Url;
            btn.Click += OpenSite_Click;
        }
        ThemeManager.ApplyButtonTheme(btn, ThemeManager.AccentColor);
        sp.Children.Add(btn);
        border.Child = sp;
        return border;
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string path && File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L($"无法打开该程序：{ex.Message}", $"Cannot launch: {ex.Message}"),
                    UiLanguage.L("打开失败", "Launch failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L($"无法打开网页：{ex.Message}", $"Cannot open page: {ex.Message}"),
                    UiLanguage.L("打开失败", "Launch failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ===== 检测已安装的录屏 / 录音软件 =====

    private static List<RecApp> DetectInstalled()
    {
        string? prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var roots = new List<string?>(2) { prog };
        if (!string.Equals(prog, progX86, StringComparison.OrdinalIgnoreCase)) roots.Add(progX86);

        var found = new List<RecApp>();
        foreach (var app in Catalog)
        {
            string? exe = null;
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                foreach (var rel in app.RelativePaths)
                {
                    foreach (var name in app.ExeNames)
                    {
                        var p = Path.Combine(root, rel ?? "", name);
                        if (File.Exists(p)) { exe = p; break; }
                    }
                    if (exe != null) break;
                }
                if (exe != null) break;
            }
            if (exe == null) exe = FindExeInRegistry(app);
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                app.ResolvedExe = exe;
                found.Add(app);
            }
        }
        return found;
    }

    private static string? FindExeInRegistry(RecApp app)
    {
        var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };
        var subKeys = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        foreach (var hive in hives)
        {
            foreach (var sub in subKeys)
            {
                using var key = hive.OpenSubKey(sub);
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var sk = key.OpenSubKey(name);
                    if (sk == null) continue;
                    var disp = sk.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(disp)) continue;
                    if (disp.IndexOf(app.RegKeyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var loc = sk.GetValue("InstallLocation") as string;
                    if (string.IsNullOrEmpty(loc)) continue;
                    foreach (var en in app.ExeNames)
                    {
                        var cand = Path.Combine(loc, en);
                        if (File.Exists(cand)) return cand;
                        var candBin = Path.Combine(loc, "bin", en);
                        if (File.Exists(candBin)) return candBin;
                    }
                }
            }
        }
        return null;
    }
}
