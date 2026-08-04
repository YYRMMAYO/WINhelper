// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Net.Http;

namespace WINHELP
{
    /// <summary>
    /// 共享 HttpClient 提供者 —— 收敛全项目多处各自 new 的 HttpClient 实例（安全审计建议 P3）。
    /// 统一使用带连接池（5 分钟复用）的 SocketsHttpHandler + 单例 HttpClient，
    /// 避免 TCP 连接与 socket 耗尽；HttpClient 本身 Timeout 设为 InfiniteTimeSpan，
    /// 各调用点必须用 per-request CancellationTokenSource 保持各自原有的超时语义
    /// （UpdateManager 10s / Agent 180s / AiClient 120s / 体检 20s / 诊断 8s / 状态 5s）。
    /// </summary>
    public static class HttpClientProvider
    {
        private static readonly Lazy<HttpClient> Lazy = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 8,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            // 默认 UA：GitHub API 强制要求 User-Agent 头，且不能依赖调用方的初始化顺序
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("WINHELP");
            return client;
        });

        /// <summary>全局共享 HttpClient 实例（线程安全）。超时请使用 per-request CTS。</summary>
        public static HttpClient Shared => Lazy.Value;

        /// <summary>创建带指定超时的 CancellationTokenSource 便捷方法。</summary>
        public static System.Threading.CancellationTokenSource Timeout(int seconds)
            => new(TimeSpan.FromSeconds(seconds));
    }
}
