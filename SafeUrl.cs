// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace WINHELP
{
    /// <summary>
    /// 安全 URL 打开工具 —— 统一收敛全项目 5 份重复的 OpenUrl 实现，并增加两层校验：
    /// 1. <see cref="Open"/>：仅校验 http/https scheme，用于编译期常量的官网链接（约 150 条），
    ///    拦截 file: / ms-settings: / javascript: 等可被用于劫持或逃逸的协议，失败提示与旧版一致。
    /// 2. <see cref="OpenTrusted"/>：在 scheme 校验之上额外校验域名白名单，用于非常量来源
    ///    （更新下载 URL、用户插件 Entry 等），防止被篡改后的配置把用户带往任意站点。
    /// </summary>
    public static class SafeUrl
    {
        /// <summary>
        /// 可信域名白名单（后缀匹配，需带 "." 边界，防 evil-github.com 之类仿冒）。
        /// 目前覆盖：GitHub 更新源、蓝奏云下载页、腾讯文档等官方来源。
        /// </summary>
        public static readonly IReadOnlyList<string> TrustedHosts = new[]
        {
            "github.com",
            "api.github.com",
            "raw.githubusercontent.com",
            "objects.githubusercontent.com",
            "lanzouu.com",
            "lanzou.com",
            "lanzn.com",
            "docs.qq.com",
            "doc.weixin.qq.com"
        };

        /// <summary>判断 URL 是否为合法的 http/https 绝对地址（编译期常量官网通用路径）。</summary>
        public static bool IsHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>判断 host 是否命中可信白名单（带 "." 边界后缀匹配）。</summary>
        public static bool IsTrustedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim().ToLowerInvariant();
            foreach (var trusted in TrustedHosts)
            {
                if (host == trusted) return true;
                if (host.EndsWith("." + trusted, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// 校验 AI API 基地址（SSRF 防护，安全审计建议 P1）：
        /// 1. 仅接受 http/https 绝对地址；
        /// 2. 显式拒绝云元数据服务地址 169.254.169.254（防止 SSRF 窃取云凭据）；
        /// 3. 非本机回环地址（localhost / 127.x / ::1）强制 https，防止 API 令牌经明文 HTTP 外泄。
        /// 返回修正后的地址；地址无效返回 null（调用方应阻断连接）。
        /// </summary>
        public static string? ValidateApiBase(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

            var host = uri.Host.ToLowerInvariant();
            // 拒绝云元数据服务（AWS/GCP/Azure 通用元数据 IP 段）
            if (host == "169.254.169.254" || host.StartsWith("169.254.169.", StringComparison.Ordinal))
                return null;

            var isLoopback = host == "localhost" || host == "127.0.0.1" || host == "::1"
                || host.StartsWith("127.", StringComparison.Ordinal) || host == "[::1]";

            // 非回环地址强制 https
            if (!isLoopback && uri.Scheme == Uri.UriSchemeHttp)
            {
                var ub = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                return ub.Uri.ToString().TrimEnd('/');
            }
            return baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// 用默认浏览器打开链接。仅接受 http/https，其余协议直接拒绝并提示。
        /// 用于编译期常量官网链接，行为与旧版 OpenUrl 等价（失败弹 MessageBox）。
        /// </summary>
        public static bool Open(string url, string? errTitle = null)
        {
            if (!IsHttpUrl(url))
            {
                MessageBox.Show("无法打开链接：仅支持 http/https 网址。", errTitle ?? "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", errTitle ?? "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        /// <summary>
        /// 用默认浏览器打开链接。在 scheme 校验之上，额外要求域名命中白名单。
        /// 用于非常量来源（更新下载 URL、插件 Entry 等），防止被篡改的配置诱导跳转。
        /// </summary>
        public static bool OpenTrusted(string url)
        {
            if (!IsHttpUrl(url) || !IsTrustedHost(new Uri(url).Host))
            {
                MessageBox.Show("无法打开链接：目标地址不在可信域名列表中。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return Open(url, "提示");
        }
    }
}
