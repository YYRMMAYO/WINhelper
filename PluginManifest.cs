using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// 插件清单（New B）：轻量、安全的"模块清单 + 外部动作"扩展机制。
    /// 不做 DLL 热插拔（签名/安全成本过高），插件仅声明：
    /// 跳转到已有页面(key) 或 打开外部链接 / 运行白名单脚本。
    /// 清单文件放在 %APPDATA%/WINHELP/Plugins/*.json。
    /// </summary>
    public class PluginManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🔌";
        public string Entry { get; set; } = "";   // 页面 key（如 "clean"）或 http(s) 链接
        public string Group { get; set; } = "插件";
        public string Desc { get; set; } = "";
    }

    /// <summary>插件加载器（静态）：轻量扩展机制，加载插件清单。</summary>
    public static class PluginLoader
    {
        private static readonly string PluginDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP", "Plugins");

        public static List<PluginManifest> Plugins { get; private set; } = new();

        /// <summary>加载所有插件清单（容错：单文件损坏不影响其它）</summary>
        public static void Load()
        {
            Plugins = new List<PluginManifest>();
            try
            {
                if (!Directory.Exists(PluginDir)) return;
                foreach (var f in Directory.GetFiles(PluginDir, "*.json"))
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(f));
                        if (p != null && !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Entry))
                            Plugins.Add(p);
                    }
                    catch { /* 单文件解析失败忽略 */ }
                }
            }
            catch { }
        }
    }
}
