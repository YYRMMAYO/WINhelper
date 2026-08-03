using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// PcHelpPage.xaml 交互逻辑 — 电脑帮助（导航 key="help"）
    /// 由 MainWindow._factories 懒加载；依赖 ThemeManager 玻璃画刷与 LocExtension 多语言。
    /// </summary>
    public partial class PcHelpPage : UserControl
    {
        public PcHelpPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

            LoadSystemInfo();
        }

        private void ApplyTheme()
        {
            RootGrid.Background = Brushes.Transparent;
        }

        /// <summary>加载系统信息</summary>
        private void LoadSystemInfo()
        {
            try
            {
                // 操作系统
                TxtOS.Text = Environment.OSVersion.VersionString;

                // 系统位数
                TxtArch.Text = RuntimeInformation.OSArchitecture.ToString();

                // .NET 版本
                TxtDotNet.Text = RuntimeInformation.FrameworkDescription;

                // 机器名称
                TxtMachine.Text = Environment.MachineName;
            }
            catch { }
        }


        // ===== 通过 process 启动系统工具 =====
        private static void RunCommand(string exe, string? args = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                };
                if (args != null) psi.Arguments = args;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ===== 系统工具入口 =====
        private void Btn_ControlPanel(object sender, RoutedEventArgs e) => RunCommand("control.exe");
        private void Btn_TaskMgr(object sender, RoutedEventArgs e) => RunCommand("taskmgr.exe");
        private void Btn_DiskCleanup(object sender, RoutedEventArgs e) => RunCommand("cleanmgr.exe");
        private void Btn_DiskMgmt(object sender, RoutedEventArgs e) => RunCommand("diskmgmt.msc");
        private void Btn_DevMgr(object sender, RoutedEventArgs e) => RunCommand("devmgmt.msc");
        private void Btn_Network(object sender, RoutedEventArgs e) => RunCommand("ncpa.cpl");
        private void Btn_Clipboard(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start ms-settings:clipboard");
        private void Btn_Power(object sender, RoutedEventArgs e) => RunCommand("powercfg.cpl");
        private void Btn_Mouse(object sender, RoutedEventArgs e) => RunCommand("main.cpl");
        private void Btn_Display(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start ms-settings:display");
        private void Btn_Sound(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start ms-settings:sound");
        private void Btn_DateTime(object sender, RoutedEventArgs e) => RunCommand("timedate.cpl");
        private void Btn_Input(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start ms-settings:regionlanguage");
        private void Btn_Programs(object sender, RoutedEventArgs e) => RunCommand("appwiz.cpl");
        private void Btn_Security(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start windowsdefender:");
        private void Btn_SysInfo(object sender, RoutedEventArgs e) => RunCommand("msinfo32.exe");
        private void Btn_OSK(object sender, RoutedEventArgs e) => RunCommand("osk.exe");
        private void Btn_Magnifier(object sender, RoutedEventArgs e) => RunCommand("magnify.exe");
        private void Btn_Snipping(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start ms-screenclip:");
        private void Btn_Mail(object sender, RoutedEventArgs e) => RunCommand("cmd.exe", "/c start outlookmail:");
    }
}
