using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WINHELP
{
    /// <summary>
    /// 单一轮次（非流式）OpenAI 兼容对话补全。供各模块复用（如 N4 智能诊断）。
    /// 读取 AgentSettingsManager 中的 API 地址 / 密钥 / 模型；未配置密钥时返回 null，由调用方降级。
    /// 任何网络 / HTTP 错误都会抛出异常，调用方必须捕获并降级，绝不阻塞主流程。
    /// </summary>
    public static class AiClient
    {
        /// <summary>
        /// 发起一次非流式对话。返回模型文本；若未配置密钥返回 null。
        /// 出错抛出 Exception（含友好提示），调用方需自行 try/catch 降级。
        /// </summary>
        public static async Task<string?> AskAsync(string prompt, string? systemPrompt = null, CancellationToken ct = default)
        {
            var settings = AgentSettingsManager.Current;
            if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey))
                return null;

            var baseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
                ? "https://api.openai.com/v1"
                : settings.ApiBaseUrl.TrimEnd('/');
            // SSRF 防护：地址无效（含云元数据地址）阻断；非回环地址强制 https
            if (SafeUrl.ValidateApiBase(baseUrl) is not string safeUrl)
                throw new Exception("AI 服务地址无效：非 http(s) 或为云元数据服务地址，已阻止连接。");
            baseUrl = safeUrl;
            var endpoint = baseUrl + "/chat/completions";
            var model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model;

            var msgs = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                msgs.Add(new { role = "system", content = systemPrompt });
            msgs.Add(new { role = "user", content = prompt });

            var payload = new
            {
                model = model,
                messages = msgs,
                temperature = 0.3,
                stream = false
            };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                using var cts = HttpClientProvider.Timeout(20); // 保持原 20s 超时语义
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                resp = await HttpClientProvider.Shared.SendAsync(req, linked.Token);
            }
            catch (Exception ex)
            {
                throw new Exception("AI 服务连接失败：" + ex.Message);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                var hint = code switch
                {
                    401 => "（API 密钥无效或缺失）",
                    404 => "（地址可能缺少 /v1 后缀）",
                    400 => "（模型名称可能不匹配）",
                    _ => ""
                };
                throw new Exception($"AI 调用失败 HTTP {code}{hint}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errEl))
            {
                var msg = errEl.ValueKind == JsonValueKind.String ? errEl.GetString()
                    : (errEl.TryGetProperty("message", out var em) ? em.GetString() : null);
                throw new Exception("AI 返回错误：" + (msg ?? "未知错误"));
            }
            var content = root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0
                && ch[0].TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)
                ? c.GetString() : null;
            return string.IsNullOrEmpty(content) ? null : content;
        }
    }
}
