using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

public partial class WindowReport : UserControl
{
    // ===== 按月统计存储（独立文件，避免改动 SettingsManager） =====
    private sealed class MonthStat
    {
        public long CleanedBytes { get; set; }
        public int OptimizeCount { get; set; }
    }

    private sealed class ReportData
    {
        public Dictionary<string, MonthStat> Months { get; set; } = new();
        public long LastCumulativeCleaned { get; set; }
        public int LastCumulativeOptimize { get; set; }
    }

    private static readonly string ReportDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
    private static readonly string ReportPath = Path.Combine(ReportDir, "report.json");

    public WindowReport()
    {
        InitializeComponent();
        ThemeManager.SetWindowIcon(this);

        // 首次使用记录
        if (SettingsManager.Current.FirstUse == default)
        {
            SettingsManager.Current.FirstUse = DateTime.Now;
            SettingsManager.Save();
        }

        ApplyTheme();
        Refresh();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        UiLanguage.Changed += () => Dispatcher.Invoke(Refresh);
    }

    private void ApplyTheme()
    {
        ThemeManager.ApplyButtonTheme(BtnReset, Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    private static string Fmt(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static ReportData LoadReport()
    {
        try
        {
            if (File.Exists(ReportPath))
            {
                var d = JsonSerializer.Deserialize<ReportData>(File.ReadAllText(ReportPath));
                if (d != null) return d;
            }
        }
        catch { }
        return new ReportData();
    }

    private static void SaveReport(ReportData d)
    {
        try
        {
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportPath, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    private void Refresh()
    {
        LblClean.Text = UiLanguage.L("累计清理", "Total Cleaned");
        LblOpt.Text = UiLanguage.L("累计优化次数", "Total Optimizes");
        LblLast.Text = UiLanguage.L("上次优化", "Last Optimize");
        LblAch.Text = UiLanguage.L("成就", "Achievements");
        BtnReset.Content = UiLanguage.L("重置统计", "Reset Stats");
        TxtNote.Text = UiLanguage.L(
            "提示：累计值为历史总和；本月值按查看时累计增量归属到当前月份（近似值）。",
            "Note: totals are cumulative; 'this month' attributes the delta seen since last view to the current month (approximate).");

        var s = SettingsManager.Current;
        var data = LoadReport();
        string key = DateTime.Now.ToString("yyyy-MM");
        if (!data.Months.ContainsKey(key)) data.Months[key] = new MonthStat();

        // 将累计增量归属到当前月
        long dClean = Math.Max(0, s.CleanedBytes - data.LastCumulativeCleaned);
        int dOpt = Math.Max(0, s.OptimizeCount - data.LastCumulativeOptimize);
        data.Months[key].CleanedBytes += dClean;
        data.Months[key].OptimizeCount += dOpt;
        data.LastCumulativeCleaned = s.CleanedBytes;
        data.LastCumulativeOptimize = s.OptimizeCount;
        SaveReport(data);

        var m = data.Months[key];

        TxtCumClean.Text = Fmt(s.CleanedBytes);
        TxtMonthClean.Text = UiLanguage.L("本月：" + Fmt(m.CleanedBytes), "This month: " + Fmt(m.CleanedBytes));
        TxtCumOpt.Text = s.OptimizeCount.ToString();
        TxtMonthOpt.Text = UiLanguage.L("本月：" + m.OptimizeCount, "This month: " + m.OptimizeCount);
        TxtStreak.Text = s.UsageStreak > 0 ? s.UsageStreak.ToString() : "—";

        TxtFirstUse.Text = s.FirstUse == default ? "—" : s.FirstUse.ToString("yyyy-MM-dd");
        TxtLastOpt.Text = s.LastOptimize == default
            ? UiLanguage.L("尚未优化", "No optimize yet")
            : s.LastOptimize.ToString("yyyy-MM-dd HH:mm");

        TxtAch.Text = BuildAchievement(s);
    }

    private static string BuildAchievement(AppSettings s)
    {
        var ach = new System.Collections.Generic.List<string>();
        if (s.OptimizeCount >= 10) ach.Add("🏅 " + (UiLanguage.Current == Lang.En ? "Optimizer Pro" : "优化达人"));
        if (s.CleanedBytes >= 1024L * 1024 * 1024) ach.Add("🧹 " + (UiLanguage.Current == Lang.En ? "Clean Guardian" : "清洁卫士"));
        if (s.UsageStreak >= 7) ach.Add("🔥 " + (UiLanguage.Current == Lang.En ? "Streak Keeper" : "坚持不懈"));
        if (ach.Count == 0) ach.Add("🌱 " + (UiLanguage.Current == Lang.En ? "Just Getting Started" : "新手上路"));
        return string.Join("   ", ach);
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(Window.GetWindow(this),
            UiLanguage.L("确定要将所有统计与成就清零吗？此操作不可恢复！", "Reset all stats and achievements? This cannot be undone!"),
            UiLanguage.L("确认重置", "Confirm Reset"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        var s = SettingsManager.Current;
        s.OptimizeCount = 0;
        s.CleanedBytes = 0;
        s.UsageStreak = 0;
        s.LastOptimize = default;
        s.LastUsageDate = default;
        s.FirstUse = default;
        SettingsManager.Save();

        try { if (File.Exists(ReportPath)) File.WriteAllText(ReportPath, JsonSerializer.Serialize(new ReportData())); } catch { }

        Refresh();
    }
}
