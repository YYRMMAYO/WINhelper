using System.Windows;
using System.Windows.Media.Animation;

namespace WINHELP
{
    /// <summary>
    /// 启动动画窗口（独立窗体，Splash）。
    /// 职责：用户点击软件后第一时间呈现，播放淡入 + 罗盘旋转 + 进度条动画，
    /// 让"启动响应慢"在感知上大幅改善；主窗口就绪后由 App 调用 FadeOutAndClose 淡出关闭。
    /// 该窗口不依赖任何主题/玻璃资源（自建纯色样式），确保最早时刻即可显示。
    /// 由 App 启动流程 Show，非导航页。
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        /// <summary>淡出并最终关闭本窗口（由 App 在主窗口就绪后调用）。</summary>
        public void FadeOutAndClose()
        {
            // 已在 UI 线程调用；用代码创建动画以避免资源 Storyboard 的命名域解析问题。
            var sb = new Storyboard();
            var fade = new DoubleAnimation(1, 0, new Duration(System.TimeSpan.FromSeconds(0.35)));
            Storyboard.SetTarget(fade, this);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fade);
            sb.Completed += (_, _) => Close();
            sb.Begin(this);
        }
    }
}
