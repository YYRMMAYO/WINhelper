using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WINHELP
{
    /// <summary>
    /// 新手教程（导航 key="tutorial"）：引导不熟悉电脑的用户申请各大 AI 平台的 API 密钥，并一键填入。
    /// 由 MainWindow._factories 懒加载；依赖 AgentSettingsManager 与 ThemeManager 玻璃画刷。
    /// </summary>
    public partial class WindowTutorial : UserControl
    {
        /// <summary>请求返回首页（由 MainWindow 注入）</summary>
        public Action? OnCloseRequest;

        /// <summary>请求打开 Agent 助手（由 MainWindow 注入）</summary>
        public Action? OnOpenAgent;

        private sealed class ProviderInfo
        {
            public string Name = "";
            public string Emoji = "";
            public string Description = "";
            public string SignupUrl = "";
            public string ApiBaseUrl = "";
            public string DefaultModel = "";
            public string[] Steps = System.Array.Empty<string>();
        }

        private static readonly ProviderInfo[] Providers =
        {
            new()
            {
                Name = "DeepSeek",
                Emoji = "🐋",
                Description = "国产大模型，价格低、中文好",
                SignupUrl = "https://platform.deepseek.com/",
                ApiBaseUrl = "https://api.deepseek.com/v1",
                DefaultModel = "deepseek-chat",
                Steps = new[]
                {
                    "1. 打开 DeepSeek 开放平台，用手机号/邮箱注册登录",
                    "2. 点右上角头像 →「API keys」",
                    "3. 点「创建 API key」，输入名称后生成",
                    "4. 复制密钥（只显示一次，请妥善保存）"
                }
            },
            new()
            {
                Name = "OpenAI",
                Emoji = "🤖",
                Description = "GPT 系列，能力全面",
                SignupUrl = "https://platform.openai.com/api-keys",
                ApiBaseUrl = "https://api.openai.com/v1",
                DefaultModel = "gpt-4o-mini",
                Steps = new[]
                {
                    "1. 注册并登录 OpenAI 账号",
                    "2. 进入「API keys」页面",
                    "3. 点「Create new secret key」生成密钥",
                    "4. 复制密钥（注意账户需有余额）"
                }
            },
            new()
            {
                Name = "通义千问（阿里云百炼）建议选择，有免费额度",
                Emoji = "🐼",
                Description = "阿里云出品，国内访问稳定",
                SignupUrl = "https://dashscope.console.aliyun.com/apiKey",
                ApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                DefaultModel = "qwen-plus",
                Steps = new[]
                {
                    "1. 用阿里云账号登录百炼控制台",
                    "2. 进入「API-KEY 管理」",
                    "3. 点「创建 API-KEY」",
                    "4. 复制并保存密钥"
                }
            },
            new()
            {
                Name = "硅基流动 SiliconFlow",
                Emoji = "🌊",
                Description = "聚合多家开源模型，新用户有免费额度",
                SignupUrl = "https://cloud.siliconflow.cn/account/ak",
                ApiBaseUrl = "https://api.siliconflow.cn/v1",
                DefaultModel = "Qwen/Qwen2.5-7B-Instruct",
                Steps = new[]
                {
                    "1. 注册登录硅基流动账号",
                    "2. 进入「账户 → API 密钥」",
                    "3. 点「新建 API 密钥」",
                    "4. 复制密钥（新用户有免费额度）"
                }
            },
            new()
            {
                Name = "智谱 AI（GLM）",
                Emoji = "🧠",
                Description = "glm-4-flash 有免费额度",
                SignupUrl = "https://open.bigmodel.cn/usercenter/apikeys",
                ApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                DefaultModel = "glm-4-flash",
                Steps = new[]
                {
                    "1. 注册登录智谱 AI 开放平台",
                    "2. 进入「API 密钥」页面",
                    "3. 点「添加新的 API key」",
                    "4. 复制密钥（glm-4-flash 有免费额度）"
                }
            },
            new()
            {
                Name = "Moonshot（Kimi）",
                Emoji = "🌙",
                Description = "长上下文模型",
                SignupUrl = "https://platform.moonshot.cn/console/api-keys",
                ApiBaseUrl = "https://api.moonshot.cn/v1",
                DefaultModel = "moonshot-v1-8k",
                Steps = new[]
                {
                    "1. 注册登录 Kimi 开放平台",
                    "2. 进入「API 密钥」页面",
                    "3. 点「创建 API 密钥」",
                    "4. 复制密钥"
                }
            }
        };

        public WindowTutorial()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
            BuildCards();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
            ThemeManager.ApplyButtonTheme(BtnBack, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
        }

        /// <summary>安全获取卡片阴影：优先 CardShadow，回退 GlassShadow，最后兜底新建，绝不抛异常。</summary>
        private static DropShadowEffect GetCardShadow()
        {
            if (Application.Current.TryFindResource("CardShadow") is DropShadowEffect c) return c;
            if (Application.Current.TryFindResource("GlassShadow") is DropShadowEffect g) return g;
            return new DropShadowEffect { BlurRadius = 24, ShadowDepth = 1, Direction = 270, Opacity = 0.10 };
        }

        private void Button_Back_Click(object sender, RoutedEventArgs e) => OnCloseRequest?.Invoke();

        // ===== 生成教程卡片 =====

        private void BuildCards()
        {
            foreach (var p in Providers)
            {
                var card = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 14),
                    Effect = GetCardShadow()
                };

                var stack = new StackPanel();

                // 头部：图标 + 名称/描述
                var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var emoji = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(0xED, 0xE7, 0xF6)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                emoji.Child = new TextBlock
                {
                    Text = p.Emoji,
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                titles.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
                });
                titles.Children.Add(new TextBlock
                {
                    Text = p.Description,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6))
                });
                header.Children.Add(emoji);
                header.Children.Add(titles);
                stack.Children.Add(header);

                // 步骤
                stack.Children.Add(new TextBlock
                {
                    Text = string.Join("\n", p.Steps),
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19,
                    Margin = new Thickness(0, 4, 0, 12)
                });

                // 按钮行
                var pCopy = p;
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                var btnSignup = new Button
                {
                    Content = "前往申请密钥",
                    Height = 34,
                    Width = 130,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                btnSignup.Click += (s, e) => OpenUrl(pCopy.SignupUrl);
                var btnFill = new Button
                {
                    Content = "填入密钥",
                    Height = 34,
                    Width = 110,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnFill.Click += (s, e) => FillKey(pCopy);
                btnRow.Children.Add(btnSignup);
                btnRow.Children.Add(btnFill);
                stack.Children.Add(btnRow);

                card.Child = stack;
                PanelProviders.Children.Add(card);
            }
        }

        // ===== 交互 =====

        private void OpenUrl(string url)
        {
            // 仅接受 http/https；失败时保留原有“请手动复制”提示，方便用户手动访问
            if (!SafeUrl.Open(url, "提示") && !string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(Window.GetWindow(this), "无法打开链接，请手动复制：\n" + url, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void FillKey(ProviderInfo p)
        {
            var (ok, key) = ShowKeyDialog(p.Name);
            if (!ok || string.IsNullOrWhiteSpace(key)) return;

            AgentSettingsManager.Current.ApiBaseUrl = p.ApiBaseUrl;
            AgentSettingsManager.Current.ApiKey = key.Trim();
            AgentSettingsManager.Current.Model = p.DefaultModel;
            AgentSettingsManager.Save();

            var r = MessageBox.Show(Window.GetWindow(this),
                $"已为「{p.Name}」保存密钥与默认模型「{p.DefaultModel}」。\n\n是否现在打开 Agent 助手开始对话？",
                "保存成功", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes) OnOpenAgent?.Invoke();
        }

        /// <summary>代码创建的密钥输入对话框，返回是否确认以及密钥内容</summary>
        private (bool ok, string key) ShowKeyDialog(string providerName)
        {
            var dlg = new Window
            {
                Title = $"填入 {providerName} 密钥",
                Width = 480,
                Height = 250,
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5))
            };

            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tip = new TextBlock
            {
                Text = "请复制平台上的 API 密钥，粘贴到下方（密钥仅保存在本机，不会上传给任何人）：",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(tip, 0);

            var pb = new PasswordBox
            {
                Height = 36,
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 13,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };
            Grid.SetRow(pb, 1);

            var clip = new Button
            {
                Content = "从剪贴板粘贴",
                Height = 30,
                Width = 120,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            Grid.SetRow(clip, 2);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            var okBtn = new Button
            {
                Content = "保存",
                Height = 36,
                Width = 100,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            var cancelBtn = new Button
            {
                Content = "取消",
                Height = 36,
                Width = 80,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Margin = new Thickness(8, 0, 0, 0)
            };
            sp.Children.Add(okBtn);
            sp.Children.Add(cancelBtn);
            Grid.SetRow(sp, 4);

            grid.Children.Add(tip);
            grid.Children.Add(pb);
            grid.Children.Add(clip);
            grid.Children.Add(sp);
            dlg.Content = grid;

            string? key = null;
            bool confirmed = false;
            clip.Click += (s, e) => { try { if (Clipboard.ContainsText()) pb.Password = Clipboard.GetText(); } catch { } };
            okBtn.Click += (s, e) => { key = pb.Password; confirmed = true; dlg.DialogResult = true; };
            cancelBtn.Click += (s, e) => { dlg.DialogResult = false; };
            dlg.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { key = pb.Password; confirmed = true; dlg.DialogResult = true; }
            };

            var res = dlg.ShowDialog() == true && confirmed;
            return (res, key ?? "");
        }
    }
}
