// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace WINHELP
{
    /// <summary>
    /// 导航宿主接口：供模块工厂在创建页面时注入导航 / 关闭 / 优化等行为，
    /// 避免在静态注册表中直接依赖 MainWindow 实例（解耦，便于统一维护）。
    /// </summary>
    public interface INavigationHost
    {
        /// <summary>按页面 key 导航（供 HomePage / SystemStatusPage 等调用）</summary>
        void Navigate(string key);
        /// <summary>触发顶部「一键优化」</summary>
        void Optimize();
        /// <summary>请求返回首页</summary>
        void CloseToHome();
        /// <summary>打开 AI 密钥教程页</summary>
        void OpenTutorial();
        /// <summary>打开 Agent 助手页</summary>
        void OpenAgent();
    }

    /// <summary>
    /// 单个功能模块的定义（导航栏、首页卡片、命令面板共用）。
    /// 把原先散落在 MainWindow.InitPages（_titles / _factories）、
    /// HomePage.xaml（卡片 Border）与 BuildCommandItems（图标映射）里的模块元数据，
    /// 集中为 C# 实例，便于人工增删改模块。
    /// <para>v5.3.0：图标字段不再承载 emoji（UI 全面去表情化），
    /// 统一由 UI 层按模块标题首字生成「首字徽标」；Icon 保留为空串占位。</para>
    /// </summary>
    public class ModuleDefinition
    {
        /// <summary>导航 / 模块 key（如 "clean"）</summary>
        public string Key { get; }
        /// <summary>中文标题</summary>
        public string TitleZh { get; }
        /// <summary>英文标题</summary>
        public string TitleEn { get; }
        /// <summary>图标（已弃用，v5.3.0 起恒为空，UI 使用首字徽标）</summary>
        public string Icon { get; }
        /// <summary>首页所属分组：system / tools / assist；null 表示不在首页显示（如 home/settings/theme/companion/tutorial）</summary>
        public string? HomeGroup { get; }
        /// <summary>是否为主级卡片（更大尺寸 + 首字徽标底盘）</summary>
        public bool IsPrimary { get; }
        /// <summary>首页卡片副标题（中文）</summary>
        public string SubtitleZh { get; }
        /// <summary>首页卡片副标题（英文）</summary>
        public string SubtitleEn { get; }

        public ModuleDefinition(string key, string titleZh, string titleEn, string icon = "",
            string? homeGroup = null, bool isPrimary = false,
            string? subtitleZh = null, string? subtitleEn = null)
        {
            Key = key;
            TitleZh = titleZh;
            TitleEn = titleEn;
            Icon = icon;
            HomeGroup = homeGroup;
            IsPrimary = isPrimary;
            SubtitleZh = subtitleZh ?? "";
            SubtitleEn = subtitleEn ?? "";
        }
    }

    /// <summary>
    /// 全部功能模块注册表（C# 实例数据）。
    /// <para>新增 / 调整模块只需编辑此文件：补充一条 ModuleDefinition，
    /// 并在 CreatePage 的 switch 中加上对应页面实例化分支即可。</para>
    /// <para>v5.3.0：已删除冗余模块 wizard（故障向导，并入 issue 问题解决）与
    /// novice（新手导览，内容并入 help 电脑帮助）；tutorial 仅作内部跳转入口（不出现在首页）。</para>
    /// </summary>
    public static class ModuleRegistry
    {
        public static readonly IReadOnlyList<ModuleDefinition> All = new ModuleDefinition[]
        {
            // ===== 系统工具（首页主级卡片） =====
            new("clean",    "系统清理",   "System Cleaner",        homeGroup: "system", isPrimary: true,  subtitleZh: "垃圾 / 大文件 / 磁盘可视化", subtitleEn: "Junk / large files / disk treemap"),
            new("startup",  "启动项",     "Startup",               homeGroup: "system", isPrimary: true,  subtitleZh: "禁用开机自启 · 影响评估",     subtitleEn: "Disable autostart · impact check"),
            new("system",   "系统状况",   "System Status",         homeGroup: "system", isPrimary: true,  subtitleZh: "设备检测 · 进程 · 诊断",      subtitleEn: "Device · processes · smart diagnosis"),
            new("net",      "网络诊断",   "Network Diagnostics",   homeGroup: "system", isPrimary: true,  subtitleZh: "连通性检测与测速",           subtitleEn: "Connectivity test & speed"),
            new("issue",    "问题解决",   "Issue Solver",          homeGroup: "system", isPrimary: true,  subtitleZh: "常见故障速查 · 一键修复",     subtitleEn: "Common issues & one-click fix"),
            new("rescue",   "系统急救",   "System Rescue",         homeGroup: "system", isPrimary: true,  subtitleZh: "蓝屏 · 电池 · 端口 · 驱动备份", subtitleEn: "BSOD / battery / ports / driver backup"),

            // ===== 效率工具（首页常规卡片） =====
            new("shred",    "文件粉碎",   "File Shredder",         homeGroup: "tools", subtitleZh: "安全彻底删除敏感文件",        subtitleEn: "Securely delete sensitive files"),
            new("snapshot", "截图标注",   "Screenshot",            homeGroup: "tools", subtitleZh: "截图并标注编辑",              subtitleEn: "Capture & annotate"),
            new("uninstall", "卸载残留",  "Uninstall Leftovers",   homeGroup: "tools", subtitleZh: "清理软件卸载后的残留",        subtitleEn: "Clean up leftover files after uninstall"),
            new("duplicate", "重复文件",  "Duplicate Files",       homeGroup: "tools", subtitleZh: "查找并清理重复大文件（入回收站）", subtitleEn: "Find & remove duplicate files (to recycle bin)"),
            new("notes",    "便签",       "Notes",                 homeGroup: "tools", subtitleZh: "桌面便签快速记录",            subtitleEn: "Quick desktop notes"),
            new("recorder", "录音录像",   "Recorder",              homeGroup: "tools", subtitleZh: "麦克风录音与屏幕录像",        subtitleEn: "Mic recording & screen capture"),
            new("tweak",    "个性化调校", "Windows Tweaks",        homeGroup: "tools", subtitleZh: "任务栏 · 右键菜单 · Hosts",   subtitleEn: "Taskbar / context menu / hosts"),
            new("checkup",  "一键体检",   "PC Checkup",            homeGroup: "tools", subtitleZh: "生成可导出体检报告",          subtitleEn: "Generate exportable health report"),

            // ===== 助手与信息（首页常规卡片） =====
            new("agent",    "Agent 助手", "Agent Assistant",       homeGroup: "assist", subtitleZh: "接入 API 获取 AI 帮助",       subtitleEn: "Connect API for AI help"),
            new("site",     "网站与官网", "Sites & Official",      homeGroup: "assist", subtitleZh: "常用网站 + 软件官网",         subtitleEn: "Common sites & official links"),
            new("tool",     "WIN 助手",   "WIN Helper",            homeGroup: "assist", subtitleZh: "实用软件官方下载",            subtitleEn: "Official downloads"),
            new("help",     "电脑帮助",   "PC Help",               homeGroup: "assist", subtitleZh: "系统工具与使用技巧",          subtitleEn: "Tools & tips"),
            new("report",   "月度报告",   "Monthly Report",        homeGroup: "assist", subtitleZh: "使用统计与成就",              subtitleEn: "Usage stats & achievements"),
            new("bug",      "BUG 反馈",   "Bug Report",            homeGroup: "assist", subtitleZh: "问题反馈与建议提交",          subtitleEn: "Report issues & suggestions"),
            new("setup",    "装机助手",   "Setup Assistant",       homeGroup: "assist", subtitleZh: "常用软件安装推荐",            subtitleEn: "Recommended software installer"),
            new("protool",  "专业工具",   "Pro Tools",             homeGroup: "assist", subtitleZh: "绿色免安装专业工具官方下载",  subtitleEn: "Portable pro tools official downloads"),

            // ===== 仅导航 / 侧栏入口（不在首页显示） =====
            new("home",      "主界面",     "Home"),
            new("settings",  "软件设置",   "Settings"),
            new("theme",     "个性装扮",   "Appearance"),
            new("companion", "陪伴运行",   "Companion"),
            // tutorial：内部跳转入口（Agent 助手页「密钥教程」按钮），不显示在首页与侧栏
            new("tutorial",  "AI 密钥教程", "AI Key Tutorial"),
        };

        /// <summary>按 key 查找模块定义；不存在返回 null。</summary>
        public static ModuleDefinition? Find(string key)
        {
            foreach (var m in All)
                if (m.Key == key) return m;
            return null;
        }

        /// <summary>
        /// 按 key 创建页面实例（含需要导航宿主的特殊接线）。
        /// 注意：需要 OnNavigate / OnCloseRequest 等回调的页面，必须通过 host 注入，
        /// 因为注册表是静态的、无法直接引用 MainWindow 实例。
        /// </summary>
        public static UserControl CreatePage(string key, INavigationHost host)
        {
            switch (key)
            {
                // —— 系统工具 ——
                case "clean":    return new SystemCleanerPage();
                case "startup":  return new StartupPage();
                case "net":      return new NetworkDiagnosticsPage();
                case "issue":    return new IssueSolverPage();
                case "system":   return new SystemStatusPage { OnNavigate = host.Navigate };
                case "rescue":   return new RescuePage();
                // —— 效率工具 ——
                case "shred":    return new WindowShredder();
                case "snapshot": return new WindowSnapshot();
                case "uninstall": return new WindowUninstaller();
                case "duplicate": return new DuplicateFilePage();
                case "notes":    return new NotesPage();
                case "recorder": return new WindowRecorder();
                case "tweak":    return new TweakPage();
                case "checkup":  return new CheckupPage();
                // —— 助手与信息 ——
                case "site":     return new SiteFinderPage();
                case "tool":     return new WinHelperPage();
                case "help":     return new PcHelpPage();
                case "agent":    return new AgentAssistantPage { OnCloseRequest = host.CloseToHome, OnOpenTutorial = host.OpenTutorial };
                case "report":   return new WindowReport();
                case "bug":      return new BugReportPage();
                case "setup":    return new SetupPage();
                case "protool":  return new ProToolPage { OnNavigate = host.Navigate };
                // —— 设置 / 装扮 / 陪伴 / 教程 / 首页 ——
                case "settings":   return new SettingsPage { OnCloseRequest = host.CloseToHome };
                case "theme":      return new AppearancePage { OnCloseRequest = host.CloseToHome };
                case "companion":  return new CompanionPage();
                case "tutorial":   return new WindowTutorial { OnCloseRequest = host.CloseToHome, OnOpenAgent = host.OpenAgent };
                case "home":
                    return new HomePage { OnNavigate = host.Navigate, OnOptimize = host.Optimize };
                default:
                    return new HomePage { OnNavigate = host.Navigate, OnOptimize = host.Optimize };
            }
        }
    }
}
