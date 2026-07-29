using System;
using System.Collections.Generic;
using System.Linq;

namespace WINHELP
{
    /// <summary>
    /// 版本升级追踪器 — 单例
    /// 每次启动记录当前版本到设置（settings.json 的 VersionHistory 列表），
    /// 升级到新版本时自动追加一条记录，并保留"上一版本"信息，
    /// 从而保证更新覆盖后不遗漏原版本信息。
    /// </summary>
    public static class UpgradeTracker
    {
        private static List<VersionRecord> _history = new();

        /// <summary>版本历史（按安装时间升序）</summary>
        public static IReadOnlyList<VersionRecord> History => _history;

        /// <summary>被本次更新覆盖的原（上一）版本号；非升级场景为 null</summary>
        public static string? PreviousVersion { get; private set; }

        /// <summary>
        /// 在启动时调用：补齐版本历史。
        /// 首次运行写入当前版本；版本变化时追加新记录并记下原版本。
        /// </summary>
        public static void Initialize()
        {
            try
            {
                _history = SettingsManager.Current.VersionHistory ?? new List<VersionRecord>();
                var current = UpdateManager.LocalVersion;

                if (_history.Count == 0)
                {
                    _history.Add(new VersionRecord { Version = current, InstalledAt = DateTime.Now });
                }
                else
                {
                    var last = _history[_history.Count - 1];
                    if (last.Version != current)
                    {
                        // 升级：记录被覆盖的原版本，并追加新版本
                        PreviousVersion = last.Version;
                        _history.Add(new VersionRecord { Version = current, InstalledAt = DateTime.Now });

                        // 仅保留最近 20 条，避免无限增长
                        if (_history.Count > 20)
                            _history = _history.Skip(_history.Count - 20).ToList();
                    }
                    // 版本未变（如重装同版本）则保持原记录
                }

                SettingsManager.Current.VersionHistory = _history;
                SettingsManager.Save();
            }
            catch
            {
                // 记录失败不影响正常使用
            }
        }
    }
}
