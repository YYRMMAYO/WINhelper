using System.Collections.Generic;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP;

/// <summary>故障排查向导页：分步诊断与修复。</summary>
public partial class TroubleshootWizardPage : UserControl
{
    private static readonly Dictionary<string, List<string>> Solutions = new()
    {
        ["无法上网"] = new() {
            "① 快速确认：右下角网络图标是否打叉或感叹号；Wi‑Fi 是否已连接、飞行模式是否误开（Win+A 可快速切换）。",
            "② 本机协议栈：按 Win+R 输入 cmd，执行 ping 127.0.0.1，不通说明 TCP/IP 协议栈异常。",
            "③ 局域网连通：执行 ping 网关IP（如 192.168.1.1），不通多为网线/路由器问题。",
            "④ 外网连通：执行 ping 223.5.5.5 与 ping www.baidu.com。能通 IP 但不通域名→DNS 问题；都不通→宽带故障。",
            "⑤ 一键修复：在管理员 cmd 执行 netsh winsock reset 与 netsh int ip reset，重启电脑。",
            "⑥ 改 DNS：网络适配器属性→IPv4→首选 DNS 填 223.5.5.5、备用 119.29.29.29，确定后执行 ipconfig /flushdns。",
            "⑦ 仍不行：重启光猫/路由器（断电 10 秒）；或连手机热点排除是宽带线路故障。"
        },
        ["电脑卡顿"] = new() {
            "① 看占用：Ctrl+Shift+Esc 打开任务管理器→性能页，确认 CPU / 内存 / 磁盘哪一项长期接近 100%。",
            "② 杀进程：在进程页按占用排序，结束无响应或异常高占用的程序（勿结束系统进程）。",
            "③ 清垃圾：使用本软件「系统清理」清理 C 盘临时文件、回收站、更新缓存释放空间。",
            "④ 管启动：在「启动项」中禁用不必要的开机自启软件，缩短开机时间。",
            "⑤ 查病毒：用 Windows 安全中心做一次全盘扫描，排除挖矿木马/流氓软件。",
            "⑥ 硬件层面：内存不足可加装；机械硬盘换固态 SSD 提升最明显；散热差导致降频请清灰、换硅脂。"
        },
        ["蓝屏 / 重启"] = new() {
            "① 记代码：蓝屏时拍照记录 STOP 错误码（如 0x0000001A）与失败模块名，便于检索原因。",
            "② 最近变更：是否刚装/更新过驱动、软件、Windows 补丁？到设置→Windows 更新→更新历史记录→卸载更新回退。",
            "③ 回滚驱动：右键开始→设备管理器，找到带黄色感叹号的设备→右键属性→驱动程序→回滚驱动。",
            "④ 内存检测：开始菜单搜 Windows 内存诊断，重启后自动扫描内存。",
            "⑤ 磁盘检查：管理员 cmd 执行 chkdsk C: /f /r，按 Y 计划下次重启检查。",
            "⑥ 进安全模式：设置→恢复→高级启动→重启，选疑难解答→高级选项→启动设置→安全模式，排查软件冲突。",
            "⑦ 仍频繁蓝屏：备份数据后用设置→恢复→重置此电脑，或全新安装系统。"
        },
        ["没有声音"] = new() {
            "① 查静音：右下角喇叭未被静音、音量不为 0；笔记本查物理静音键/功能键（Fn+Fx）。",
            "② 选设备：右键喇叭→声音设置，确认输出设备是你正在用的音箱/耳机，并点测试。",
            "③ 设默认：控制面板→声音→播放选项卡，右键正确设备→设为默认设备。",
            "④ 重启用：设备管理器→声音、视频和游戏控制器→右键声卡→禁用设备再启用设备。",
            "⑤ 重装驱动：右键声卡→卸载设备（勾选删除驱动），重启让系统重装；或到官网装最新驱动。",
            "⑥ 疑难解答：右键喇叭→疑难解答声音问题，让系统自动修复常见配置。"
        },
        ["软件闪退"] = new() {
            "① 管理员运行：右键程序→以管理员身份运行，排除权限不足。",
            "② 兼容模式：右键→属性→兼容性→勾以兼容模式运行（选旧版 Windows），并勾禁用全屏优化。",
            "③ 更新程序：到官网或 Microsoft Store 升级到最新版本，旧版常与系统不兼容。",
            "④ 装运行库：确认已装 Visual C++ Redistributable(2005~2022)、.NET Framework、DirectX；游戏还需显卡运行库。",
            "⑤ 看日志：事件查看器(eventvwr.msc)→Windows 日志→应用程序，按时间找红色错误定位崩溃模块 dll。",
            "⑥ 干净启动：msconfig→服务勾隐藏所有 Microsoft 服务→全部禁用→重启，逐步启用排查冲突软件。"
        },
        ["风扇很吵 / 发热"] = new() {
            "① 看温度：用图吧工具箱等工具看 CPU/GPU 温度，待机大于 60℃、游戏大于 90℃ 属过热。",
            "② 清灰尘：断电后用毛刷/气吹清理出风口、风扇、散热鳍片积灰。",
            "③ 降负载：任务管理器结束异常占用进程；浏览器关多余标签页；游戏降画质/帧率。",
            "④ 电源计划：电源选项切到平衡或节能，避免高性能一直拉高频率。",
            "⑤ 换硅脂：长期使用(2~3 年)后 CPU/GPU 硅脂干涸，更换可降 10~20℃。",
            "⑥ 外接散热：笔记本加散热底座；仍无效且频繁降频建议送修清灰/换风扇。"
        },
        ["开机慢 / 启动慢"] = new() {
            "① 看耗时：任务管理器→启动应用，顶部显示上次 BIOS 时间与各项启用影响。",
            "② 禁自启：在「启动项」中关闭不必要的开机自启（聊天、云盘、播放器等可手动开）。",
            "③ 快速启动：电源选项→选择电源按钮功能→取消启用快速启动后测试开机。",
            "④ 查硬盘：机械硬盘 HDD 开机明显慢于固态 SSD；将系统与常用软件迁移到 SSD。",
            "⑤ 修系统：管理员 cmd 执行 sfc /scannow 修复系统文件，排除系统损坏拖慢。",
            "⑥ 卸冗余：卸载极少使用的全家桶/工具栏类软件，减少后台服务。"
        },
        ["屏幕 / 显示异常"] = new() {
            "① 查连线：外接显示器确认 HDMI/DP 线插紧、信号源选对；笔记本用 Win+P 切换投影。",
            "② 调分辨率：右键桌面→显示设置→分辨率选推荐，缩放选 100%~150% 避免模糊。",
            "③ 更驱动：设备管理器→显示适配器→右键更新；或到 NVIDIA/AMD/Intel 官网装最新驱动。",
            "④ 闪屏花屏：更新驱动无效则可能是线材/接口松动或显卡硬件故障，借显示器排查。",
            "⑤ 亮度颜色：显示设置→夜间模式/亮度；外接屏用显示器物理按键恢复出厂。",
            "⑥ 黑屏有灯：强制关机再开；仍黑屏多为显卡/内存接触不良，重新插拔内存与显卡。"
        },
        ["蓝牙 / 外设连不上"] = new() {
            "① 开关确认：Win+A 打开快速设置确认蓝牙已开；设备电量充足、处于配对模式。",
            "② 重配对：设置→蓝牙和其他设备→移除该设备→重新添加蓝牙或其他设备配对。",
            "③ 重启用：设备管理器→蓝牙/人体学输入设备→右键禁用再启用；或重启蓝牙支持服务。",
            "④ 驱更新：蓝牙适配器右键更新驱动；USB 外设换接口/换线排除接触问题。",
            "⑤ 排查冲突：拔掉其他占用相同通道的设备；无线键鼠靠近接收器、避开 2.4G 干扰。",
            "⑥ 系统修复：运行蓝牙疑难解答（设置→系统→疑难解答→其他→蓝牙）。"
        },
        ["Windows 更新失败"] = new() {
            "① 看错误码：设置→Windows 更新→更新历史记录，记下失败更新的错误码（如 0x80070002）。",
            "② 重试用：点击重试；或重启电脑后再次检查更新。",
            "③ 清缓存：管理员 cmd 依次执行 net stop wuauserv、net stop cryptSvc、net stop bits、net stop msiserver，删除 C:\\Windows\\SoftwareDistribution 后重启服务。",
            "④ 修组件：管理员 cmd 执行 DISM /Online /Cleanup-Image /RestoreHealth 再 sfc /scannow。",
            "⑤ 手动装：到 Microsoft 更新目录(catalog.update.microsoft.com)按 KB 编号手动下载安装。",
            "⑥ 仍失败：用设置→恢复→重置此电脑（保留文件）作为最后手段。"
        },
        ["打印机无法打印"] = new() {
            "① 查状态：设置→蓝牙和其他设备→打印机和扫描仪，确认打印机在线、无暂停/脱机/错误。",
            "② 清队列：打开打印队列，取消所有卡住的任务（文档→取消所有文档）。",
            "③ 重启用：重启打印机；电脑端设备管理器禁用再启用对应打印端口/驱动。",
            "④ 重装驱动：到打印机官网下载对应型号驱动重装；USB 换接口/换线排除接触问题。",
            "⑤ 设默认：确认目标打印机已设为系统默认打印机。",
            "⑥ 网络打印：电脑与打印机需在同一网段；无线打印检查 Wi‑Fi 连接是否正常。"
        },
        ["USB 设备不识别"] = new() {
            "① 换接口：插机箱后置 USB（供电更足）；前置面板/集线器供电不足是常见原因。",
            "② 换线/设备：排除线材损坏，插别的 USB 设备确认是接口还是设备问题。",
            "③ 重启用：设备管理器→通用串行总线控制器→右键 USB 根集线器禁用再启用或卸载重装。",
            "④ 查驱动：带黄色感叹号的设备→更新或回滚驱动。",
            "⑤ 供电：移动硬盘需双头 USB 或外接供电，避免过长延长线。",
            "⑥ 关节能：设备管理器→USB 根集线器→电源管理→取消允许计算机关闭此设备以节电。"
        },
        ["摄像头 / 麦克风用不了"] = new() {
            "① 查隐私：设置→隐私和安全性→相机/麦克风，确认允许桌面应用访问且未对具体 App 关闭。",
            "② 查占用：确认没有其他软件（会议/录屏）正独占摄像头，关闭后再试。",
            "③ 重启用：设备管理器→图像设备/音频输入→禁用再启用，或卸载重装驱动。",
            "④ 查默认：在会议/录音软件里选对输入设备（正确的摄像头与麦克风）。",
            "⑤ 驱动更新：到官网装最新摄像头/声卡驱动。",
            "⑥ 外置设备：USB 摄像头换接口；笔记本若有物理遮挡开关确认已开。"
        },
        ["Windows 激活 / 激活失效"] = new() {
            "① 看状态：设置→系统→激活，查看错误码（如 0xC004F074）。",
            "② 重试用：点击疑难解答让系统自助修复，连网后等片刻自动激活。",
            "③ 查密钥：确认使用与当前版本匹配的密钥；换硬件可能需重新绑定数字许可证。",
            "④ 用命令：管理员 cmd 执行 slmgr /ipk 密钥 与 slmgr /ato 手动激活（仅当你有合法密钥）。",
            "⑤ 电话激活：按提示选通过电话激活，按语音步骤输入安装 ID。",
            "⑥ 仍失败：联系设备厂商（品牌机内置数字许可证）或 Microsoft 支持。"
        },
        ["C 盘空间不足 / 飘红"] = new() {
            "① 看占用：此电脑右键 C 盘→属性→磁盘清理，勾选临时文件/回收站/更新缓存清理。",
            "② 用本软件：运行系统清理一键清理 C 盘垃圾与更新残留。",
            "③ 移大文件：把桌面/下载/视频移到其他盘；微信/QQ 文件存储路径改到其他盘。",
            "④ 关休眠：管理员 cmd 执行 powercfg -h off 释放与内存等大的 hiberfil.sys。",
            "⑤ 卸载冗余：控制面板→程序和功能，卸载不用的软件。",
            "⑥ 扩分区：用磁盘管理压缩相邻分区后扩展 C 盘，操作前务必备份数据。"
        },
        ["程序无法安装 / 安装报错"] = new() {
            "① 看错误码：记下安装程序弹出的错误码/日志路径，针对性搜索。",
            "② 装运行库：先装 Visual C++ Redistributable、.NET Framework、DirectX 等常见前置。",
            "③ 以管理员：右键安装包→以管理员身份运行，排除权限问题。",
            "④ 关冲突：临时关闭杀毒/防火墙，结束占用相关文件的进程后重试。",
            "⑤ 清残留：用官方卸载工具或第三方卸载器清旧版本残留注册表再装。",
            "⑥ 兼容模式：右键安装包→属性→兼容性→勾以兼容模式运行旧版 Windows。"
        },
        ["Wi‑Fi 频繁掉线"] = new() {
            "① 查信号：靠近路由器，排除距离/墙体遮挡；2.4G 穿墙好但慢，5G 快但需近。",
            "② 改信道：登录路由器后台，把 2.4G 信道固定为 1/6/11 之一避开邻居干扰。",
            "③ 关节能：设备管理器→网络适配器→电源管理→取消允许计算机关闭此设备以节电。",
            "④ 更驱动：到网卡/电脑官网更新无线网卡驱动。",
            "⑤ 重连：忘记该网络后重新输入密码连接，或重启路由器。",
            "⑥ 查干扰：远离微波炉/蓝牙/无线键鼠等 2.4G 干扰源，必要时换网线直连。"
        },
        ["键盘 / 鼠标失灵"] = new() {
            "① 查连接：有线换 USB 口；无线换电池/重新对码接收器。",
            "② 测其他：插别的键鼠确认是设备还是接口问题。",
            "③ 重启用：设备管理器→键盘/鼠标→禁用再启用，或卸载后重启重装驱动。",
            "④ 关筛选键：设置→辅助功能→键盘，确认筛选键/粘滞键未误开。",
            "⑤ 驱动：到官网装最新键鼠/芯片组驱动；游戏鼠标装官方驱动。",
            "⑥ 仍失灵：外接设备借换测试；笔记本内置失灵多为硬件送修。"
        }
    };

