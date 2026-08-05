using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// CheckupPage.xaml 交互逻辑 — 一键体检（导航 key="checkup"，v4.9.0 新增）。
    /// 聚合硬件信息 + 健康分 + 垃圾扫描 + 启动项 + 磁盘空间，生成自包含内联 CSS 的
    /// HTML 报告（深浅两套配色跟随主题）与纯文本格式；保存到桌面便于分享求助。
    /// 脱敏：报告中的用户名一律替换为 &lt;user&gt;，不输出完整个人文件路径（呼应安全审计 P3）。
    /// </summary>
    public partial class CheckupPage : UserControl
    {
        private bool _busy;
        private string _lastHtml = "";     // HTML 报告内容
        private string? _lastHtmlPath;     // 已保存的 HTML 文件路径
        private string _lastText = "";

        public CheckupPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            UiMode.Changed += OnModeChanged;
            Unloaded += (_, __) => UiMode.Changed -= OnModeChanged;
        }

        /// <summary>普通/专业模式切换：若已生成过报告，按新模式重新生成</summary>
        private void OnModeChanged()
        {
            Dispatcher.Invoke(async () =>
            {
                if (_busy || string.IsNullOrEmpty(_lastText)) return;
                try
                {
                    var (text, html) = await BuildReportAsync();
                    _lastText = text;
                    TxtReport.Text = text;
                }
                catch { /* 保持旧报告 */ }
            });
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnRun, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnSave, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnOpen, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnCopy, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            BtnRun.IsEnabled = false;
            BtnRun.Content = UiLanguage.L("体检中…", "Checking…");
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TxtStatus.Text = UiLanguage.L("正在采集硬件信息与健康指标…", "Collecting hardware & health metrics…");
            try
            {
                var (text, html) = await BuildReportAsync();
                _lastText = text;
                TxtReport.Text = text;
                TxtScore.Text = _score.ToString();
                TxtGrade.Text = _grade;
                ScoreBar.Value = _score;
                ScoreBar.Foreground = new SolidColorBrush(ScoreColor(_score));
                BtnOpen.IsEnabled = !string.IsNullOrEmpty(_lastHtmlPath);
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
                TxtStatus.Text = UiLanguage.L("体检完成：可「保存报告到桌面」或「复制文本」分享。",
                    "Done: save to Desktop or copy text to share.");
            }
            catch (Exception ex)
            {
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtStatus.Text = UiLanguage.L("体检出错：", "Checkup failed: ") + ex.Message;
            }
            finally
            {
                _busy = false;
                BtnRun.IsEnabled = true;
                BtnRun.Content = UiLanguage.L("开始体检", "Start checkup");
            }
        }

        private int _score;
        private string _grade = "";

        private async Task<(string text, string html)> BuildReportAsync()
        {
            var sb = new StringBuilder();
            var username = Environment.UserName;

            // ===== 1) 健康分 =====
            var health = HealthScoreService.Compute();
            _score = health.Score;
            _grade = health.Grade;
            sb.AppendLine("===== 系统健康评分 =====");
            sb.AppendLine($"评分：{health.Score}/100（{health.Grade}）");
            sb.AppendLine($"结论：{health.Summary}");
            if (health.Suggestions.Count > 0)
            {
                sb.AppendLine("建议：");
                foreach (var s in health.Suggestions) sb.AppendLine("  • " + s);
            }
            sb.AppendLine();

            // ===== 2) 硬件信息 =====
            sb.AppendLine("===== 硬件信息 =====");
            try
            {
                var items = await HardwareInfo.CollectAsync();
                foreach (var it in items.Take(24))
                    sb.AppendLine($"{Glossary.Hint(it.Label)}：{Sanitize(it.Value, username)}");
            }
            catch (Exception ex) { sb.AppendLine("（硬件信息获取失败：" + ex.Message + "）"); }
            sb.AppendLine();

            // ===== 3) 垃圾占用估算 =====
            sb.AppendLine("===== 可清理空间估算 =====");
            long junk = 0;
            try
            {
                var (tmpSize, _) = await Task.Run(() => Cleaner.SumMatching(Cleaner.TempDirs(), "*", SearchOption.TopDirectoryOnly));
                var (brSize, _) = await Task.Run(() => Cleaner.SumMatching(Cleaner.BrowserCacheDirs(), "*", SearchOption.AllDirectories));
                var (upSize, _) = await Task.Run(() => Cleaner.SumMatching(Cleaner.UpdateCacheDirs(), "*", SearchOption.AllDirectories));
                junk = tmpSize + brSize + upSize;
                sb.AppendLine($"临时文件：{FormatSize(tmpSize)}");
                sb.AppendLine($"浏览器缓存：{FormatSize(brSize)}");
                sb.AppendLine($"更新缓存：{FormatSize(upSize)}");
                sb.AppendLine($"合计可清理：{FormatSize(junk)}");
                if (!UiMode.IsPro)
                    sb.AppendLine("说明：这些是系统运行产生的临时数据，清理后不影响系统和已装软件。");
                if (!UiMode.IsPro)
                    sb.AppendLine("（提示：这些都是程序运行产生的临时数据，清理后不影响系统和已装软件）");
            }
            catch (Exception ex) { sb.AppendLine("（扫描失败：" + ex.Message + "）"); }
            sb.AppendLine();

            // ===== 4) 启动项 =====
            sb.AppendLine("===== 开机启动项 =====");
            try
            {
                int n = 0;
                foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                    using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                        if (key != null) n += key.ValueCount;
                sb.AppendLine($"当前开机启动项：{n} 项" + (n > 12 ? "（偏多，可在「启动项」模块精简）" : ""));
            }
            catch { sb.AppendLine("（读取失败）"); }
            sb.AppendLine();

            // ===== 5) 磁盘空间 =====
            sb.AppendLine("===== 磁盘空间 =====");
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    sb.AppendLine($"{d.Name}  总 {FormatSize(d.TotalSize)}  剩余 {FormatSize(d.TotalFreeSpace)}" +
                                  $"  ({d.TotalFreeSpace * 100.0 / Math.Max(d.TotalSize, 1):F0}%)");
            }
            catch (Exception ex) { sb.AppendLine("（磁盘读取失败：" + ex.Message + "）"); }
            sb.AppendLine();

            // ===== 6) 系统信息 =====
            sb.AppendLine("===== 系统信息 =====");
            sb.AppendLine($"操作系统：{Environment.OSVersion.VersionString}");
            sb.AppendLine($"系统位数：{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"用户：<user>");
            sb.AppendLine($"体检时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("—— 由 司南工具箱 v" + UpdateManager.LocalVersion + " 生成 ——");

            // ===== HTML 版（内联 CSS，深浅随主题）=====
            bool isDark = ThemeManager.ActivePresetKey is "starry" or "aurora";
            _lastHtml = BuildHtml(sb.ToString(), health, junk, isDark, username);
            _lastHtmlPath = null;
            return (sb.ToString(), _lastHtml);
        }

        private string BuildHtml(string text, HealthScoreService.HealthResult health, long junk, bool dark, string username)
        {
            string bg = dark ? "#1E2430" : "#F4F6FA";
            string card = dark ? "#2A3242" : "#FFFFFF";
            string fg = dark ? "#D6DEEA" : "#2C3E50";
            string sub = dark ? "#9AA7BD" : "#7F8C8D";
            string accent = dark ? "#5FA8FF" : "#4A90D9";
            string good = dark ? "#4CAF78" : "#27AE60";
            string warn = dark ? "#E6A23C" : "#E67E22";
            var color = health.Score >= 85 ? good : health.Score >= 50 ? warn : "#E74C3C";

            var esc = new StringBuilder(text).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\n", "<br/>").ToString();

            return $@"<!DOCTYPE html>
<html lang=""zh-CN""><head><meta charset=""utf-8""/>
<title>司南工具箱体检报告</title>
<style>
body{{margin:0;padding:24px;background:{bg};font-family:'Microsoft YaHei',sans-serif;color:{fg};}}
.wrap{{max-width:860px;margin:0 auto;}}
h1{{font-size:20px;margin:0 0 4px;}}
.sub{{color:{sub};font-size:12px;margin-bottom:16px;}}
.score{{display:inline-block;background:{color};color:#fff;border-radius:10px;
  padding:10px 18px;font-size:22px;font-weight:bold;margin:8px 0;}}
.bar{{height:10px;background:{card};border-radius:5px;overflow:hidden;margin:6px 0 14px;}}
.bar i{{display:block;height:100%;width:{health.Score}%;background:{color};}}
.card{{background:{card};border-radius:12px;padding:14px 16px;margin:10px 0;}}
.card h2{{font-size:14px;margin:0 0 8px;color:{accent};}}
.card p{{font-size:12.5px;line-height:1.7;margin:2px 0;}}
li{{font-size:12.5px;line-height:1.7;}}
.foot{{color:{sub};font-size:11px;margin-top:18px;text-align:center;}}
</style></head><body><div class=""wrap"">
<h1>司南工具箱体检报告</h1>
<div class=""sub"">生成时间：{DateTime.Now:yyyy-MM-dd HH:mm} ｜ 软件版本 v{UpdateManager.LocalVersion} ｜ 用户：&lt;user&gt;</div>
<div class=""score"">健康分 {health.Score} / 100（{health.Grade}）</div>
<div class=""bar""><i></i></div>
<div class=""card""><h2>结论</h2><p>{health.Summary}</p></div>
{(health.Suggestions.Count > 0 ? "<div class=\"card\"><h2>优化建议</h2><ul>" + string.Concat(health.Suggestions.Select(s => "<li>" + HtmlEsc(s) + "</li>")) + "</ul></div>" : "")}
<div class=""card""><h2>可清理空间</h2><p>合计约 <b>" + FormatSize(junk) + @"</b>（可在「系统清理」模块一键释放）</p></div>
<div class=""card""><h2>详细数据</h2><p>" + esc + @"</p></div>
<div class=""foot"">—— 由 司南工具箱（GPL v2 开源）生成 ——</div>
</div></body></html>";

            static string HtmlEsc(string s) =>
                s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>脱敏：用户名 → &lt;user&gt;，并避免输出完整个人文件路径。</summary>
        private static string Sanitize(string value, string username)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var s = value.Replace(username, "<user>", StringComparison.OrdinalIgnoreCase);
            // 常见个人目录前缀也替换
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            s = s.Replace(local, "%LocalAppData%").Replace(appData, "%AppData%");
            return s;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F0} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static Color ScoreColor(int score)
            => score >= 85 ? Color.FromRgb(0x27, 0xAE, 0x60)
             : score >= 50 ? Color.FromRgb(0xE6, 0x7E, 0x22)
             : Color.FromRgb(0xE7, 0x4C, 0x3C);

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var htmlPath = Path.Combine(dir, $"司南工具箱体检报告_{stamp}.html");
                File.WriteAllText(htmlPath, _lastHtml, Encoding.UTF8);
                _lastHtmlPath = htmlPath;
                MessageBox.Show(UiLanguage.L("报告已保存到：\n", "Report saved to:\n") + htmlPath,
                    UiLanguage.L("保存成功", "Saved"), MessageBoxButton.OK, MessageBoxImage.Information);
                BtnOpen.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLanguage.L("保存失败：", "Save failed: ") + ex.Message,
                    UiLanguage.L("提示", "Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastHtmlPath) || !File.Exists(_lastHtmlPath)) return;
            try { Process.Start(new ProcessStartInfo(_lastHtmlPath) { UseShellExecute = true }); }
            catch { }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastText)) return;
            try { Clipboard.SetText(_lastText); }
            catch { }
        }
    }
}
