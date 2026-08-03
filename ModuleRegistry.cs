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
    /// </summary>
    public class ModuleDefinition
    {
        /// <summary>导航 / 模块 key（如 "clean"）</summary>
        public string Key { get; }
        /// <summary>中文标题</summary>
        public string TitleZh { get; }
        /// <summary>英文标题</summary>
        public string TitleEn { get; }
        /// <summary>图标（emoji），用于首页卡片与命令面板</summary>
        public string Icon { get; }
        /// <summary>首页所属分组：system / tools / assist；null 表示不在首页显示（如 home/settings/theme/companion）</summary>
        public string? HomeGroup { get; }
        /// <summary>是否为主级卡片（更大尺寸 + 图标底盘）</summary>
        public bool IsPrimary { get; }
        /// <summary>首页卡片副标题（中文）</summary>
        public string SubtitleZh { get; }
        /// <summary>首页卡片副标题（英文）</summary>
        public string SubtitleEn { get; }

        public ModuleDefinition(string key, string titleZh, string titleEn, string icon,
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
    /// </summary>
    public static class ModuleRegistry
    {
        public static readonly IReadOnlyList<ModuleDefinition> All = new ModuleDefinition[]
        {
            // ===== 系统工具（首页主级卡片） =====
            new("clean",    "系统清理",   "System Cleaner",        "🧹", "system", true,  "垃圾 / 大文件 / 磁盘可视化", "Junk / large files / disk treemap"),
            new("startup",  "启动项",     "Startup",               "🚀", "system", true,  "禁用开机自启 · 影响评估",     "Disable autostart · impact check"),
            new("system",   "系统状况",   "System Status",         "💻", "system", true,  "设备检测 · 进程 · 诊断",      "Device · processes · smart diagnosis"),
            new("net",      "网络诊断",   "Network Diagnostics",   "📡", "system", true,  "连通性检测与测速",           "Connectivity test & speed"),
            new("issue",    "问题解决",   "Issue Solver",          "🩺", "system", true,  "常见故障速查 · 一键修复",     "Common issues & one-click fix"),

            // ===== 效率工具（首页常规卡片） =====
            new("wizard",   "故障向导",   "Troubleshoot Wizard",   "🔧", "tools",  false, "向导式排查常见问题",          "Step-by-step troubleshooting"),
            new("shred",    "文件粉碎",   "File Shredder",         "🗜️", "tools",  false, "安全彻底删除敏感文件",        "Securely delete sensitive files"),
            new("snapshot", "截图标注",   "Screenshot",            "📷", "tools",  false, "截图并标注编辑",              "Capture & annotate"),
            new("uninstall", "卸载残留",  "Uninstall Leftovers",   "🧨", "tools",  false, "清理软件卸载后的残留",        "Clean up leftover files after uninstall"),
            new("notes",    "便签",       "Notes",                 "📝", "tools",  false, "桌面便签快速记录",            "Quick desktop notes"),
            new("recorder", "录音录像",   "Recorder",              "🎙️", "tools",  false, "麦克风录音与屏幕录像",        "Mic recording & screen capture"),

            // ===== 助手与信息（首页常规卡片） =====
            new("agent",    "Agent 助手", "Agent Assistant",       "🤖", "assist", false, "接入 API 获取 AI 帮助",       "Connect API for AI help"),
            new("site",     "网站与官网", "Sites & Official",      "🌐", "assist", false, "常用网站 + 软件官网",         "Common sites & official links"),
            new("tool",     "WIN 助手",   "WIN Helper",            "🛠️", "assist", false, "实用软件官方下载",            "Official downloads"),
            new("help",     "电脑帮助",   "PC Help",               "💻", "assist", false, "系统工具与使用技巧",          "Tools & tips"),
            new("report",   "月度报告",   "Monthly Report",        "📊", "assist", false, "使用统计与成就",              "Usage stats & achievements"),
            new("novice",   "新手导览",   "Beginner Guide",        "📘", "assist", false, "小白也能懂的功能",            "Features for beginners"),
            new("tutorial", "AI 密钥教程", "AI Key Tutorial",      "🔑", "assist", false, "申请并填入 AI 密钥",          "Get & enter your AI key"),
            new("bug",      "BUG 反馈",   "Bug Report",            "🐞", "assist", false, "问题反馈与建议提交",          "Report issues & suggestions"),
            new("setup",    "装机助手",   "Setup Assistant",       "💿", "assist", false, "常用软件安装推荐",            "Recommended software installer"),

            // ===== 仅导航 / 侧栏入口（不在首页显示） =====
            new("home",      "主界面",     "Home",                  "🏠"),
            new("settings",  "软件设置",   "Settings",              "⚙️"),
            new("theme",     "个性装扮",   "Appearance",            "🎨"),
            new("companion", "陪伴运行",   "Companion",             "🐾"),
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
                // —— 效率工具 ——
                case "shred":    return new WindowShredder();
                case "snapshot": return new WindowSnapshot();
                case "uninstall": return new WindowUninstaller();
                case "notes":    return new NotesPage();
                case "recorder": return new WindowRecorder();
                case "wizard":   return new TroubleshootWizardPage { OnNavigate = host.Navigate };
                // —— 助手与信息 ——
                case "site":     return new SiteFinderPage();
                case "tool":     return new WinHelperPage();
                case "help":     return new PcHelpPage();
                case "agent":    return new AgentAssistantPage { OnCloseRequest = host.CloseToHome, OnOpenTutorial = host.OpenTutorial };
                case "report":   return new WindowReport();
                case "novice":   return new BeginnerGuidePage();
                case "tutorial": return new WindowTutorial { OnCloseRequest = host.CloseToHome, OnOpenAgent = host.OpenAgent };
                case "bug":      return new BugReportPage();
                case "setup":    return new SetupPage();
                // —— 设置 / 装扮 / 陪伴 / 首页 ——
                case "settings":   return new SettingsPage { OnCloseRequest = host.CloseToHome };
                case "theme":      return new AppearancePage { OnCloseRequest = host.CloseToHome };
                case "companion":  return new CompanionPage();
                case "home":
                    return new HomePage { OnNavigate = host.Navigate, OnOptimize = host.Optimize };
                default:
                    return new HomePage { OnNavigate = host.Navigate, OnOptimize = host.Optimize };
            }
        }
    }
}
