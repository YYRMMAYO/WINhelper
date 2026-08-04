// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System.IO;
using System.Text;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// Agent 助手 — 用户自定义接入的网络 API 设置（OpenAI 兼容 Chat Completions 协议）
    /// </summary>
    public class AgentSettings
    {
        /// <summary>API 基地址，例如 https://api.openai.com/v1 或 http://localhost:11434/v1</summary>
        public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

        /// <summary>API 密钥（Bearer Token）。本地模型通常留空</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>模型名称，例如 gpt-4o-mini / deepseek-chat / qwen-max 等</summary>
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>采样温度 0~2</summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>系统提示词（人设）</summary>
        public string SystemPrompt { get; set; }
            = "你是 司南工具箱 内置的 AI 助手，擅长帮助用户解决 Windows 电脑使用、软件下载与常见故障排查问题。回答简洁、准确、友好。";
    }

    /// <summary>
    /// Agent 设置管理器 — 单例，持久化到 AppData/WINHELP/agent.json
    /// API 密钥使用 Windows DPAPI（受当前用户保护）加密后落盘，避免明文泄露（安全审计建议 P1）。
    /// </summary>
    public static class AgentSettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "agent.json");

        // DPAPI 附加熵：即使其它进程能读取同一用户的数据，没有此熵也无法解密
        // 注意：此值严禁改动 —— 改动会导致老用户已保存的 API 密钥无法解密（解密失败被清空）
        private const string EntropyTag = "WINHELP.AgentSettings.v1";

        public static AgentSettings Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var s = JsonSerializer.Deserialize<AgentSettings>(json);
                    if (s != null)
                    {
                        // 解密 API 密钥；解密失败（如配置被篡改或其它用户环境）则置空并回写
                        s.ApiKey = Decrypt(s.ApiKey);
                        Current = s;
                    }
                }
            }
            catch { /* 加载失败用默认值 */ }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                // 序列化前加密，落盘时密钥非明文
                var copy = new AgentSettings
                {
                    ApiBaseUrl = Current.ApiBaseUrl,
                    ApiKey = Encrypt(Current.ApiKey),
                    Model = Current.Model,
                    Temperature = Current.Temperature,
                    SystemPrompt = Current.SystemPrompt
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(copy,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 保存失败静默忽略 */ }
        }

        /// <summary>使用 DPAPI 加密（仅当前 Windows 用户可解密）。空值原样返回。</summary>
        private static string Encrypt(string plain)
            => DpapiProtector.Encrypt(plain, EntropyTag);

        /// <summary>使用 DPAPI 解密；失败返回空字符串（由调用方回写清空）。</summary>
        private static string Decrypt(string cipher)
            => DpapiProtector.Decrypt(cipher, EntropyTag);
    }
}
