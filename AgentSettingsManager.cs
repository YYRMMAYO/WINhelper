using System.IO;
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
    /// </summary>
    public static class AgentSettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "agent.json");

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
                    if (s != null) Current = s;
                }
            }
            catch { /* 加载失败用默认值 */ }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 保存失败静默忽略 */ }
        }
    }
}
