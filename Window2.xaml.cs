using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// Window2.xaml 交互逻辑 — WIN助手（实用软件官方直通车）
    /// </summary>
    public partial class Window2 : UserControl
    {
        public Window2()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);
        }

        private void ApplyTheme()
        {
            RootGrid.Background = ThemeManager.CreateBackgroundBrush();

            ThemeManager.ApplyButtonTheme(BtnBack, Color.FromRgb(0x95, 0xA5, 0xA6),
                hoverColor: Color.FromRgb(0x7F, 0x8C, 0x8D));
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}");
            }
        }

        private void Button_Click_Compress(object sender, RoutedEventArgs e)   => OpenUrl("https://www.7-zip.org/");
        private void Button_Click_Record(object sender, RoutedEventArgs e)     => OpenUrl("https://www.audacityteam.org/");
        private void Button_Click_Uninstall(object sender, RoutedEventArgs e)  => OpenUrl("https://geekuninstaller.com/");
        private void Button_Click_OBS(object sender, RoutedEventArgs e)        => OpenUrl("https://obsproject.com/");
        private void Button_Click_VLC(object sender, RoutedEventArgs e)        => OpenUrl("https://www.videolan.org/vlc/");
        private void Button_Click_Everything(object sender, RoutedEventArgs e) => OpenUrl("https://www.voidtools.com/");
        private void Button_Click_Notepad(object sender, RoutedEventArgs e)    => OpenUrl("https://notepad-plus-plus.org/");
        private void Button_Click_Snipaste(object sender, RoutedEventArgs e)   => OpenUrl("https://www.snipaste.com/");
        private void Button_Click_PotPlayer(object sender, RoutedEventArgs e)  => OpenUrl("https://potplayer.daum.net/");
        private void Button_Click_GIMP(object sender, RoutedEventArgs e)        => OpenUrl("https://www.gimp.org/");
        private void Button_Click_Inkscape(object sender, RoutedEventArgs e)    => OpenUrl("https://inkscape.org/");
        private void Button_Click_HandBrake(object sender, RoutedEventArgs e)   => OpenUrl("https://handbrake.fr/");
        private void Button_Click_ScreenToGif(object sender, RoutedEventArgs e) => OpenUrl("https://www.screentogif.com/");
        private void Button_Click_VSCode(object sender, RoutedEventArgs e)      => OpenUrl("https://code.visualstudio.com/");
        private void Button_Click_DBeaver(object sender, RoutedEventArgs e)     => OpenUrl("https://dbeaver.io/");
        private void Button_Click_WinSCP(object sender, RoutedEventArgs e)      => OpenUrl("https://winscp.net/");
        private void Button_Click_PuTTY(object sender, RoutedEventArgs e)       => OpenUrl("https://www.putty.org/");
        private void Button_Click_Rufus(object sender, RoutedEventArgs e)       => OpenUrl("https://rufus.ie/");
        private void Button_Click_CPUZ(object sender, RoutedEventArgs e)        => OpenUrl("https://www.cpuid.com/softwares/cpu-z.html");
        private void Button_Click_HWiNFO(object sender, RoutedEventArgs e)      => OpenUrl("https://www.hwinfo.com/");
        private void Button_Click_qBittorrent(object sender, RoutedEventArgs e) => OpenUrl("https://www.qbittorrent.org/");
        private void Button_Click_Bandizip(object sender, RoutedEventArgs e)    => OpenUrl("https://www.bandisoft.com/bandizip/");
        private void Button_Click_Telegram(object sender, RoutedEventArgs e)    => OpenUrl("https://desktop.telegram.org/");
        private void Button_Click_QuickLook(object sender, RoutedEventArgs e)   => OpenUrl("https://github.com/QL-Win/QuickLook");
        private void Button_Click_LocalSend(object sender, RoutedEventArgs e)   => OpenUrl("https://localsend.org/");
        private void Button_Click_WattToolkit(object sender, RoutedEventArgs e) => OpenUrl("https://steampp.net/");

        // ===== 新增实用程序 =====
        private void Button_Click_PowerToys(object sender, RoutedEventArgs e)       => OpenUrl("https://github.com/microsoft/PowerToys");
        private void Button_Click_LibreOffice(object sender, RoutedEventArgs e)     => OpenUrl("https://www.libreoffice.org/");
        private void Button_Click_Firefox(object sender, RoutedEventArgs e)         => OpenUrl("https://www.mozilla.org/firefox/");
        private void Button_Click_ShareX(object sender, RoutedEventArgs e)          => OpenUrl("https://getsharex.com/");
        private void Button_Click_IrfanView(object sender, RoutedEventArgs e)       => OpenUrl("https://www.irfanview.com/");
        private void Button_Click_Calibre(object sender, RoutedEventArgs e)         => OpenUrl("https://calibre-ebook.com/");
        private void Button_Click_CrystalDiskInfo(object sender, RoutedEventArgs e) => OpenUrl("https://crystalmark.info/en/software/crystaldiskinfo/");
        private void Button_Click_BleachBit(object sender, RoutedEventArgs e)       => OpenUrl("https://www.bleachbit.org/");
        private void Button_Click_WinTerminal(object sender, RoutedEventArgs e)     => OpenUrl("https://github.com/microsoft/terminal");
        private void Button_Click_Git(object sender, RoutedEventArgs e)             => OpenUrl("https://git-scm.com/");
        private void Button_Click_Brave(object sender, RoutedEventArgs e)           => OpenUrl("https://brave.com/");
        private void Button_Click_MusicBee(object sender, RoutedEventArgs e)        => OpenUrl("https://getmusicbee.com/");
        private void Button_Click_PaintNET(object sender, RoutedEventArgs e)        => OpenUrl("https://www.getpaint.net/");
    }
}