    // N8 故障类别 → 相关事件日志来源关键词（用于在事件日志概览中定位/滚动）
    private static readonly Dictionary<string, string[]> CategoryEventSources = new()
    {
        ["无法上网"] = new[] { "Tcpip", "Dhcp", "DNS", "NetBT", "WLAN", "WinHttp" },
        ["电脑卡顿"] = new[] { "Application Error", "ESENT", "DistributedCOM" },
        ["蓝屏 / 重启"] = new[] { "BugCheck", "Windows Error Reporting", "Kernel-Power", "WHEA" },
        ["没有声音"] = new[] { "Audio", "Audiosrv" },
        ["软件闪退"] = new[] { "Application Error", ".NET Runtime", "Windows Error Reporting" },
        ["风扇很吵 / 发热"] = new[] { "Kernel-Power" },
        ["开机慢 / 启动慢"] = new[] { "Winlogon", "Service Control Manager" },
        ["屏幕 / 显示异常"] = new[] { "Display", "nvlddmkm" },
        ["蓝牙 / 外设连不上"] = new[] { "Bluetooth", "BTH" },
        ["Windows 更新失败"] = new[] { "WindowsUpdate", "CBS" },
        ["打印机无法打印"] = new[] { "Print", "Microsoft-Windows-PrintService" },
        ["USB 设备不识别"] = new[] { "USB", "USBHUB" },
        ["摄像头 / 麦克风用不了"] = new[] { "Camera", "Audiosrv" },
        ["Windows 激活 / 激活失效"] = new[] { "Windows Activation", "Software Protection" },
        ["C 盘空间不足 / 飘红"] = new[] { "ESENT", "CBS" },
        ["程序无法安装 / 安装报错"] = new[] { "MsiInstaller", "Application Error" },
        ["Wi‑Fi 频繁掉线"] = new[] { "WLAN", "Dhcp", "Netwtw" },
        ["键盘 / 鼠标失灵"] = new[] { "HID", "Keyboard", "Mouse" }
    };

