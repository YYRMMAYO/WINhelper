using System;
using System.IO;
using System.Threading;

namespace WINHELP
{
    /// <summary>
    /// 定时计划管理器（N5）：在 App 运行期间按用户设定的频率与时间，
    /// 自动执行一键优化（清理临时文件 + 清空回收站）。
    ///
    /// 注意：App.xaml.cs 必须在启动时调用一次 SchedulerManager.Start()，
    /// 并在程序退出时调用 SchedulerManager.Stop()。
    /// 本类仅读取 SettingsManager 现有字段，并调用 Cleaner 的静态方法。
    /// </summary>
    public static class SchedulerManager
    {
        // 计时器：每分钟检查一次
        private static Timer? _timer;

        // 重入保护
        private static readonly object _lock = new object();
        private static bool _running = false;

        // 上次执行日期（静态字段 + 文件持久化，避免同日内重复执行 / 重启后重复执行）
        private static DateTime _lastRunDate = DateTime.MinValue;

        private static readonly string LastRunFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "WINHELP", "scheduler.lastrun");

        /// <summary>启动定时检查。重复调用是安全的（只会创建一次计时器）。</summary>
        public static void Start()
        {
            LoadLastRun();
            if (_timer != null) return;

            // 30 秒后首次触发，之后每分钟触发一次
            _timer = new Timer(Tick, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
        }

        /// <summary>停止定时检查并释放计时器资源。</summary>
        public static void Stop()
        {
            var t = _timer;
            _timer = null;
            t?.Dispose();
        }

        private static void LoadLastRun()
        {
            try
            {
                if (File.Exists(LastRunFilePath) &&
                    DateTime.TryParse(File.ReadAllText(LastRunFilePath).Trim(), out var d))
                {
                    _lastRunDate = d.Date;
                }
            }
            catch
            {
                // 忽略读取错误，退化为从未执行
            }
        }

        private static void SaveLastRun(DateTime date)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LastRunFilePath)!);
                File.WriteAllText(LastRunFilePath, date.ToString("yyyy-MM-dd"));
            }
            catch
            {
                // 忽略写入错误
            }
        }

        /// <summary>计时器回调：检查是否到了计划执行时间。</summary>
        private static void Tick(object? state)
        {
            // 防止上一次尚未执行完时重入
            if (_running) return;
            lock (_lock)
            {
                if (_running) return;
                _running = true;
            }

            try
            {
                var s = SettingsManager.Current;
                if (!s.SchedulerEnabled) return;

                // 解析时间（HH:mm），无效则跳过
                if (!TimeSpan.TryParse(s.SchedulerTime, out var target)) return;

                var now = DateTime.Now;
                if (now.Hour != target.Hours || now.Minute != target.Minutes) return;

                // 星期匹配：SchedulerDayOfWeek == -1 表示每天；否则 0=周一 … 6=周日
                if (s.SchedulerDayOfWeek != -1)
                {
                    int today = ((int)now.DayOfWeek + 6) % 7; // 把周日(0)映射到 6，周一映射到 0
                    if (today != s.SchedulerDayOfWeek) return;
                }

                // 今天已经执行过则不再执行
                if (_lastRunDate == now.Date) return;

                _lastRunDate = now.Date;
                SaveLastRun(now.Date);
                RunOptimize();
            }
            catch
            {
                // 永远不要从计时器回调抛出异常
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>执行一次计划优化（创建还原点 + 一键优化）。全程异常安全。</summary>
        private static void RunOptimize()
        {
            try
            {
                if (SettingsManager.Current.RestorePointEnabled)
                {
                    try
                    {
                        Cleaner.CreateSystemRestorePoint("WINHELP 定时优化");
                    }
                    catch
                    {
                        // 创建还原点失败不影响后续清理
                    }
                }

                // 一键优化：清理临时文件 + 清空回收站，返回释放的字节数。
                // 无人值守运行：对超过 200MB 的大文件/目录做保护（不自动删除，避免误删），
                // 回收站仍按原计划清空（属用户主动设定的计划任务）。
                long freed = Cleaner.OneClickOptimize(200L * 1024 * 1024);
                System.Diagnostics.Debug.WriteLine($"[SchedulerManager] 定时优化完成，释放 {freed} 字节。");
            }
            catch
            {
                // 永远不要从计时器回调抛出异常
            }
        }
    }
}
