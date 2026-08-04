// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// 自动更新管理器 — 通过 GitHub Tags 检查远程版本，有新版本则通知用户。
    /// v4.9.0：下载源改为双源（GitHub Releases 主 + 蓝奏云备），并内置安装包 SHA-256 常量供校验。
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

        // 下载源（主）：GitHub Releases 最新发布页（P5 会创建 Release 并上传安装包）
        public const string PrimaryDownloadUrl =
            "https://github.com/YYRMMAYO/WINhelper/releases/latest";
        // 下载源（备）：蓝奏云下载页面
        public const string BackupDownloadUrl =
            "https://wwbpq.lanzouu.com/b01d71xtzg";
        // 安装包 SHA-256 校验值：发布流程（P5）计算 dist/BAND/司南工具箱_Setup_v4.9.0.exe 后回填此处
        public const string ReleaseSha256 = "D63B7BFCFD12EC1B654C50CD85A09B84D91EF6B87C19A10562306E120F6B3B55";

        static UpdateManager()
        {
            // GitHub API 要求 User-Agent 头
            HttpClientProvider.Shared.DefaultRequestHeaders.UserAgent.Clear();
            HttpClientProvider.Shared.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("YayuToolbox", LocalVersion));
            HttpClientProvider.Shared.DefaultRequestHeaders.Accept.Clear();
            HttpClientProvider.Shared.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
                using var cts = HttpClientProvider.Timeout(10); // 保持原 10s 超时语义
                var json = await HttpClientProvider.Shared.GetStringAsync(TagsUrl, cts.Token);
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
                        DownloadUrl = PrimaryDownloadUrl,
                        ReleaseNotes = $"发现新版本 {remoteVer}，点击前往 GitHub Releases 下载页面（备选：蓝奏云）。"
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

        /// <summary>用默认浏览器打开下载链接（仅允许可信域名，防配置被篡改后诱导跳转）</summary>
        public static void OpenDownloadUrl(string url)
        {
            SafeUrl.OpenTrusted(url);
        }

        /// <summary>
        /// 校验文件 SHA-256 是否与内置 <see cref="ReleaseSha256"/> 一致。
        /// 返回 true 表示文件与官方发布的安装包一致（防篡改）；ReleaseSha256 未回填时返回 false。
        /// </summary>
        public static bool VerifyFileHash(string path)
        {
            if (string.IsNullOrEmpty(ReleaseSha256) || string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                using var fs = File.OpenRead(path);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(fs);
                var hex = Convert.ToHexString(hash).ToLowerInvariant();
                return hex == ReleaseSha256.ToLowerInvariant();
            }
            catch { return false; }
        }
    }
}