    // 故障类别 → UI 展示用的 emoji 与一句话简介（优化分类卡片样式）
    private static readonly Dictionary<string, (string Emoji, string Desc)> CategoryMeta = new()
    {
        ["无法上网"] = ("🌐", "Wi‑Fi / 宽带 / 局域网无法连接"),
        ["电脑卡顿"] = ("🐢", "运行慢、卡死、占用高"),
        ["蓝屏 / 重启"] = ("💥", "频繁蓝屏、自动重启"),
        ["没有声音"] = ("🔇", "无声、设备未识别"),
        ["软件闪退"] = ("⚠️", "程序崩溃、报错退出"),
        ["风扇很吵 / 发热"] = ("🌡️", "温度过高、风扇狂转"),
        ["开机慢 / 启动慢"] = ("⏱️", "开机/启动耗时过长"),
        ["屏幕 / 显示异常"] = ("🖥️", "花屏、黑屏、模糊"),
        ["蓝牙 / 外设连不上"] = ("🔵", "蓝牙、外设配对失败"),
        ["Windows 更新失败"] = ("⬇️", "补丁安装报错"),
        ["打印机无法打印"] = ("🖨️", "脱机、卡队列、驱动问题"),
        ["USB 设备不识别"] = ("🔌", "U 盘/设备无反应"),
        ["摄像头 / 麦克风用不了"] = ("📷", "无法调用相机/麦克风"),
        ["Windows 激活 / 激活失效"] = ("🔑", "激活失败、提示盗版"),
        ["C 盘空间不足 / 飘红"] = ("💽", "空间告急、清理扩容"),
        ["程序无法安装 / 安装报错"] = ("📦", "安装中断、缺运行库"),
        ["Wi‑Fi 频繁掉线"] = ("📶", "无线不稳、经常断流"),
        ["键盘 / 鼠标失灵"] = ("⌨️", "按键无反应、指针不动")
    };

