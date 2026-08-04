// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WINHELP
{
    /// <summary>
    /// DPAPI 加密保护工具 —— 从 AgentSettingsManager 抽出的公共封装，
    /// 供 Agent 历史记录（v4.9.0 新增）、Agent 设置等需要落盘保护的敏感数据复用。
    /// 使用 Windows DPAPI（DataProtectionScope.CurrentUser），密文仅当前 Windows 用户可解密；
    /// 附加熵（entropyTag）可防止其它进程读取到同一用户数据后直接解密。
    /// </summary>
    public static class DpapiProtector
    {
        /// <summary>使用 DPAPI 加密（仅当前 Windows 用户可解密）。空值原样返回。</summary>
        public static string Encrypt(string plain, string entropyTag)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var data = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain),
                    Encoding.UTF8.GetBytes(entropyTag),
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch { return ""; } // DPAPI 失败时宁可丢弃也不明文保存
        }

        /// <summary>使用 DPAPI 解密；失败返回空字符串（由调用方决定回退或清空）。</summary>
        public static string Decrypt(string cipher, string entropyTag)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
            {
                var data = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher),
                    Encoding.UTF8.GetBytes(entropyTag),
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }

        /// <summary>
        /// 判断一段文本看起来是否已经是 DPAPI 密文：Base64 可解码且不是合法 JSON。
        /// 用于旧数据迁移判定 —— 明文 JSON 历史记录首次加载后会被立即重写为密文。
        /// </summary>
        public static bool LooksEncrypted(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            try
            {
                // 合法 JSON（明文格式）→ 判定为未加密
                using (JsonDocument.Parse(s)) { }
                return false;
            }
            catch (JsonException) { }
            // 尝试 Base64 解码，成功且解码后为 UTF-8 可打印文本 → 判定为密文
            try
            {
                var bytes = Convert.FromBase64String(s);
                return bytes.Length > 8; // DPAPI 密文远大于 8 字节
            }
            catch { return false; }
        }
    }
}
