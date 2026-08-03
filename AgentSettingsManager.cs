using System.IO;
using System.Security.Cryptography;
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
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WINHELP.AgentSettings.v1");

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
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var data = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch { return ""; } // DPAPI 失败时宁可丢弃密钥也不明文保存
        }

        /// <summary>使用 DPAPI 解密；失败返回空字符串（由调用方回写清空）。</summary>
        private static string Decrypt(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
                {
                var data = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }
}