    private enum View { Categories, Solution, EventLog }
    private View _view = View.Categories;
    private string _solCat = "";
    private string[]? _evHint;

    public TroubleshootWizardPage()
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += () => Dispatcher.Invoke(RenderCurrent);
        UiLanguage.Changed += () => Dispatcher.Invoke(() => { LocalizeStatic(); RenderCurrent(); });
        LocalizeStatic();
        RenderCategories();
    }

    /// <summary>语言切换时重设顶部静态标题（动态内容由 RenderCurrent 处理）</summary>
    private void LocalizeStatic()
    {
        TxtTitle.Text = UiLanguage.L("故障排查向导", "Troubleshooting Wizard");
        TxtSubtitle.Text = UiLanguage.L("选择你遇到的问题，按步骤自行排查",
            "Pick the problem you hit and follow the steps");
    }

    private void RenderCurrent()
    {
        switch (_view)
        {
            case View.Categories: RenderCategories(); break;
            case View.Solution: RenderSolution(_solCat); break;
            case View.EventLog: RenderEventLog(_evHint); break;
        }
    }

    private void RenderCategories()
    {
        _view = View.Categories;
        Body.Children.Clear();
        TxtStatus.Text = UiLanguage.L("请选择问题类别", "Select a problem category");

        // 分类以卡片网格展示（与装机助手/官网模块风格统一）
        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        foreach (var cat in Solutions.Keys)
        {
            CategoryMeta.TryGetValue(cat, out var meta);
            var emoji = meta.Emoji ?? "🔧";
            var desc = meta.Desc ?? "";

            var card = new Border
            {
                Width = 252,
                Margin = new Thickness(0, 0, 12, 12),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE7F6")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ClickAnim.SetIsEnabled(card, true);
            ClickAnim.SetHoverScale(card, 1.03);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = $"{emoji} {cat}",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
            });
            if (!string.IsNullOrEmpty(desc))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            card.Child = sp;

            var captured = cat;
            card.MouseLeftButtonUp += (s, ev) => { _solCat = captured; RenderSolution(captured); };
            wrap.Children.Add(card);
        }
        Body.Children.Add(wrap);

        // N8 入口：事件日志概览
        var evBtn = new Button
        {
            Content = "📋 " + UiLanguage.L("事件日志概览（近 72 小时错误/警告）", "Event Log Overview (errors/warnings, last 72h)"),
            Height = 44,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E44AD")),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 0, 16, 0)
        };
        evBtn.Click += (s, ev) => { _evHint = null; RenderEventLog(null); };
        Body.Children.Add(evBtn);
    }

    private void RenderSolution(string cat)
    {
        _view = View.Solution;
        _solCat = cat;
        Body.Children.Clear();
        TxtStatus.Text = UiLanguage.L("排查建议：", "Troubleshooting: ") + cat;

        var head = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE7F6")),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        head.Child = new TextBlock
        {
            Text = "针对「" + cat + "」的排查步骤：",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
        };
        Body.Children.Add(head);

        var panel = new StackPanel();
        int i = 1;
        foreach (var step in Solutions[cat])
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            row.Child = new TextBlock
            {
                Text = $"{i}. {step}",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50")),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(row);
            i++;
        }
        Body.Children.Add(panel);

        // N8 相关事件日志提示：跳转到概览并滚动定位相关来源
        if (CategoryEventSources.TryGetValue(cat, out var sources))
        {
            var hint = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3E5F5")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var hintBtn = new Button
            {
                Content = "🔍 " + UiLanguage.L("查看相关事件日志", "View related event logs"),
                Height = 36,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E44AD")),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            hintBtn.Click += (s, ev) => { _evHint = sources; RenderEventLog(sources); };
            hint.Child = hintBtn;
            Body.Children.Add(hint);
        }

        var back = new Button
        {
            Content = "← " + UiLanguage.L("重新选择问题", "Back to categories"),
            Height = 38,
            Width = 160,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E44AD")),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        back.Click += (s, ev) => RenderCategories();
        Body.Children.Add(back);
    }

    // ===== N8 事件日志概览（WMI 读取 System/Application，只读，不修改日志）=====
    private void RenderEventLog(string[]? hintSources)
    {
        _view = View.EventLog;
        _evHint = hintSources;
        Body.Children.Clear();
        TxtStatus.Text = UiLanguage.L("事件日志概览", "Event Log Overview");

        var head = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE7F6")),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        head.Child = new TextBlock
        {
            Text = "📋 " + UiLanguage.L("事件日志概览（系统/应用程序，近 72 小时错误与警告）",
                                        "Event Log Overview (System/Application, errors & warnings, last 72h)"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50")),
            TextWrapping = TextWrapping.Wrap
        };
        Body.Children.Add(head);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var btnRefresh = new Button
        {
            Content = UiLanguage.L("刷新", "Refresh"),
            Height = 36,
            Width = 110,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E44AD")),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var btnBack = new Button
        {
            Content = "← " + UiLanguage.L("返回向导", "← Back to wizard"),
            Height = 36,
            Width = 150,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6")),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnRow.Children.Add(btnRefresh);
        btnRow.Children.Add(btnBack);
        Body.Children.Add(btnRow);

        var txtStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        Body.Children.Add(txtStatus);

        var evPanel = new StackPanel();
        Body.Children.Add(evPanel);

        btnRefresh.Click += (s, ev) => LoadEventLogs(evPanel, txtStatus, hintSources);
        btnBack.Click += (s, ev) => RenderCategories();

        // 进入即按需加载一次（非定时轮询）
        LoadEventLogs(evPanel, txtStatus, hintSources);
    }

    private void LoadEventLogs(StackPanel panel, TextBlock status, string[]? hintSources)
    {
        panel.Children.Clear();
        status.Text = UiLanguage.L("正在读取事件日志…", "Reading event logs…");
        try
        {
            var cutoff = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddHours(-72));
            var groups = new Dictionary<string, (int Count, List<string> Samples)>(StringComparer.OrdinalIgnoreCase);

            foreach (var log in new[] { "System", "Application" })
            {
                var q = $"SELECT * FROM Win32_NTLogEvent WHERE LogFile='{log}' AND (Type='Error' OR Type='Warning') AND TimeGenerated >= \"{cutoff}\"";
                using var searcher = new ManagementObjectSearcher(q);
                foreach (ManagementObject mo in searcher.Get())
                {
                    var src = mo["Source"]?.ToString() ?? UiLanguage.L("未知来源", "Unknown");
                    var msg = mo["Message"]?.ToString() ?? "";
                    if (msg.Length > 160) msg = msg.Substring(0, 160) + "…";

                    if (!groups.TryGetValue(src, out var g))
                    {
                        g = (0, new List<string>());
                        groups[src] = g;
                    }
                    g.Count++;
                    if (g.Samples.Count < 3 && !string.IsNullOrWhiteSpace(msg))
                        g.Samples.Add(msg);
                    groups[src] = g;
                }
            }

            if (groups.Count == 0)
            {
                status.Text = UiLanguage.L("近 72 小时内未发现错误或警告事件，或日志为空。",
                                            "No error/warning events in the last 72h, or logs are empty.");
                return;
            }

            status.Text = UiLanguage.L($"共 {groups.Count} 个来源报告错误/警告（近 72 小时）：",
                                        $"{groups.Count} sources reported errors/warnings (last 72h):");

            var ordered = groups.OrderByDescending(kv => kv.Value.Count).ToList();

            // 找出与当前故障类别相关的来源，用于滚动定位
            string? scrollTo = null;
            if (hintSources != null)
            {
                foreach (var hs in hintSources)
                {
                    foreach (var kv in ordered)
                    {
                        if (kv.Key.IndexOf(hs, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            scrollTo = kv.Key;
                            break;
                        }
                    }
                    if (scrollTo != null) break;
                }
            }

            FrameworkElement? scrollTarget = null;
            foreach (var kv in ordered)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 10),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF0F3")),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                var sp = new StackPanel();
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
                });
                var cnt = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C")),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(8, 0, 0, 0)
                };
                cnt.Child = new TextBlock
                {
                    Text = kv.Value.Count.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                title.Children.Add(cnt);
                sp.Children.Add(title);

                foreach (var sample in kv.Value.Samples)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "• " + sample,
                        FontSize = 11,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                        Margin = new Thickness(0, 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                card.Child = sp;
                panel.Children.Add(card);
                if (scrollTo != null && kv.Key == scrollTo) scrollTarget = card;
            }

            if (scrollTarget != null)
            {
                scrollTarget.BringIntoView();
            }
        }
        catch (Exception ex)
        {
            status.Text = UiLanguage.L("读取事件日志失败（无权限或日志不可用）：", "Failed to read event logs (no permission or unavailable): ") + ex.Message;
        }
    }
}
