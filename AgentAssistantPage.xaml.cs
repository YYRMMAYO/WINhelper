using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// AgentAssistantPage.xaml 交互逻辑 — Agent 助手（导航 key="agent"，自主接入网络 API 的 AI 对话）
    /// 协议：OpenAI 兼容的 /chat/completions（支持流式 SSE，兼容本地模型如 Ollama）
    /// 新增：常用服务预设（含本地模型端口）、连接/端口测试，以及更清晰的错误分类。
    /// 由 MainWindow._factories 懒加载；依赖 AiClient / ToolRegistry / AgentSettingsManager 与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class AgentAssistantPage : UserControl
    {
        /// <summary>请求返回首页（由 MainWindow 注入）</summary>
        public Action? OnCloseRequest;

        /// <summary>请求打开 AI 密钥教程（由 MainWindow 注入）</summary>
        public Action? OnOpenTutorial;

        // 多轮对话历史（不包含 system 提示词）
        private readonly List<ChatTurn> _messages = new();

        // 当前正在生成的助手气泡文本引用
        private TextBlock? _activeText;

        private bool _isBusy = false;
        private bool _isTesting = false;
        private readonly string _placeholder = "输入消息，Enter 发送 / Shift+Enter 换行";

        // 会话历史持久化：仅保存多轮对话（role/content），绝不写入 API 密钥等敏感设置。
        private static readonly string HistoryPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP", "agent_history.json");

        // API 客户端（生成可能较慢，超时设长一些）
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(180) };

        private sealed record ChatTurn(string Role, string Content);

        public AgentAssistantPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

            // 初始化输入框水印
            TxtInput.Text = _placeholder;
            TxtInput.Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7));
            TxtInput.GotFocus += TxtInput_GotFocus;
            TxtInput.LostFocus += TxtInput_LostFocus;

            SldTemp.ValueChanged += (s, e) => TxtTempVal.Text = SldTemp.Value.ToString("0.0");

            // 填充设置字段
            PopulateSettings();

            // 会话持久化：若上次有对话历史则恢复，否则显示欢迎提示
            if (!LoadHistory())
            {
                // 欢迎提示（不使用 emoji，避免缺字/乱码）
                AddMessageBubble("assistant",
                    "你好，我是你的 AI 助手\n点击右上角「设置」配置 API 地址、密钥与模型后即可开始对话。\n支持 OpenAI 及任意兼容服务（如 DeepSeek、通义千问、Ollama 本地模型等）。");
            }
        }

        /// <summary>把当前多轮对话保存到 agent_history.json（仅 role/content）。</summary>
        private void SaveHistory()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json);
            }
            catch (Exception ex) { App.LogCrash(ex, "AgentHistorySave"); /* 持久化失败不应影响对话 */ }
        }

        /// <summary>从 agent_history.json 恢复上次对话；有内容返回 true，否则 false。</summary>
        private bool LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return false;
                var json = File.ReadAllText(HistoryPath);
                var list = JsonSerializer.Deserialize<List<ChatTurn>>(json);
                if (list == null || list.Count == 0) return false;
                foreach (var m in list)
                {
                    if (string.IsNullOrEmpty(m.Role) || string.IsNullOrEmpty(m.Content)) continue;
                    _messages.Add(m);
                    AddMessageBubble(m.Role, m.Content);
                }
                return true;
            }
            catch (Exception ex) { App.LogCrash(ex, "AgentHistoryLoad"); return false; }
        }

        /// <summary>导出当前对话为本地文件（txt 纯文本或 json），不含任何密钥。</summary>
        private void Button_Export_Click(object sender, RoutedEventArgs e)
        {
            if (_messages.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    UiLanguage.L("当前没有可导出的对话内容。", "There is no conversation to export yet."),
                    UiLanguage.L("导出对话", "Export Conversation"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = UiLanguage.L("导出对话", "Export Conversation"),
                Filter = UiLanguage.L("文本文件 (*.txt)|*.txt|JSON 文件 (*.json)|*.json", "Text (*.txt)|*.txt|JSON (*.json)|*.json"),
                FileName = "司南工具箱-AI对话-" + DateTime.Now.ToString("yyyyMMdd-HHmm"),
                DefaultExt = ".txt",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("# 司南工具箱 AI 对话导出");
                    sb.AppendLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    sb.AppendLine();
                    foreach (var m in _messages)
                    {
                        sb.AppendLine((m.Role == "user" ? "【用户】" : "【助手】"));
                        sb.AppendLine(m.Content);
                        sb.AppendLine();
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString());
                }
                System.Windows.MessageBox.Show(
                    UiLanguage.L("对话已导出到：\n", "Conversation exported to:\n") + dlg.FileName,
                    UiLanguage.L("导出成功", "Exported"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    UiLanguage.L("导出失败：", "Export failed: ") + ex.Message,
                    UiLanguage.L("导出对话", "Export Conversation"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnSettings, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnTest, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnSaveSettings, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnClear, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
            ThemeManager.ApplyButtonTheme(BtnSend, ThemeManager.AccentColor);
            ThemeManager.ApplyButtonTheme(BtnTutorial, Color.FromRgb(0x6C, 0x4B, 0xB4),
                hoverColor: Color.FromRgb(0x55, 0x39, 0x92));
        }

        // ===== 设置 =====

        private void PopulateSettings()
        {
            TxtApiBase.Text = AgentSettingsManager.Current.ApiBaseUrl;
            TxtApiKey.Password = AgentSettingsManager.Current.ApiKey;
            TxtModel.Text = AgentSettingsManager.Current.Model;
            SldTemp.Value = AgentSettingsManager.Current.Temperature;
            TxtTempVal.Text = AgentSettingsManager.Current.Temperature.ToString("0.0");
            TxtSystem.Text = AgentSettingsManager.Current.SystemPrompt;
        }

        private void Button_Settings_Click(object sender, RoutedEventArgs e)
        {
            // 重新填充，避免显示未保存的编辑
            PopulateSettings();
            SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Button_SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = TxtApiBase.Text.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                TxtSettingsHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtSettingsHint.Text = "[!] API 地址不能为空";
                return;
            }
            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                TxtSettingsHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtSettingsHint.Text = "[!] 地址需以 http(s):// 开头";
                return;
            }

            AgentSettingsManager.Current.ApiBaseUrl = baseUrl.TrimEnd('/');
            AgentSettingsManager.Current.ApiKey = TxtApiKey.Password;
            AgentSettingsManager.Current.Model = string.IsNullOrWhiteSpace(TxtModel.Text) ? "gpt-4o-mini" : TxtModel.Text.Trim();
            AgentSettingsManager.Current.Temperature = SldTemp.Value;
            AgentSettingsManager.Current.SystemPrompt = TxtSystem.Text;
            AgentSettingsManager.Save();

            TxtSettingsHint.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            TxtSettingsHint.Text = "[OK] 已保存";
        }

        /// <summary>常用服务预设：选择后自动填入对应 API 地址（含本地模型端口）及匹配默认模型</summary>
        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPreset.SelectedItem is ComboBoxItem item)
            {
                var content = item.Content?.ToString() ?? "";
                int a = content.IndexOf('(');
                int b = content.IndexOf(')');
                if (a >= 0 && b > a)
                {
                    TxtApiBase.Text = content.Substring(a + 1, b - a - 1).Trim();
                    TxtSettingsHint.Text = "";
                }
                // 同步为该服务商匹配的默认模型，避免 model 仍是 OpenAI 默认的 gpt-4o-mini，
                // 否则发给 DeepSeek / 通义千问 等会触发 HTTP 400（模型不匹配）
                if (item.Tag is string m && !string.IsNullOrWhiteSpace(m))
                    TxtModel.Text = m;
            }
        }

        /// <summary>从当前 UI 设置读取（与“测试连接”使用同一来源，保证测试即所得）</summary>
        private (string baseUrl, string apiKey, string model, double temperature, string systemPrompt) ReadUiSettings()
        {
            var baseUrl = (TxtApiBase.Text ?? "").Trim().TrimEnd('/');
            var apiKey = TxtApiKey.Password ?? "";
            var model = string.IsNullOrWhiteSpace(TxtModel.Text) ? "gpt-4o-mini" : TxtModel.Text.Trim();
            var temperature = SldTemp.Value;
            var systemPrompt = TxtSystem.Text ?? "";
            return (baseUrl, apiKey, model, temperature, systemPrompt);
        }

        private void Button_Tutorial_Click(object sender, RoutedEventArgs e)
            => OnOpenTutorial?.Invoke();

        private void ChkAgent_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtAgentHint != null)
                TxtAgentHint.Visibility = (ChkAgent.IsChecked == true)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== 连接 / 端口测试 =====

        private async void Button_Test_Click(object sender, RoutedEventArgs e)
        {
            if (_isTesting) return;
            _isTesting = true;
            BtnTest.IsEnabled = false;
            BtnTest.Content = "测试中…";
            TxtTestHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TxtTestHint.Text = "正在测试连接…";
            try
            {
                var model = string.IsNullOrWhiteSpace(TxtModel.Text) ? "gpt-4o-mini" : TxtModel.Text.Trim();
                await TestConnectionAsync(TxtApiBase.Text.Trim(), TxtApiKey.Password, model);
            }
            finally
            {
                _isTesting = false;
                BtnTest.IsEnabled = true;
                BtnTest.Content = "测试连接";
            }
        }

        private void SetTest(bool ok, string msg)
        {
            Dispatcher.Invoke(() =>
            {
                TxtTestHint.Foreground = new SolidColorBrush(ok
                    ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xE7, 0x4C, 0x3C));
                TxtTestHint.Text = (ok ? "[OK] " : "[ERR] ") + msg;
            });
        }

        /// <summary>
        /// 连接测试：先探测 host:port 的 TCP 可达性（直接定位“端口未监听/防火墙/超时”），
        /// 再发起一次真实的极小 POST /chat/completions 探测，验证“模型 + 密钥 + 路径”三者同时有效。
        /// 仅用 max_tokens=1，不产生实质内容，也不消耗真实对话额度。
        /// 这样“测试成功”即代表“对话可用”，不会再出现测试通过却对话 400 的情况。
        /// </summary>
        private async Task TestConnectionAsync(string baseUrl, string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                SetTest(false, "地址无效：需以 http:// 或 https:// 开头");
                return;
            }
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                SetTest(false, "地址格式不正确（示例：http://localhost:11434/v1）");
                return;
            }

            var host = uri.Host;
            var port = uri.Port; // 未显式指定时返回协议默认端口（80/443）

            // 1) TCP 端口连通性探测
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(host, port);
                var done = await Task.WhenAny(connectTask, Task.Delay(4000));
                if (done != connectTask || !tcp.Connected)
                {
                    SetTest(false, $"无法连接端口 {port}（服务未启动 / 端口未监听 / 被防火墙拦截 / 超时）");
                    return;
                }
            }
            catch (SocketException sx)
            {
                SetTest(false, $"端口 {port} 不可达：{sx.SocketErrorCode}（请确认服务已启动且端口正确）");
                return;
            }
            catch (Exception ex)
            {
                SetTest(false, $"端口探测失败：{ex.Message}");
                return;
            }

            // 2) 真实对话探测：直接 POST /chat/completions，验证模型 + 密钥 + 路径同时有效
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
                var probe = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 1,
                    temperature = 0.0,
                    stream = false
                };
                var json = JsonSerializer.Serialize(probe);
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    SetTest(true, $"连接成功：已用模型「{model}」完成一次真实对话测试");
                    return;
                }

                // 非 200：读取错误体并给出针对性诊断
                var err = await SafeRead(resp);
                var code = (int)resp.StatusCode;
                if (code == 400)
                    SetTest(false,
                        $"HTTP 400：模型「{model}」不被该服务支持，或请求格式错误。\n请确认「模型名称」与该服务商匹配（如 DeepSeek 用 deepseek-chat，通义千问用 qwen-plus）。\n{Truncate(err, 400)}");
                else if (code == 401)
                    SetTest(false, $"HTTP 401：API 密钥无效或缺失。\n{Truncate(err, 400)}");
                else if (code == 404)
                    SetTest(false, $"HTTP 404：接口路径不存在，请确认 API 地址以 /v1 结尾。\n{Truncate(err, 400)}");
                else if (code == 429)
                    SetTest(false, $"HTTP 429：请求过于频繁或额度不足（密钥有效，可稍后重试）。\n{Truncate(err, 400)}");
                else
                    SetTest(false, $"连接失败：HTTP {code} {resp.ReasonPhrase}\n{Truncate(err, 400)}");
            }
            catch (OperationCanceledException)
            {
                SetTest(false, "对话探测超时（端口通但服务未在限定时间内响应）");
            }
            catch (HttpRequestException hx)
            {
                SetTest(false, $"HTTP 请求失败：{hx.Message}（本地模型地址需以 /v1 结尾）");
            }
            catch (Exception ex)
            {
                SetTest(false, $"探测异常：{ex.Message}");
            }
        }

        // ===== 输入框水印 =====

        private void TxtInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtInput.Text == _placeholder)
            {
                TxtInput.Text = "";
                TxtInput.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
            }
        }

        private void TxtInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtInput.Text))
            {
                TxtInput.Text = _placeholder;
                TxtInput.Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7));
            }
        }

        private void TxtInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                Button_Send_Click(sender, e);
            }
        }

        // ===== 对话 =====

        private void Button_Clear_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _messages.Clear();
            ChatList.Children.Clear();
            TxtEmpty.Visibility = Visibility.Visible;
            SaveHistory();
        }

        private async void Button_Send_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;

            var text = TxtInput.Text;
            if (text == _placeholder) text = "";
            text = text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // 清空输入
            TxtInput.Text = "";
            TxtInput.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
            TxtEmpty.Visibility = Visibility.Collapsed;

            // 显示用户消息
            AddMessageBubble("user", text);
            _messages.Add(new ChatTurn("user", text));
            SaveHistory();

            // 准备助手气泡
            var (border, textBlock) = AddMessageBubble("assistant", "思考中…");
            _activeText = textBlock;

            _isBusy = true;
            BtnSend.IsEnabled = false;
            BtnSend.Content = "生成中";
            BtnClear.IsEnabled = false;

            Action<string> onDelta = piece =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 首次收到内容时清掉占位符
                    if (textBlock.Text == "思考中…") textBlock.Text = "";
                    textBlock.Text += piece;
                    ChatScroll.ScrollToEnd();
                });
            };

            try
            {
                var full = ChkAgent.IsChecked == true
                    ? await AgentLoopAsync(text, onDelta)
                    : await StreamReplyAsync(text, onDelta);
                _messages.Add(new ChatTurn("assistant", full));
                SaveHistory();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    textBlock.Text = "调用失败：\n" + ex.Message;
                    border.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF0, 0xF0));
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                });
            }
            finally
            {
                _activeText = null;
                _isBusy = false;
                BtnSend.IsEnabled = true;
                BtnSend.Content = "发送 ➤";
                BtnClear.IsEnabled = true;
                Dispatcher.Invoke(ChatScroll.ScrollToEnd);
            }
        }

        /// <summary>流式调用 OpenAI 兼容接口，逐片回调 onDelta，返回完整文本</summary>
        private async Task<string> StreamReplyAsync(string userText, Action<string> onDelta)
        {
            // 与“测试连接”使用同一来源（UI 当前值），保证“测试成功即对话可用”，避免保存前后不一致
            var (baseUrl, apiKey, model, temperature, systemPrompt) = ReadUiSettings();
            var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";

            var msgs = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                msgs.Add(new { role = "system", content = systemPrompt });
            foreach (var m in _messages)
                msgs.Add(new { role = m.Role, content = m.Content });

            var payload = new
            {
                model = model,
                messages = msgs,
                temperature = temperature,
                stream = true
            };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // 区分连接层错误（端口/防火墙/超时）与 HTTP 业务错误，给出清晰诊断
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (HttpRequestException hx) when (hx.InnerException is SocketException sx)
            {
                throw new Exception($"无法连接到该地址/端口：{sx.SocketErrorCode}（请确认服务已启动、端口正确且未被防火墙拦截；本地模型地址需以 /v1 结尾）");
            }
            catch (HttpRequestException hx)
            {
                throw new Exception($"连接失败：{hx.Message}");
            }
            catch (OperationCanceledException)
            {
                throw new Exception("连接超时（服务未在限定时间内响应，可点击「测试连接」排查端口）");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await SafeRead(resp);
                var hint = "";
                if ((int)resp.StatusCode == 404)
                    hint = "\n提示：若使用本地模型(Ollama/LM Studio)，请确认 API 地址以 /v1 结尾。";
                else if ((int)resp.StatusCode == 400)
                    hint = "\n提示：多为「模型名称」与该服务商不匹配（如 DeepSeek 用 deepseek-chat，通义千问用 qwen-plus）。请在「设置」中修正并保存后重试。";
                else if ((int)resp.StatusCode == 401)
                    hint = "\n提示：API 密钥无效或缺失。请在「设置」中填写正确密钥并保存后重试。";
                throw new Exception($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{Truncate(err, 600)}{hint}");
            }

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            var sb = new StringBuilder();

            if (contentType.Contains("event-stream"))
            {
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        // 顶层错误
                        if (root.TryGetProperty("error", out var errEl))
                        {
                            var msg = errEl.ValueKind == JsonValueKind.String ? errEl.GetString()
                                : (errEl.TryGetProperty("message", out var em) ? em.GetString() : null);
                            throw new InvalidOperationException("API 返回错误: " + (msg ?? "未知错误"));
                        }

                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];
                            string? piece = null;
                            if (choice.TryGetProperty("delta", out var delta))
                            {
                                if (delta.TryGetProperty("content", out var c)) piece = c.GetString();
                            }
                            else if (choice.TryGetProperty("message", out var msg))
                            {
                                if (msg.TryGetProperty("content", out var c)) piece = c.GetString();
                            }
                            if (!string.IsNullOrEmpty(piece))
                            {
                                sb.Append(piece);
                                onDelta(piece);
                            }
                        }
                    }
                    catch (InvalidOperationException) { throw; }
                    catch (JsonException) { /* 心跳/注释行忽略 */ }
                }
            }
            else
            {
                // 非流式响应
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errEl2))
                {
                    var msg = errEl2.ValueKind == JsonValueKind.String ? errEl2.GetString()
                        : (errEl2.TryGetProperty("message", out var em) ? em.GetString() : null);
                    throw new InvalidOperationException("API 返回错误: " + (msg ?? "未知错误"));
                }
                var content = root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0
                    && ch[0].TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)
                    ? c.GetString() : null;
                if (!string.IsNullOrEmpty(content)) { sb.Append(content); onDelta(content); }
            }

            return sb.ToString();
        }

        // ===== AI 代操作模式（沙盒）=====
        // 通过 OpenAI 兼容的 function calling 让 AI 调用受限工具；每次调用前必须用户确认。

        private const string OperatorNote =
            "\n\n[代操作模式] 你拥有以下工具帮助用户在 Windows 电脑上完成任务：" +
            "open_app（打开程序）、open_settings（打开系统设置页）、run_diagnostic（运行只读诊断命令）、take_screenshot（截取屏幕）。" +
            "每次调用工具前，用户都会看到明确的确认提示，只有用户点击「允许」才会真正执行；如果用户拒绝，你会收到「用户拒绝了该操作」的结果。" +
            "请优先使用最安全、影响最小的方式完成任务，绝不要建议用户执行危险或破坏性的操作。";

        /// <summary>
        /// 非流式对话补全（用于代操作循环）：发送 payload，返回解析后的响应根节点。
        /// 网络/HTTP 错误会抛出异常，由调用方转为对话消息。
        /// </summary>
        private async Task<JsonElement> PostChatAsync(string endpoint, string apiKey, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req);
            }
            catch (HttpRequestException hx) when (hx.InnerException is SocketException sx)
            {
                throw new Exception($"无法连接到该地址/端口：{sx.SocketErrorCode}（请确认服务已启动、端口正确，本地模型地址需以 /v1 结尾）");
            }
            catch (HttpRequestException hx)
            {
                throw new Exception($"连接失败：{hx.Message}");
            }
            catch (OperationCanceledException)
            {
                throw new Exception("连接超时（可点击「测试连接」排查端口）");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await SafeRead(resp);
                var code = (int)resp.StatusCode;
                var hint = code switch
                {
                    404 => "\n提示：若使用本地模型，请确认 API 地址以 /v1 结尾。",
                    400 => "\n提示：可能是该模型不支持「工具调用/图像」，或模型名称不匹配。可关闭「AI 代操作」后再试。",
                    401 => "\n提示：API 密钥无效或缺失。",
                    _ => ""
                };
                throw new Exception($"HTTP {code} {resp.ReasonPhrase}\n{Truncate(err, 600)}{hint}");
            }

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        /// <summary>
        /// 代操作主循环：与模型多轮交互，遇到工具调用则经用户确认后执行，
        /// 将结果回传模型，直到模型给出最终自然语言回答或达到步数上限。
        /// </summary>
        private async Task<string> AgentLoopAsync(string userText, Action<string> onDelta)
        {
            var (baseUrl, apiKey, model, temperature, systemPrompt) = ReadUiSettings();
            var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";

            var apiMessages = new List<object>();
            apiMessages.Add(new { role = "system", content = systemPrompt + OperatorNote });
            foreach (var m in _messages)
                apiMessages.Add(new { role = m.Role, content = (object)m.Content });

            const int maxSteps = 8;
            for (int step = 0; step < maxSteps; step++)
            {
                var payload = new
                {
                    model = model,
                    messages = apiMessages,
                    temperature = temperature,
                    stream = false,
                    tools = ToolRegistry.Tools,
                    tool_choice = "auto"
                };

                JsonElement root;
                try { root = await PostChatAsync(endpoint, apiKey, payload); }
                catch (Exception ex) { return "调用失败：\n" + ex.Message; }

                if (root.TryGetProperty("error", out var errEl))
                {
                    var msg = errEl.ValueKind == JsonValueKind.String ? errEl.GetString()
                        : (errEl.TryGetProperty("message", out var em) ? em.GetString() : null);
                    return "API 返回错误: " + (msg ?? "未知错误");
                }
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return "（未收到有效响应）";

                var message = choices[0].GetProperty("message");

                // 是否包含工具调用
                if (message.TryGetProperty("tool_calls", out var tcs)
                    && tcs.ValueKind == JsonValueKind.Array && tcs.GetArrayLength() > 0)
                {
                    var toolCallsEcho = new List<object>();
                    var toolResults = new List<object>();

                    foreach (var tc in tcs.EnumerateArray())
                    {
                        var id = tc.GetProperty("id").GetString() ?? "";
                        var fn = tc.GetProperty("function");
                        var name = fn.GetProperty("name").GetString() ?? "";
                        var argsJson = fn.TryGetProperty("arguments", out var aEl) ? (aEl.GetString() ?? "{}") : "{}";
                        var args = JsonDocument.Parse(argsJson).RootElement.Clone();

                        toolCallsEcho.Add(new { id = id, type = "function", function = new { name = name, arguments = argsJson } });

                        var result = await ToolRegistry.ExecuteToolAsync(name, args, Window.GetWindow(this));
                        Dispatcher.Invoke(() => AddToolBubble(result.Description, result.Text));

                        toolResults.Add(new { role = "tool", tool_call_id = id, content = result.Text });

                        // 截图：把图像作为 user 消息附上，供视觉模型使用
                        if (!string.IsNullOrEmpty(result.ImageBase64))
                        {
                            toolResults.Add(new
                            {
                                role = "user",
                                content = (object)new object[]
                                {
                                    new { type = "text", text = "（已附上刚才的屏幕截图，请分析当前界面）" },
                                    new { type = "image_url", image_url = new { url = "data:image/png;base64," + result.ImageBase64 } }
                                }
                            });
                        }
                    }

                    var assistantContent = message.TryGetProperty("content", out var cEl) && cEl.ValueKind == JsonValueKind.String
                        ? cEl.GetString() ?? "" : "";

                    apiMessages.Add(new { role = "assistant", content = assistantContent, tool_calls = toolCallsEcho });
                    apiMessages.AddRange(toolResults);
                    continue;
                }

                // 无工具调用 → 最终回答
                var text = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() ?? "" : "";
                onDelta(text);
                return text;
            }

            return "（已达到最大操作步数，如需继续请提出新的请求。）";
        }

        private void AddToolBubble(string title, string detail)
        {
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xEC, 0xFD)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xC4, 0xE9)),
                BorderThickness = new Thickness(1)
            };
            var tb = new TextBlock
            {
                Text = "🔧 " + title + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x23, 0x6A))
            };
            bubble.Child = tb;
            ChatList.Children.Add(bubble);
            TxtEmpty.Visibility = Visibility.Collapsed;
            ChatScroll.ScrollToEnd();
        }

        // ===== 气泡渲染 =====

        private (Border, TextBlock) AddMessageBubble(string role, string text)
        {
            var isUser = role == "user";

            var bubble = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 520,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = isUser
                    ? new SolidColorBrush(ThemeManager.AccentColor)
                    : new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5))
            };

            var tb = new TextBlock
            {
                Text = text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Foreground = isUser ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
            };

            bubble.Child = tb;
            ChatList.Children.Add(bubble);
            TxtEmpty.Visibility = Visibility.Collapsed;
            ChatScroll.ScrollToEnd();
            return (bubble, tb);
        }

        // ===== 工具 =====

        private static async Task<string> SafeRead(HttpResponseMessage resp)
        {
            try { return await resp.Content.ReadAsStringAsync(); }
            catch { return ""; }
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
    }
}
