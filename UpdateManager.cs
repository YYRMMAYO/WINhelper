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
        // 安装包 SHA-256 参考值：发布流程（P5）计算最终安装包后回填。
        // 注：应用内下载更新不再依赖此常量校验（跨版本更新时无法预知未来安装包的哈希），
        // 改为运行时读取 GitHub Release body 中声明的 SHA-256（见 DownloadLatestAsync）；
        // 此常量保留供手动核验 / 展示用途。
        public const string ReleaseSha256 = "B1D9EBB9F756B0A4EA47D149A39C2E3D0939825E2FE75606D3A86537ECE0ACD4";

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
        /// 从 GitHub Tags API 获取全部 tag，解析版本号取**最高**者作为远端版本
        /// （不依赖 API 返回顺序，v5.1.0 修复：tags 数组顺序不保证按版本排列），
        /// 与本地版本号比较，远端更新则触发 UpdateAvailable 事件。
        /// </summary>
        public static async Task CheckAsync()
        {
            try
            {
                using var cts = HttpClientProvider.Timeout(10); // 保持原 10s 超时语义
                var json = await HttpClientProvider.Shared.GetStringAsync(TagsUrl, cts.Token);
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    return; // 仓库还没有 tag

                // 遍历全部 tag，解析版本号并取最大值（忽略非 x.y.z 格式的 tag）
                string remoteVer = "";
                Version? remoteMax = null;
                foreach (var tag in root.EnumerateArray())
                {
                    var name = tag.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(name)) continue;
                    var candidate = name.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? name[1..] : name;
                    if (!Version.TryParse(candidate, out var v)) continue;
                    if (remoteMax == null || v > remoteMax) { remoteMax = v; remoteVer = candidate; }
                }
                if (remoteMax == null) return;

                // 版本号比较
                if (!Version.TryParse(LocalVersion, out var local)) return;

                if (remoteMax > local)
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

        // ===== v5.2.0：从 GitHub 直接下载最新 tag 的 exe 并安装 =====

        /// <summary>最新 Release 资产信息（版本 / 文件名 / 下载地址 / 官方 SHA-256）。</summary>
        public sealed class ReleaseAsset
        {
            public string Version { get; init; } = "";
            public string FileName { get; init; } = "";
            public string DownloadUrl { get; init; } = "";
            /// <summary>发布者在 Release body 中声明的 SHA-256（无则视为发布流程不完整，禁止自动安装）。</summary>
            public string Sha256 { get; init; } = "";
        }

        /// <summary>从 Release body 中提取官方 SHA-256（支持 "SHA256: xxx" / "SHA-256: xxx" / "sha256=xxx" 等常见写法）。</summary>
        private static string ExtractSha256(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(body,
                @"SHA-?256[\s:=：]+\s*([0-9a-fA-F]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (m is System.Text.RegularExpressions.Match mm && mm.Success)
                    return mm.Groups[1].Value.ToLowerInvariant();
            }
            return "";
        }

        /// <summary>
        /// 使用与 <see cref="CheckAsync"/> 相同的逻辑（遍历 tags 取最高版本），
        /// 再通过 GitHub API 查询该 tag 对应的 Release，返回其中第一个 .exe 资产
        /// 及其在 Release body 中声明的 SHA-256。查询失败返回 null。
        /// </summary>
        public static async Task<ReleaseAsset?> GetLatestReleaseExeAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = HttpClientProvider.Timeout(10);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);

                // 1) 遍历 tags 取最高版本（与 CheckAsync 完全相同的解析逻辑）
                var tagsJson = await HttpClientProvider.Shared.GetStringAsync(TagsUrl, linked.Token);
                using (var doc = JsonDocument.Parse(tagsJson))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                        return null;

                    Version? remoteMax = null;
                    string remoteVer = "";
                    foreach (var tag in root.EnumerateArray())
                    {
                        var name = tag.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(name)) continue;
                        var candidate = name.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                            ? name[1..] : name;
                        if (!Version.TryParse(candidate, out var v)) continue;
                        if (remoteMax == null || v > remoteMax) { remoteMax = v; remoteVer = candidate; }
                    }
                    if (remoteMax == null) return null;

                    // 2) 查询该 tag 的 Release（资产 + body 中声明的官方 SHA-256）
                    var releaseUrl = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/tags/v{remoteVer}";
                    var relJson = await HttpClientProvider.Shared.GetStringAsync(releaseUrl, linked.Token);
                    using var relDoc = JsonDocument.Parse(relJson);
                    var relRoot = relDoc.RootElement;

                    string bodySha = "";
                    if (relRoot.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String)
                        bodySha = ExtractSha256(bodyEl.GetString() ?? "");

                    if (relRoot.TryGetProperty("assets", out var assets)
                        && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in assets.EnumerateArray())
                        {
                            if (!a.TryGetProperty("name", out var nameEl)) continue;
                            string name = nameEl.GetString() ?? "";
                            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            string url = a.TryGetProperty("browser_download_url", out var urlEl)
                                ? urlEl.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(url)) continue;
                            return new ReleaseAsset
                            {
                                Version = remoteVer,
                                FileName = name,
                                DownloadUrl = url,
                                Sha256 = bodySha
                            };
                        }
                    }
                }
                return null;
            }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// 从 GitHub 下载最新 tag 的安装包到临时目录，下载完成后用「Release body 中声明的官方 SHA-256」校验。
        /// <para>
        /// 校验设计（v5.2.0）：不依赖编译期常量（常量无法预知未来版本的哈希，跨版本校验必然失效），
        /// 改为运行时读取 GitHub Release body 中的 SHA-256 —— 发布流程（P5）上传资产时会把哈希写入 Release 说明。
        /// 发布方未声明哈希 / 校验失败一律返回 null 并删除文件，绝不允许“无校验直接安装”。
        /// </para>
        /// </summary>
        /// <param name="progress">下载进度回调 (已下载字节, 总字节)。</param>
        /// <param name="ct">取消令牌。</param>
        public static async Task<string?> DownloadLatestAsync(
            IProgress<(long Read, long Total)>? progress = null,
            CancellationToken ct = default)
        {
            var asset = await GetLatestReleaseExeAsync(ct);
            if (asset == null || string.IsNullOrEmpty(asset.Sha256)) return null; // 发布未声明哈希 → 拒绝

            string tmp = Path.Combine(Path.GetTempPath(), asset.FileName);
            try
            {
                using var cts = HttpClientProvider.Timeout(600); // 大文件放宽到 10 分钟
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);

                using var resp = await HttpClientProvider.Shared.GetAsync(
                    asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                resp.EnsureSuccessStatusCode();

                long total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(linked.Token);
                await using var dst = File.Create(tmp);
                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf, linked.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), linked.Token);
                    read += n;
                    progress?.Report((read, total));
                }
                await dst.FlushAsync(linked.Token);
            }
            catch
            {
                TryDeleteFile(tmp);
                return null;
            }

            // SHA-256 防篡改校验（以 Release body 声明的官方哈希为准）
            if (!VerifyFileHash(tmp, asset.Sha256))
            {
                TryDeleteFile(tmp);
                return null;
            }
            return tmp;
        }

        /// <summary>按指定期望值校验文件 SHA-256（十六进制小写比较）。</summary>
        public static bool VerifyFileHash(string path, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256) || string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                using var fs = File.OpenRead(path);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(fs);
                var hex = Convert.ToHexString(hash).ToLowerInvariant();
                return hex == expectedSha256.ToLowerInvariant();
            }
            catch { return false; }
        }

        /// <summary>用系统默认方式启动安装包（Inno Setup 会自行请求管理员权限）。</summary>
        public static bool LaunchInstaller(string installerPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
