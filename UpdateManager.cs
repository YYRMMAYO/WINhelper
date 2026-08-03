using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// 自动更新管理器 — 通过 GitHub Tags 检查远程版本，有新版本则通知用户
    /// </summary>
    public static class UpdateManager
    {
        // ===== 配置区域 =====
        // GitHub 仓库信息
        private const string GITHUB_OWNER = "YYRMMAYO";
        private const string GITHUB_REPO = "WINhelper";

        // GitHub API：获取所有 tags
        private static readonly string TagsUrl =
            $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/tags";

        // 下载页：蓝奏云下载地址
        private static readonly string ReleasesUrl =
            "https://wwbpq.lanzouu.com/b01d71xtzg";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static UpdateManager()
        {
            // GitHub API 要求 User-Agent 头
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("YayuToolbox", LocalVersion));
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        /// <summary>检查到新版本时触发</summary>
        public static event Action<UpdateInfo>? UpdateAvailable;

        /// <summary>更新信息</summary>
        public class UpdateInfo
        {
            public string RemoteVersion { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
        }

        /// <summary>
        /// 版本覆盖值 — 由 SiteFinderPage 的"软件版本"文字模块解析后设置。
        /// 设置后 LocalVersion 将返回此值，而非程序集版本。
        /// 这是版本检测的检测路径：SiteFinderPage → 软件更新 → 软件版本文字模块。
        /// </summary>
        public static string? VersionOverride { get; set; }

        /// <summary>获取本地当前版本号</summary>
        public static string LocalVersion
        {
            get
            {
                // 优先使用从 SiteFinderPage 软件版本文字模块解析的版本号
                if (!string.IsNullOrWhiteSpace(VersionOverride))
                    return VersionOverride;

                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver == null) return "1.0.0";
                // 去掉末尾的 .0（如 1.3.1.0 → 1.3.1）
                if (ver.Revision == 0 && ver.Build >= 0)
                    return $"{ver.Major}.{ver.Minor}.{ver.Build}";
                return ver.ToString();
            }
        }

        /// <summary>获取完整版本标识（仅版本号，不含构建时间）</summary>
        public static string FullVersion => $"v{LocalVersion}";

        /// <summary>
        /// 异步检查更新
        /// 从 GitHub Tags API 获取最新 tag 名称作为远端版本号，
        /// 与本地版本号比较，远端更新则触发 UpdateAvailable 事件。
        /// </summary>
        public static async Task CheckAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(TagsUrl);
                using var doc = JsonDocument.Parse(json);

                // tags 是一个数组，第一个就是最新的 tag
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    return; // 仓库还没有 tag

                var latest = root[0];
                var tagName = latest.TryGetProperty("name", out var n)
                    ? n.GetString() ?? ""
                    : "";

                if (string.IsNullOrEmpty(tagName)) return;

                // 去掉 tag 前面的 "v" 前缀（如 v1.3.2 → 1.3.2）
                var remoteVer = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tagName[1..]
                    : tagName;

                // 版本号比较
                if (!Version.TryParse(LocalVersion, out var local)) return;
                if (!Version.TryParse(remoteVer, out var remote)) return;

                if (remote > local)
                {
                    UpdateAvailable?.Invoke(new UpdateInfo
                    {
                        RemoteVersion = remoteVer,
                        DownloadUrl = ReleasesUrl,
                        ReleaseNotes = $"发现新版本 {remoteVer}，点击前往蓝奏云下载页面。"
                    });
                }
            }
            catch (HttpRequestException)
            {
                // 网络不通 → 静默跳过
            }
            catch (TaskCanceledException)
            {
                // 超时 → 静默跳过
            }
            catch (JsonException)
            {
                // API 返回格式异常（如被限流）→ 静默跳过
            }
        }

        /// <summary>用默认浏览器打开下载链接</summary>
        public static void OpenDownloadUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
