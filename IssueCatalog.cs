using System;
using System.Collections.Generic;
using System.Text;

namespace WINHELP
{
    /// <summary>
    /// 一条可执行的修复动作。命令全部是编译期字面量常量，不接受任何用户输入拼接，
    /// 并在页面加载时统一注册进 <see cref="CommandRunner"/> 白名单。
    /// </summary>
    public sealed class FixAction
    {
        /// <summary>按钮文案（中文）</summary>
        public string LabelZh { get; }
        /// <summary>按钮文案（英文）</summary>
        public string LabelEn { get; }
        /// <summary>要执行的命令原文</summary>
        public string Command { get; }
        /// <summary>是否需要管理员权限（未提权时会弹 UAC）</summary>
        public bool NeedAdmin { get; }
        /// <summary>是否需要重启电脑才能生效</summary>
        public bool NeedReboot { get; }
        /// <summary>超时秒数</summary>
        public int TimeoutSec { get; }
        /// <summary>风险等级，决定确认框样式与徽标颜色</summary>
        public RiskLevel Risk { get; }
        /// <summary>副作用提示（中文），null 表示无额外副作用</summary>
        public string? WarnZh { get; }
        /// <summary>副作用提示（英文）</summary>
        public string? WarnEn { get; }

        public FixAction(string labelZh, string labelEn, string command,
            bool needAdmin = false, bool needReboot = false, int timeoutSec = 120,
            RiskLevel risk = RiskLevel.Safe, string? warnZh = null, string? warnEn = null)
        {
            LabelZh = labelZh;
            LabelEn = labelEn;
            Command = command;
            NeedAdmin = needAdmin;
            NeedReboot = needReboot;
            TimeoutSec = timeoutSec;
            Risk = risk;
            WarnZh = warnZh;
            WarnEn = warnEn;
        }

        /// <summary>按当前界面语言取按钮文案</summary>
        public string Label => UiLanguage.L(LabelZh, LabelEn);

        /// <summary>按当前界面语言取副作用提示，无则返回 null</summary>
        public string? Warn => WarnZh == null ? null : UiLanguage.L(WarnZh, WarnEn ?? WarnZh);
    }

    /// <summary>
    /// 一条常见问题：症状、成因、人工排查步骤，以及可选的一键修复动作。
    /// </summary>
    public sealed class IssueEntry
    {
        /// <summary>唯一标识（分类前缀-短名）</summary>
        public string Id { get; }
        /// <summary>图标 emoji</summary>
        public string Icon { get; }
        public string TitleZh { get; }
        public string TitleEn { get; }
        /// <summary>症状描述（用户视角）</summary>
        public string SymptomZh { get; }
        public string SymptomEn { get; }
        /// <summary>常见成因</summary>
        public string CauseZh { get; }
        public string CauseEn { get; }
        /// <summary>人工排查步骤</summary>
        public IReadOnlyList<string> StepsZh { get; }
        public IReadOnlyList<string> StepsEn { get; }
        /// <summary>可执行的修复动作，可为空（纯知识条目）</summary>
        public IReadOnlyList<FixAction> Fixes { get; }
        /// <summary>检索用别名、错误码、俗称</summary>
        public string Keywords { get; }
        /// <summary>所属分类 key，由 <see cref="IssueCategory"/> 构造时回填</summary>
        public string CategoryKey { get; internal set; } = "";
        /// <summary>预拼的小写检索串（中英文 + 关键词 + 命令）</summary>
        public string Haystack { get; }
        /// <summary>本条目的最高风险等级</summary>
        public RiskLevel Risk { get; }
        /// <summary>是否含需要管理员权限的修复动作</summary>
        public bool NeedAdmin { get; }

        public IssueEntry(string id, string icon, string titleZh, string titleEn,
            string symptomZh, string symptomEn, string causeZh, string causeEn,
            string[] stepsZh, string[] stepsEn, string keywords, params FixAction[] fixes)
        {
            Id = id;
            Icon = icon;
            TitleZh = titleZh;
            TitleEn = titleEn;
            SymptomZh = symptomZh;
            SymptomEn = symptomEn;
            CauseZh = causeZh;
            CauseEn = causeEn;
            StepsZh = stepsZh;
            StepsEn = stepsEn;
            Fixes = fixes;
            Keywords = keywords;

            var risk = RiskLevel.Safe;
            bool admin = false;
            var sb = new StringBuilder();
            sb.Append(titleZh).Append(' ').Append(titleEn).Append(' ')
              .Append(symptomZh).Append(' ').Append(symptomEn).Append(' ')
              .Append(keywords).Append(' ').Append(id);
            foreach (var f in fixes)
            {
                if (f.Risk > risk) risk = f.Risk;
                if (f.NeedAdmin) admin = true;
                sb.Append(' ').Append(f.Command);
            }
            Risk = risk;
            NeedAdmin = admin;
            Haystack = sb.ToString().ToLowerInvariant();
        }

        public string Title => UiLanguage.L(TitleZh, TitleEn);
        public string Symptom => UiLanguage.L(SymptomZh, SymptomEn);
        public string Cause => UiLanguage.L(CauseZh, CauseEn);
        public IReadOnlyList<string> Steps => UiLanguage.Current == Lang.En ? StepsEn : StepsZh;
    }

    /// <summary>问题分类（网络 / 系统 / 性能 / 外设 / 磁盘 / 账户）。</summary>
    public sealed class IssueCategory
    {
        public string Key { get; }
        public string Icon { get; }
        public string TitleZh { get; }
        public string TitleEn { get; }
        public IReadOnlyList<IssueEntry> Items { get; }

        public IssueCategory(string key, string icon, string titleZh, string titleEn, params IssueEntry[] items)
        {
            Key = key;
            Icon = icon;
            TitleZh = titleZh;
            TitleEn = titleEn;
            Items = items;
            foreach (var it in items) it.CategoryKey = key;
        }

        public string Title => UiLanguage.L(TitleZh, TitleEn);
    }

    /// <summary>
    /// 电脑与网络常见问题解决方案总目录（C# 实例数据）。
    /// <para>新增 / 调整问题条目只需编辑此文件，页面会自动渲染并纳入检索。</para>
    /// <para>安全约定：所有 <see cref="FixAction.Command"/> 都是编译期字面量，
    /// 通过 <see cref="EnsureRegistered"/> 注册为 <see cref="CommandRunner"/> 的精确匹配白名单，
    /// 不存在任何参数拼接，因此没有命令注入面。</para>
    /// </summary>
    public static class IssueCatalog
    {
        // ── 常用超时（秒） ──
        private const int TQuick = 60;     // 秒级命令
        private const int TNormal = 120;   // 常规
        private const int TLong = 900;     // chkdsk 扫描
        private const int TVeryLong = 1800; // sfc / DISM / 全盘杀毒

        public static readonly IReadOnlyList<IssueCategory> Categories = new IssueCategory[]
        {
            // ═══════════════ ① 网络与上网 ═══════════════
            new IssueCategory("net", "🌐", "网络与上网", "Network & Internet",

                new IssueEntry("net-dns", "🕸️",
                    "网页打不开但 QQ 微信能用", "Web pages fail but chat apps work",
                    "聊天软件、游戏都正常，唯独浏览器打不开网页，或只有某些网站打不开。",
                    "Chat apps and games work fine, but the browser cannot load pages, or only certain sites fail.",
                    "本机 DNS 缓存里存了过期或错误的解析记录，导致域名指向了失效的 IP。",
                    "The local DNS cache holds stale or wrong records, pointing domains at dead IP addresses.",
                    new[]{
                        "先用手机连同一个 Wi-Fi 试试，确认是电脑的问题还是宽带的问题。",
                        "换一个浏览器或用无痕窗口打开，排除浏览器缓存与插件干扰。",
                        "点下方一键清空 DNS 缓存，然后刷新网页重试。",
                        "若仍不行，继续看下面的「完全无法上网」与「DNS 解析异常」两条。"
                    },
                    new[]{
                        "Try the same Wi-Fi on your phone first to tell whether it is the PC or the broadband.",
                        "Open the site in another browser or a private window to rule out cache and extensions.",
                        "Click the button below to flush the DNS cache, then reload the page.",
                        "If it still fails, see the no-internet and DNS resolution entries below."
                    },
                    "dns 缓存 打不开 网页 浏览器 flushdns 解析",
                    new FixAction("清空 DNS 缓存", "Flush DNS cache", "ipconfig /flushdns",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-winsock", "🔌",
                    "完全无法上网 / 网络协议栈损坏", "No internet at all, broken network stack",
                    "网络图标显示已连接，但所有程序都上不了网；或装了加速器、VPN 后网络彻底瘫痪。",
                    "The network icon shows connected but nothing can reach the internet, often after installing a VPN or game accelerator.",
                    "第三方软件往 Winsock 里插入了分层协议（LSP），卸载不干净就会截断所有网络请求。",
                    "Third-party software injected a Layered Service Provider into Winsock and left it broken after uninstall.",
                    new[]{
                        "先确认路由器本身正常：手机连同一个网络能否上网。",
                        "回想最近是否装过 VPN、网游加速器、代理或安全软件，先把它彻底卸载。",
                        "点下方重置 Winsock，然后必须重启电脑。",
                        "重启后若还不行，再执行「重置 TCP/IP 协议栈」。"
                    },
                    new[]{
                        "Confirm the router itself works by testing the same network on your phone.",
                        "Recall any VPN, game accelerator, proxy or security suite installed recently and fully uninstall it.",
                        "Click the button below to reset Winsock, then reboot the PC.",
                        "If the problem persists after reboot, also run the TCP/IP stack reset."
                    },
                    "winsock lsp 断网 上不了网 vpn 加速器 协议栈",
                    new FixAction("重置 Winsock 网络协议", "Reset Winsock", "netsh winsock reset",
                        needAdmin: true, needReboot: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "会清除第三方软件注入的网络分层协议，VPN 或加速器可能需要重新安装。必须重启后生效。",
                        warnEn: "Removes third-party LSPs; your VPN or accelerator may need reinstalling. Requires a reboot.")),

                new IssueEntry("net-ipreset", "♻️",
                    "IP 配置异常 / 自动获取失败", "Invalid IP configuration",
                    "网络诊断提示「以太网没有有效的 IP 配置」，或手动改过 IP 后再也连不上网。",
                    "Windows diagnostics reports an invalid IP configuration, or the network broke after manually setting an IP.",
                    "TCP/IP 注册表项被改乱，或残留了错误的静态 IP、网关与子网掩码。",
                    "The TCP/IP registry entries are corrupted, or a wrong static IP and gateway are left over.",
                    new[]{
                        "先在网络设置里把 IPv4 改回自动获得 IP 地址和 DNS。",
                        "若仍报「没有有效的 IP 配置」，点下方重置 TCP/IP 协议栈。",
                        "执行完必须重启电脑，重启后网络会重新向路由器申请地址。",
                        "如果公司或学校要求固定 IP，重置后需要重新填一次。"
                    },
                    new[]{
                        "First set IPv4 back to obtain IP address and DNS automatically.",
                        "If it still reports an invalid IP configuration, reset the TCP/IP stack below.",
                        "Reboot afterwards so the PC requests a fresh address from the router.",
                        "If your office or school requires a static IP, you must re-enter it after the reset."
                    },
                    "ip 配置 无效 tcpip 重置 没有有效的ip配置",
                    new FixAction("重置 TCP/IP 协议栈", "Reset TCP/IP stack", "netsh int ip reset",
                        needAdmin: true, needReboot: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "会把手动设置的静态 IP 与 DNS 清回自动获取，公司或校园网需要重新配置。必须重启后生效。",
                        warnEn: "Static IP and DNS settings revert to automatic; office or campus networks need reconfiguring. Requires a reboot.")),

                new IssueEntry("net-apipa", "🔢",
                    "IP 地址变成 169.254 开头", "Address stuck at 169.254.x.x",
                    "查看网络详情时 IPv4 地址是 169.254 开头，网络显示感叹号且完全无法通信。",
                    "The IPv4 address starts with 169.254, the network shows a warning icon and nothing connects.",
                    "电脑没能从路由器的 DHCP 拿到地址，于是自己随机分配了一个链路本地地址（APIPA）。",
                    "The PC failed to get an address from the router DHCP and assigned itself a link-local APIPA address.",
                    new[]{
                        "先重启路由器和光猫，断电 30 秒再上电，这是最常见的原因。",
                        "检查网线是否插紧，换一个路由器 LAN 口试试。",
                        "点下方释放并重新获取 IP 地址。",
                        "若路由器 DHCP 被关闭或地址池已满，需要进路由器后台开启或扩大地址池。"
                    },
                    new[]{
                        "Power-cycle the router and modem for 30 seconds first, this is the most common cause.",
                        "Check the cable is firmly seated and try another LAN port.",
                        "Click below to release and renew the IP address.",
                        "If the router DHCP is disabled or its pool is full, fix that in the router admin page."
                    },
                    "169.254 apipa dhcp 获取不到ip 感叹号 自动专用地址",
                    new FixAction("释放并重新获取 IP", "Release and renew IP", "ipconfig /release && ipconfig /renew",
                        needAdmin: false, timeoutSec: TNormal, risk: RiskLevel.Caution,
                        warnZh: "执行过程中会短暂断网数秒，正在下载或开会时请先暂停。",
                        warnEn: "The network drops for a few seconds; pause downloads or meetings first.")),

                new IssueEntry("net-proxy", "🚧",
                    "能 ping 通却打不开网页", "Ping works but pages will not load",
                    "命令行 ping 网站正常有回应，但浏览器一直转圈或提示无法连接到代理服务器。",
                    "Ping replies normally but the browser hangs or reports it cannot connect to the proxy server.",
                    "系统级代理被科学上网工具、恶意软件或已卸载的软件残留设置，指向了一个不存在的代理。",
                    "A system-wide proxy was left behind by a proxy tool, malware or an uninstalled app, pointing nowhere.",
                    new[]{
                        "打开设置 → 网络和 Internet → 代理，把手动设置代理关掉。",
                        "检查浏览器自身的代理与插件设置。",
                        "点下方清除系统级 WinHTTP 代理。",
                        "若反复出现，用杀毒软件全盘扫描，可能是恶意软件反复写入。"
                    },
                    new[]{
                        "Open Settings, Network and Internet, Proxy, and turn off manual proxy setup.",
                        "Check the browser own proxy settings and extensions.",
                        "Click below to clear the system-wide WinHTTP proxy.",
                        "If it keeps coming back, run a full antivirus scan for malware rewriting it."
                    },
                    "代理 proxy 无法连接到代理服务器 winhttp ping通打不开",
                    new FixAction("清除系统代理设置", "Reset system proxy", "netsh winhttp reset proxy",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "会清除系统级代理配置，如果你正在使用需要代理的公司内网，之后需重新设置。",
                        warnEn: "Clears the system proxy; corporate networks that require a proxy must be reconfigured.")),

                new IssueEntry("net-cert", "🔐",
                    "浏览器提示证书错误或不安全", "Certificate or HTTPS errors in the browser",
                    "打开任何 HTTPS 网站都提示证书无效、不受信任或您的连接不是私密连接。",
                    "Every HTTPS site warns that the certificate is invalid, untrusted or the connection is not private.",
                    "系统时间与真实时间偏差过大，导致所有证书都被判定为尚未生效或已过期。",
                    "The system clock is far off, so every certificate looks not-yet-valid or expired.",
                    new[]{
                        "先看任务栏右下角的日期和时间是否正确，尤其是年份。",
                        "点下方强制与网络时间服务器同步。",
                        "若时间总是跑偏，多半是主板纽扣电池没电了，需要更换。",
                        "时间正确后仍报错，检查是否装了会拦截 HTTPS 的杀毒软件或代理。"
                    },
                    new[]{
                        "Check the date and time in the taskbar, especially the year.",
                        "Click below to force a sync with the internet time server.",
                        "If the clock keeps drifting, the motherboard CMOS battery is likely dead and needs replacing.",
                        "If errors persist with a correct clock, check for HTTPS-intercepting antivirus or proxies."
                    },
                    "证书 错误 https 不安全 时间 不是私密连接 net::err_cert",
                    new FixAction("同步网络时间", "Sync with internet time", "w32tm /resync",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-latency", "📉",
                    "网络延迟高 / 打游戏卡顿丢包", "High latency or packet loss",
                    "网页能打开但很慢，游戏里延迟忽高忽低、频繁卡顿或掉线。",
                    "Pages load slowly, games show spiking latency, stuttering or disconnects.",
                    "无线信号弱、信道拥挤、家里有人在大流量下载，或运营商线路质量差。",
                    "Weak Wi-Fi signal, a crowded channel, someone saturating the line, or poor ISP routing.",
                    new[]{
                        "先跑一次下方的连续 20 次 ping，观察延迟波动和丢包率。",
                        "平均延迟稳定在 30ms 内且 0 丢包，说明本地网络正常，问题在游戏服务器。",
                        "延迟忽高忽低多为无线干扰，改用网线直连测试。",
                        "有丢包则检查是否有人在下载，或联系运营商检测线路。"
                    },
                    new[]{
                        "Run the 20-packet ping below and watch the jitter and loss rate.",
                        "A steady sub-30ms average with zero loss means your local network is fine.",
                        "Wildly varying latency usually means Wi-Fi interference; test with a wired connection.",
                        "Packet loss means someone is saturating the line, or ask your ISP to check it."
                    },
                    "延迟 卡顿 丢包 ping 高延迟 游戏卡 网速慢",
                    new FixAction("测试网络延迟与丢包", "Test latency and loss", "ping -n 20 223.5.5.5",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-nslookup", "🔍",
                    "DNS 解析异常 / 域名被劫持", "DNS resolution failure or hijacking",
                    "特定网站打不开或跳转到广告页面，其他网站正常。",
                    "Specific sites fail or redirect to ad pages while others work fine.",
                    "运营商 DNS 故障或被劫持，把域名解析到了错误的服务器。",
                    "The ISP DNS is broken or hijacked, resolving domains to the wrong servers.",
                    new[]{
                        "点下方用阿里公共 DNS 做一次对照解析。",
                        "如果用公共 DNS 能解析出正常结果，说明是运营商 DNS 的问题。",
                        "在网络适配器属性里把 DNS 改成 223.5.5.5 与 119.29.29.29。",
                        "改完记得清空一次 DNS 缓存再测试。"
                    },
                    new[]{
                        "Run the lookup below against a public DNS server for comparison.",
                        "If the public DNS returns correct results, your ISP DNS is at fault.",
                        "Set DNS to 223.5.5.5 and 119.29.29.29 in the adapter properties.",
                        "Flush the DNS cache afterwards and test again."
                    },
                    "dns 劫持 污染 解析失败 nslookup 域名 跳转广告",
                    new FixAction("用公共 DNS 测试解析", "Test with public DNS", "nslookup www.baidu.com 223.5.5.5",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-wifi", "📶",
                    "Wi-Fi 频繁掉线或搜不到信号", "Wi-Fi keeps dropping or cannot find networks",
                    "无线网络时断时续，或干脆搜不到自家路由器的信号。",
                    "The wireless connection drops intermittently, or the home network does not appear at all.",
                    "信号弱、信道被邻居占满、网卡省电模式自动关闭无线，或驱动异常。",
                    "Weak signal, a crowded channel, the adapter power-saving mode, or a faulty driver.",
                    new[]{
                        "点下方查看当前无线连接的信号强度、信道与协商速率。",
                        "信号低于 60% 就该考虑靠近路由器或加中继。",
                        "在设备管理器里找到无线网卡 → 属性 → 电源管理，取消勾选允许关闭此设备以节约电源。",
                        "路由器后台把 2.4G 信道手动改成 1、6、11 中较空闲的一个。"
                    },
                    new[]{
                        "Run the command below to see signal strength, channel and negotiated rate.",
                        "Below 60 percent signal you should move closer or add a repeater.",
                        "In Device Manager open the Wi-Fi adapter, Power Management, and uncheck the power-saving option.",
                        "In the router admin page pin the 2.4G channel to whichever of 1, 6 or 11 is least crowded."
                    },
                    "wifi 无线 掉线 断网 信号弱 搜不到 信道",
                    new FixAction("查看无线连接状态", "Show wireless status", "netsh wlan show interfaces",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-hosts", "📄",
                    "特定网站打不开疑似 hosts 被篡改", "Certain sites blocked by a tampered hosts file",
                    "只有个别网站或某个软件的更新服务器连不上，换网络换设备都一样。",
                    "Only a few specific sites or one app update server are unreachable, on any network.",
                    "hosts 文件被破解补丁、优化软件或恶意程序写入了屏蔽条目。",
                    "The hosts file was modified by a crack, a tuning utility or malware to block those addresses.",
                    new[]{
                        "点下方查看当前 hosts 文件的全部内容。",
                        "正常情况下应该只有以井号开头的注释，没有多余的 IP 映射。",
                        "若发现可疑条目，用记事本以管理员身份打开该文件删除对应行并保存。",
                        "文件位置：C:\\Windows\\System32\\drivers\\etc\\hosts"
                    },
                    new[]{
                        "Run the command below to print the whole hosts file.",
                        "Normally it contains only comment lines starting with a hash symbol.",
                        "If you find suspicious entries, open the file in Notepad as administrator and remove those lines.",
                        "The file lives at C:\\Windows\\System32\\drivers\\etc\\hosts"
                    },
                    "hosts 篡改 屏蔽 打不开 破解补丁 host文件",
                    new FixAction("查看 hosts 文件内容", "Show hosts file", @"type %windir%\System32\drivers\etc\hosts",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("net-firewall", "🛡️",
                    "防火墙误拦截导致联机失败", "Firewall blocks games or local sharing",
                    "局域网联机、共享打印机、远程桌面连不上，关掉防火墙就正常。",
                    "LAN gaming, printer sharing or remote desktop fails, but works with the firewall off.",
                    "防火墙里积累了大量错误的自定义规则，或某次误点了阻止导致程序被永久拦截。",
                    "Accumulated wrong custom rules, or an accidental block choice permanently banned the app.",
                    new[]{
                        "先在设置里单独给目标程序放行，这是最稳妥的做法。",
                        "确认对方设备与本机在同一网段，且网络配置文件都是专用网络而非公用网络。",
                        "以上都无效再考虑下方的重置防火墙，这会清空所有自定义规则。",
                        "重置后需要重新给需要联网的程序放行。"
                    },
                    new[]{
                        "First allow the specific app through the firewall, which is the safest fix.",
                        "Make sure both devices are on the same subnet and the network profile is Private, not Public.",
                        "Only if that fails consider the reset below, which wipes all custom rules.",
                        "After the reset you must re-allow the apps that need network access."
                    },
                    "防火墙 拦截 局域网 共享 联机 firewall 远程桌面",
                    new FixAction("重置防火墙为默认策略", "Reset firewall to defaults", "netsh advfirewall reset",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Danger,
                        warnZh: "会清空全部自定义防火墙规则并恢复出厂默认，所有已放行的程序都要重新放行。此操作不可撤销。",
                        warnEn: "Wipes every custom firewall rule and restores defaults. All allowed apps must be re-added. This cannot be undone.")),

                new IssueEntry("net-ipconfig", "🧾",
                    "想查看完整网络配置信息", "Inspect the full network configuration",
                    "需要把 IP、网关、DNS、MAC 地址等信息发给网管或客服。",
                    "You need to send IP, gateway, DNS and MAC details to IT support.",
                    "属于日常排查取证，不是故障。",
                    "This is routine information gathering, not a fault.",
                    new[]{
                        "点下方一键导出全部网络适配器的详细配置。",
                        "重点看已连接适配器的 IPv4 地址、默认网关与 DNS 服务器三项。",
                        "输出可以直接在控制台里选中复制，粘贴给对方。"
                    },
                    new[]{
                        "Click below to dump the full configuration of every adapter.",
                        "Focus on the IPv4 address, default gateway and DNS servers of the connected adapter.",
                        "You can select and copy the output straight from the console."
                    },
                    "ipconfig 网络配置 网关 mac地址 查看ip",
                    new FixAction("查看完整网络配置", "Show full network config", "ipconfig /all",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe))
            ),

            // ═══════════════ ② 系统与更新 ═══════════════
            new IssueCategory("sys", "🛠️", "系统与更新", "System & Updates",

                new IssueEntry("sys-wu", "🔄",
                    "Windows 更新失败或卡在 0%", "Windows Update stuck or failing",
                    "更新一直卡在某个百分比不动，或反复提示更新失败并回滚。",
                    "Updates hang at a percentage, or repeatedly fail and roll back.",
                    "更新缓存目录 SoftwareDistribution 里的文件下载不完整或已损坏。",
                    "Files in the SoftwareDistribution cache are incomplete or corrupted.",
                    new[]{
                        "先确保磁盘剩余空间充足，系统更新至少需要 10GB 以上。",
                        "点下方重建更新缓存，命令会依次停止更新服务、改名缓存目录、再启动服务。",
                        "执行完重新进入设置 → Windows 更新，点检查更新重新下载。",
                        "若仍失败，先运行「系统文件损坏」条目里的 sfc 与 DISM 修复。"
                    },
                    new[]{
                        "Make sure you have plenty of free disk space, updates need at least 10GB.",
                        "Click below to rebuild the update cache: it stops the services, renames the cache folders and restarts them.",
                        "Then go back to Settings, Windows Update and check for updates again.",
                        "If it still fails, run the sfc and DISM repairs from the corrupted system files entry first."
                    },
                    "windows更新 更新失败 卡住 0x80 softwaredistribution 回滚",
                    new FixAction("重建 Windows 更新缓存", "Rebuild Windows Update cache",
                        @"net stop wuauserv && net stop bits && net stop cryptsvc && ren %SystemRoot%\SoftwareDistribution SoftwareDistribution.old && ren %SystemRoot%\System32\catroot2 catroot2.old && net start wuauserv && net start bits && net start cryptsvc",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Caution,
                        warnZh: "会重置更新缓存与更新历史记录，已下载但未安装的更新需要重新下载。不影响已安装的更新。",
                        warnEn: "Resets the update cache and history; downloaded-but-not-installed updates must be fetched again. Installed updates are unaffected.")),

                new IssueEntry("sys-sfc", "🩹",
                    "系统文件损坏导致功能异常", "Corrupted system files break Windows features",
                    "设置打不开、右键菜单卡死、某些系统功能报错找不到文件。",
                    "Settings will not open, right-click menus freeze, or features report missing files.",
                    "系统文件被误删、被优化软件清理，或断电导致写入不完整。",
                    "System files were deleted, purged by a cleaner tool, or damaged by a power loss during writes.",
                    new[]{
                        "点下方运行系统文件检查，全程需要 5 到 20 分钟，期间请勿关机。",
                        "扫描完成后看提示：找到并成功修复表示已解决，重启后生效。",
                        "若提示找到损坏但无法修复，接着运行下一条的 DISM 修复，然后再跑一次 sfc。",
                        "两者都修不好则考虑系统更新到最新版本或就地重装。"
                    },
                    new[]{
                        "Run the system file check below; it takes 5 to 20 minutes, do not power off.",
                        "If it reports that corrupt files were found and repaired, reboot to apply.",
                        "If it found corruption it could not fix, run the DISM repair next and then rerun sfc.",
                        "If both fail, consider updating to the latest build or an in-place repair install."
                    },
                    "sfc scannow 系统文件 损坏 修复 设置打不开",
                    new FixAction("扫描并修复系统文件", "Scan and repair system files", "sfc /scannow",
                        needAdmin: true, timeoutSec: TVeryLong, risk: RiskLevel.Caution,
                        warnZh: "预计耗时 5 到 20 分钟，期间请勿关机或强制结束。修复完成后建议重启。",
                        warnEn: "Takes 5 to 20 minutes. Do not power off or kill the process. Reboot after it finishes.")),

                new IssueEntry("sys-dism", "🧬",
                    "sfc 修不好 / 组件存储损坏", "sfc cannot fix it, component store is corrupt",
                    "运行 sfc 后提示发现损坏文件但无法修复其中某些文件。",
                    "sfc reports it found corrupt files but was unable to fix some of them.",
                    "系统用于自我修复的组件存储（WinSxS）本身也损坏了，需要先从微软服务器拉取干净副本。",
                    "The component store WinSxS used for self-repair is itself damaged and needs clean files from Microsoft.",
                    new[]{
                        "确保电脑已连接互联网，DISM 需要联网下载修复源。",
                        "点下方运行 DISM 在线修复，耗时通常 10 到 30 分钟。",
                        "进度条可能长时间停在 20% 或 62.3%，这是正常现象，请耐心等待。",
                        "DISM 完成后再运行一次 sfc /scannow，然后重启。"
                    },
                    new[]{
                        "Make sure the PC is online, DISM downloads repair sources from Microsoft.",
                        "Run the online DISM repair below, it usually takes 10 to 30 minutes.",
                        "The progress may sit at 20 or 62.3 percent for a long time, this is normal.",
                        "After DISM finishes, run sfc /scannow once more and reboot."
                    },
                    "dism restorehealth 组件存储 winsxs 修复 0x800f081f",
                    new FixAction("DISM 在线修复系统映像", "DISM online repair",
                        "DISM /Online /Cleanup-Image /RestoreHealth",
                        needAdmin: true, timeoutSec: TVeryLong, risk: RiskLevel.Caution,
                        warnZh: "需要联网，预计耗时 10 到 30 分钟。进度长时间停滞属正常现象，请勿中断。",
                        warnEn: "Requires internet, takes 10 to 30 minutes. Long pauses in progress are normal, do not interrupt.")),

                new IssueEntry("sys-checkhealth", "🩺",
                    "只想体检不想动系统", "Check health without repairing anything",
                    "怀疑系统有问题，但不想执行任何会修改系统的操作。",
                    "You suspect a problem but do not want to run anything that modifies the system.",
                    "属于只读检测，用于决定是否需要进一步修复。",
                    "A read-only check to decide whether deeper repair is warranted.",
                    new[]{
                        "点下方执行组件存储健康快速检查，只读不修改，几秒完成。",
                        "输出显示未检测到组件存储损坏表示系统映像健康。",
                        "若提示可修复，再去执行上一条的 DISM 在线修复。"
                    },
                    new[]{
                        "Run the quick component store health check below, read-only and takes seconds.",
                        "No component store corruption detected means the image is healthy.",
                        "If it says the store is repairable, run the DISM online repair above."
                    },
                    "dism checkhealth 体检 只读 检测 健康",
                    new FixAction("快速检查系统映像健康", "Quick image health check",
                        "DISM /Online /Cleanup-Image /CheckHealth",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Safe)),

                new IssueEntry("sys-store", "🏪",
                    "应用商店打不开或下载失败", "Microsoft Store will not open or download",
                    "点开应用商店一片空白、闪退，或下载一直卡在 0MB。",
                    "The Store shows a blank page, crashes, or downloads stall at zero bytes.",
                    "商店缓存损坏，或账户登录状态异常。",
                    "The Store cache is corrupt or the account sign-in state is broken.",
                    new[]{
                        "点下方清空商店缓存，命令执行后商店会自动重新打开。",
                        "若仍打不开，在设置 → 应用里找到 Microsoft Store，选高级选项 → 修复，再试重置。",
                        "确认系统时间正确，时间不对会导致商店无法验证证书。",
                        "检查是否登录了 Microsoft 账户，退出重新登录一次。"
                    },
                    new[]{
                        "Click below to clear the Store cache; the Store reopens automatically afterwards.",
                        "If it still fails, go to Settings, Apps, Microsoft Store, Advanced options, then Repair and Reset.",
                        "Verify the system clock is correct, a wrong time breaks certificate validation.",
                        "Sign out of your Microsoft account and sign back in."
                    },
                    "应用商店 microsoft store 打不开 下载失败 wsreset 闪退",
                    new FixAction("重置应用商店缓存", "Reset Store cache", "wsreset.exe",
                        needAdmin: false, timeoutSec: TNormal, risk: RiskLevel.Caution,
                        warnZh: "会清空商店下载缓存并自动重新打开商店窗口，不会卸载已安装的应用。",
                        warnEn: "Clears the Store download cache and reopens the Store. Installed apps are not removed.")),

                new IssueEntry("sys-time", "⏰",
                    "系统时间总是不准", "The system clock keeps drifting",
                    "每次开机时间都差好几分钟甚至几小时，手动改完过一会又变了。",
                    "The clock is off by minutes or hours on every boot and drifts back after fixing it.",
                    "时间同步服务未启动，或主板纽扣电池电量耗尽无法保存时间。",
                    "The time service is not running, or the motherboard CMOS battery is dead.",
                    new[]{
                        "点下方启动时间服务并强制同步。",
                        "同步成功后在设置 → 时间和语言里确认时区是 UTC+08:00 北京时间。",
                        "如果每次关机后时间都重置回很久以前，说明主板纽扣电池 CR2032 没电了，换一颗即可。",
                        "双系统用户时间差 8 小时属于正常现象，是 UTC 与本地时间标准不同导致。"
                    },
                    new[]{
                        "Click below to start the time service and force a resync.",
                        "Then confirm the time zone in Settings, Time and Language.",
                        "If the clock resets to a very old date on every boot, replace the CR2032 CMOS battery.",
                        "An exact 8-hour offset on dual-boot systems is normal, caused by the UTC versus local time convention."
                    },
                    "时间不对 时间同步 w32time 纽扣电池 时区",
                    new FixAction("启动时间服务并强制同步", "Start time service and resync",
                        "net start w32time && w32tm /resync /force",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("sys-gpupdate", "📋",
                    "策略修改后没有生效", "Policy changes are not taking effect",
                    "在组策略里改了设置，但重启后依然是旧的行为。",
                    "You changed a Group Policy setting but the old behaviour persists after reboot.",
                    "策略需要刷新才能应用，或被域策略覆盖。",
                    "Policies need a refresh to apply, or a domain policy overrides your local change.",
                    new[]{
                        "点下方强制刷新组策略。",
                        "部分策略需要注销或重启才能完全生效。",
                        "家庭版没有组策略编辑器，相关设置需要改注册表实现。",
                        "公司电脑的域策略优先级高于本地策略，本地改动会被覆盖。"
                    },
                    new[]{
                        "Click below to force a policy refresh.",
                        "Some policies still need a sign-out or reboot to fully apply.",
                        "Home edition has no Group Policy Editor; use the registry instead.",
                        "On a domain-joined PC, domain policy overrides your local settings."
                    },
                    "组策略 gpedit gpupdate 策略不生效 域",
                    new FixAction("强制刷新组策略", "Force policy refresh", "gpupdate /force",
                        needAdmin: false, timeoutSec: TNormal, risk: RiskLevel.Safe)),

                new IssueEntry("sys-safeboot", "🆘",
                    "卡在安全模式循环出不来", "Stuck in a safe mode boot loop",
                    "每次开机都自动进入安全模式，正常重启也没用。",
                    "The PC boots into safe mode every time, even after a normal restart.",
                    "启动配置里的 safeboot 标志被勾选后没有取消（常见于用 msconfig 勾了安全引导）。",
                    "The safeboot flag in the boot configuration was set and never cleared, often via msconfig.",
                    new[]{
                        "这是非常常见的求助场景：在 msconfig 里勾了安全引导后就再也进不去正常系统。",
                        "点下方删除启动配置里的 safeboot 标志。",
                        "执行成功后重启电脑即可恢复正常启动。",
                        "也可以运行 msconfig，在引导选项卡里取消勾选安全引导。"
                    },
                    new[]{
                        "This is a very common trap: ticking Safe boot in msconfig locks you into safe mode.",
                        "Click below to delete the safeboot flag from the boot configuration.",
                        "Reboot after it succeeds and Windows starts normally again.",
                        "You can also run msconfig and untick Safe boot on the Boot tab."
                    },
                    "安全模式 循环 出不来 safeboot msconfig 安全引导 bcdedit",
                    new FixAction("取消安全模式引导标志", "Clear safe boot flag",
                        "bcdedit /deletevalue {current} safeboot",
                        needAdmin: true, needReboot: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "修改的是启动配置，执行成功后需要重启才能恢复正常启动。",
                        warnEn: "Modifies the boot configuration; reboot afterwards to start normally.")),

                new IssueEntry("sys-bcdenum", "📑",
                    "想查看当前启动配置", "Inspect the current boot configuration",
                    "多系统引导顺序异常，或想确认是否开启了安全模式、测试签名等标志。",
                    "Multi-boot order looks wrong, or you want to check flags like safe mode or test signing.",
                    "属于只读排查，用于确认启动项状态。",
                    "Read-only inspection to confirm the boot entry state.",
                    new[]{
                        "点下方列出当前启动项的全部配置。",
                        "重点看 safeboot 是否存在，存在则说明被锁在安全模式。",
                        "testsigning 为 Yes 表示开启了测试模式，桌面右下角会显示水印。"
                    },
                    new[]{
                        "Click below to list the full configuration of the current boot entry.",
                        "Check whether a safeboot value exists, which means safe mode is forced.",
                        "testsigning set to Yes means test mode is on and a watermark shows on the desktop."
                    },
                    "bcdedit 启动配置 引导 多系统 测试模式 水印",
                    new FixAction("列出当前启动配置", "List boot configuration", "bcdedit /enum {current}",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Safe))
            ),

            // ═══════════════ ③ 性能与启动 ═══════════════
            new IssueCategory("perf", "🚀", "性能与启动", "Performance & Startup",

                new IssueEntry("perf-explorer", "🖥️",
                    "任务栏卡死 / 桌面无响应", "Taskbar frozen or desktop unresponsive",
                    "点开始菜单没反应、任务栏图标转圈、桌面右键卡住，但鼠标还能动。",
                    "The Start menu does not respond, taskbar icons spin, right-click hangs, but the mouse still moves.",
                    "资源管理器进程 explorer.exe 假死，通常由某个第三方右键菜单扩展或缩略图卡住引起。",
                    "The explorer.exe shell process hung, usually due to a third-party context menu extension or a stuck thumbnail.",
                    new[]{
                        "不要直接强制关机，那样容易损坏文件。",
                        "点下方重启资源管理器，桌面会黑屏一两秒然后恢复。",
                        "重启后若很快又卡死，回想最近装了什么带右键菜单的软件（网盘、压缩、下载工具）。",
                        "长期反复出现可用第三方工具禁用可疑的 Shell 扩展。"
                    },
                    new[]{
                        "Do not force a power-off, that risks file corruption.",
                        "Click below to restart Explorer; the desktop blacks out for a second and returns.",
                        "If it hangs again quickly, think about recently installed apps with context menu entries.",
                        "For recurring cases, disable suspicious shell extensions with a third-party tool."
                    },
                    "任务栏 卡死 无响应 桌面 explorer 开始菜单 假死",
                    new FixAction("重启资源管理器", "Restart Explorer",
                        "taskkill /f /im explorer.exe && start explorer.exe",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "桌面会短暂黑屏一到两秒，已打开的文件夹窗口会全部关闭，请先保存正在编辑的内容。",
                        warnEn: "The desktop blacks out briefly and all open folder windows close. Save your work first.")),

                new IssueEntry("perf-iconcache", "🖼️",
                    "桌面图标变成白纸或显示错乱", "Desktop icons show as blank pages",
                    "快捷方式图标全变成白色文档图标，或图标张冠李戴。",
                    "Shortcut icons turn into blank white document icons, or show the wrong image.",
                    "图标缓存数据库损坏，系统读不到正确的图标索引。",
                    "The icon cache database is corrupt and Windows cannot read the correct icon index.",
                    new[]{
                        "点下方重建图标缓存，命令会关闭资源管理器、删除缓存文件再重启。",
                        "执行完图标会重新逐个加载，稍等片刻即可恢复。",
                        "若个别快捷方式仍是白纸，说明它指向的目标程序已被卸载或移动。",
                        "重建后仍全部异常，可尝试重启电脑让系统彻底重建。"
                    },
                    new[]{
                        "Click below to rebuild the icon cache: it closes Explorer, deletes the cache and restarts it.",
                        "Icons then reload one by one, give it a moment.",
                        "If a single shortcut stays blank, its target program was uninstalled or moved.",
                        "If everything is still wrong, reboot so Windows rebuilds the cache from scratch."
                    },
                    "图标 白纸 缓存 iconcache 快捷方式 显示错乱",
                    new FixAction("重建图标缓存", "Rebuild icon cache",
                        @"taskkill /f /im explorer.exe && del /a /q ""%localappdata%\IconCache.db"" && del /a /f /q ""%localappdata%\Microsoft\Windows\Explorer\iconcache*"" && start explorer.exe",
                        needAdmin: false, timeoutSec: TNormal, risk: RiskLevel.Caution,
                        warnZh: "桌面会短暂黑屏，已打开的文件夹窗口会关闭。图标会重新逐个加载。",
                        warnEn: "The desktop blacks out briefly and open folder windows close. Icons reload one by one.")),

                new IssueEntry("perf-hiberfil", "💤",
                    "C 盘被休眠文件占用几个 G", "hiberfil.sys eats several gigabytes of C drive",
                    "C 盘空间莫名少了很多，根目录有个几个 G 的 hiberfil.sys 删不掉。",
                    "The C drive lost several gigabytes to an undeletable hiberfil.sys in the root.",
                    "系统休眠功能预留了与物理内存等大的磁盘空间用于保存内存快照。",
                    "Hibernation reserves disk space equal to your physical RAM to store the memory snapshot.",
                    new[]{
                        "16GB 内存的电脑，休眠文件通常也占 6 到 12GB。",
                        "如果你从不使用休眠功能，点下方关闭它即可立刻释放这部分空间。",
                        "注意：关闭休眠会同时关闭快速启动，开机速度可能略微变慢几秒。",
                        "想恢复的话执行 powercfg /h on 即可。"
                    },
                    new[]{
                        "On a 16GB machine the hibernation file typically takes 6 to 12GB.",
                        "If you never use hibernation, turn it off below to reclaim that space immediately.",
                        "Note that this also disables Fast Startup, so boot may take a few seconds longer.",
                        "Run powercfg /h on to bring it back."
                    },
                    "hiberfil c盘满 休眠 快速启动 空间不足 powercfg",
                    new FixAction("关闭休眠释放空间", "Disable hibernation", "powercfg /h off",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "会同时关闭休眠与快速启动功能，开机速度可能略慢。执行 powercfg /h on 可恢复。",
                        warnEn: "Also disables Fast Startup, so boot may be slightly slower. Run powercfg /h on to restore.")),

                new IssueEntry("perf-power", "⚡",
                    "游戏剪辑时性能被限制", "Performance is throttled during games or rendering",
                    "CPU 频率上不去、游戏帧数偏低，任务管理器显示 CPU 占用不高但就是卡。",
                    "The CPU will not boost, frame rates are low, and usage stays low while everything feels sluggish.",
                    "系统电源计划处于平衡或节能模式，主动限制了 CPU 睿频。",
                    "The power plan is set to Balanced or Power saver, which caps CPU boost.",
                    new[]{
                        "点下方切换到高性能电源计划。",
                        "台式机建议长期使用高性能；笔记本插电时用高性能，用电池时切回平衡。",
                        "游戏本还需在厂商自带的控制中心里切换性能模式并开启独显直连。",
                        "若切换后仍降频，检查散热是否积灰导致过热保护。"
                    },
                    new[]{
                        "Click below to switch to the High performance power plan.",
                        "Desktops can stay on High performance; laptops should use it while plugged in only.",
                        "Gaming laptops also need the vendor control centre set to performance mode.",
                        "If it still throttles, check for dust buildup causing thermal protection."
                    },
                    "性能 降频 卡顿 帧数低 电源计划 高性能 睿频",
                    new FixAction("切换到高性能电源计划", "Switch to High performance plan",
                        "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "笔记本使用电池时会明显缩短续航，建议插电时再切换。",
                        warnEn: "Noticeably shortens laptop battery life; prefer switching only while plugged in.")),

                new IssueEntry("perf-wake", "🌙",
                    "电脑自动唤醒或无法进入睡眠", "The PC wakes by itself or will not sleep",
                    "半夜电脑自己亮屏开机，或点睡眠后屏幕黑一下又立刻醒来。",
                    "The PC wakes up on its own at night, or wakes immediately after being put to sleep.",
                    "某个程序、计划任务或外设（鼠标、网卡）持有唤醒请求或阻止了睡眠。",
                    "An app, scheduled task or device such as the mouse or network adapter holds a wake request.",
                    new[]{
                        "点下方列出当前所有阻止睡眠的请求方。",
                        "常见元凶是播放器、下载工具、以及设置了唤醒定时器的计划任务。",
                        "外设唤醒可在设备管理器 → 该设备 → 电源管理里取消允许此设备唤醒计算机。",
                        "网卡唤醒（网络唤醒 WOL）也在同一处关闭。"
                    },
                    new[]{
                        "Click below to list everything currently blocking sleep.",
                        "Common culprits are media players, download managers and scheduled tasks with wake timers.",
                        "For devices, open Device Manager, the device, Power Management, and untick allow this device to wake the computer.",
                        "Wake-on-LAN for the network adapter is disabled in the same place."
                    },
                    "自动唤醒 睡眠 待机 唤醒 powercfg requests 不睡眠",
                    new FixAction("列出阻止睡眠的请求", "List sleep blockers", "powercfg /requests",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("perf-battery", "🔋",
                    "笔记本续航明显变差", "Laptop battery life has dropped noticeably",
                    "以前能用 6 小时，现在两三个小时就没电了。",
                    "It used to last six hours and now dies in two or three.",
                    "锂电池随充放电循环自然损耗，设计容量与实际满充容量的差距即为损耗程度。",
                    "Lithium batteries wear with charge cycles; the gap between design and full-charge capacity is the wear level.",
                    new[]{
                        "点下方生成电池健康报告，文件会保存到桌面。",
                        "用浏览器打开该 HTML 文件，找到 Installed batteries 一节。",
                        "对比 DESIGN CAPACITY（设计容量）与 FULL CHARGE CAPACITY（满充容量）。",
                        "满充容量低于设计容量的 70% 就该考虑更换电池了。"
                    },
                    new[]{
                        "Click below to generate a battery health report saved to your Desktop.",
                        "Open the HTML file in a browser and find the Installed batteries section.",
                        "Compare DESIGN CAPACITY with FULL CHARGE CAPACITY.",
                        "Below 70 percent of the design capacity it is time to replace the battery."
                    },
                    "电池 续航 损耗 batteryreport 健康 笔记本",
                    new FixAction("生成电池健康报告到桌面", "Generate battery report to Desktop",
                        @"powercfg /batteryreport /output ""%userprofile%\Desktop\battery-report.html""",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("perf-mem", "🧠",
                    "内存占用异常高", "Abnormally high memory usage",
                    "什么都没开内存就占了 80%，电脑越用越卡。",
                    "Memory sits at 80 percent with nothing open and the PC gets slower over time.",
                    "某个程序存在内存泄漏，或后台驻留了大量自启动程序。",
                    "An app is leaking memory, or too many startup programs are resident in the background.",
                    new[]{
                        "点下方列出占用内存超过 300MB 的所有进程。",
                        "重点看是否有你不认识的进程，或某个程序占用远超预期。",
                        "浏览器多标签页占内存高属正常，关闭不用的标签即可。",
                        "若某程序持续增长不释放，是典型内存泄漏，更新或重装该软件。"
                    },
                    new[]{
                        "Click below to list every process using more than 300MB.",
                        "Look for processes you do not recognise or apps using far more than expected.",
                        "High browser usage with many tabs is normal, just close unused tabs.",
                        "An app whose usage only grows is leaking memory; update or reinstall it."
                    },
                    "内存 占用高 泄漏 卡顿 tasklist 内存不足",
                    new FixAction("列出高内存占用进程", "List high-memory processes",
                        @"tasklist /fi ""memusage gt 300000"" /v",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe))
            ),

            // ═══════════════ ④ 外设与驱动 ═══════════════
            new IssueCategory("dev", "🖨️", "外设与驱动", "Devices & Drivers",

                new IssueEntry("dev-spooler", "🖨️",
                    "打印机脱机 / 点打印没反应", "Printer shows offline or nothing prints",
                    "打印机明明开着，电脑上却显示脱机；点打印后毫无动静。",
                    "The printer is powered on but shows as offline, and printing does nothing.",
                    "打印后台处理程序 Print Spooler 服务卡死。",
                    "The Print Spooler service has hung.",
                    new[]{
                        "先确认打印机电源、数据线或 Wi-Fi 连接正常，并且不是缺纸缺墨。",
                        "点下方重启打印后台服务。",
                        "重启服务后在设置里右键打印机，取消勾选脱机使用打印机。",
                        "网络打印机还需确认电脑与打印机在同一网段。"
                    },
                    new[]{
                        "First check the printer power, cable or Wi-Fi, and that it is not out of paper or ink.",
                        "Click below to restart the print spooler service.",
                        "Then right-click the printer in Settings and untick Use Printer Offline.",
                        "For network printers, confirm both devices are on the same subnet."
                    },
                    "打印机 脱机 打印 spooler 不打印 打印失败",
                    new FixAction("重启打印后台服务", "Restart Print Spooler",
                        "net stop spooler && net start spooler",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "重启服务期间正在打印的任务会中断。",
                        warnEn: "Any job currently printing will be interrupted.")),

                new IssueEntry("dev-printqueue", "🗑️",
                    "打印队列卡死删不掉", "Print queue is stuck and will not clear",
                    "队列里有一个正在删除的任务卡住不走，后面的文档全都印不出来。",
                    "A job stuck in deleting state blocks the queue and nothing else prints.",
                    "打印任务的临时文件损坏，服务无法正常处理或删除它。",
                    "The spool file for that job is corrupt so the service can neither print nor delete it.",
                    new[]{
                        "先试上一条的重启打印服务，多数情况就能解决。",
                        "仍卡住则点下方清空打印队列，这会删除全部待打印任务。",
                        "清空后重新提交打印。",
                        "若反复卡死，考虑重装打印机驱动。"
                    },
                    new[]{
                        "Try restarting the spooler from the previous entry first, that usually clears it.",
                        "If it is still stuck, clear the queue below, which deletes all pending jobs.",
                        "Then submit the print job again.",
                        "If it keeps jamming, reinstall the printer driver."
                    },
                    "打印队列 卡住 正在删除 清空 删不掉 printers",
                    new FixAction("强制清空打印队列", "Force clear print queue",
                        @"net stop spooler && del /f /q %systemroot%\System32\spool\PRINTERS\*.* && net start spooler",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Danger,
                        warnZh: "会删除全部等待中的打印任务，包括别人提交到这台电脑共享打印机的任务。此操作不可撤销。",
                        warnEn: "Deletes every pending print job, including those queued by others on a shared printer. This cannot be undone.")),

                new IssueEntry("dev-audio", "🔇",
                    "电脑突然没有声音", "Sound suddenly stopped working",
                    "音量图标正常、没有静音，但扬声器和耳机都没声音。",
                    "The volume icon looks fine and nothing is muted, yet neither speakers nor headphones produce sound.",
                    "音频服务异常，或默认播放设备被切换到了不存在的设备（如已拔掉的 HDMI 显示器）。",
                    "The audio service crashed, or the default playback device switched to something absent such as an unplugged HDMI monitor.",
                    new[]{
                        "先点右下角音量图标，确认输出设备选的是扬声器而不是 HDMI 或已断开的耳机。",
                        "点下方重启 Windows 音频服务。",
                        "仍无声则在设备管理器里卸载声卡驱动后重启，系统会自动重装。",
                        "笔记本插耳机没声音多为驱动的插孔检测问题，重装原厂声卡驱动可解决。"
                    },
                    new[]{
                        "Click the volume icon and check the output device is the speakers, not HDMI or a disconnected headset.",
                        "Click below to restart the Windows Audio service.",
                        "If still silent, uninstall the audio driver in Device Manager and reboot to auto-reinstall.",
                        "No sound from the headphone jack usually means a driver jack-detection issue; reinstall the OEM audio driver."
                    },
                    "没声音 声音 音频 扬声器 耳机 audiosrv 静音",
                    new FixAction("重启音频服务", "Restart audio service",
                        "net stop /y audiosrv && net start audiosrv",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "会连带重启依赖音频的服务，正在播放的音视频会中断。",
                        warnEn: "Dependent services also restart and any playing audio or video is interrupted.")),

                new IssueEntry("dev-bluetooth", "📲",
                    "蓝牙搜不到或连不上设备", "Bluetooth cannot find or pair devices",
                    "蓝牙开关正常但搜不到耳机鼠标，或配对后立刻断开。",
                    "Bluetooth is on but no headset or mouse appears, or it disconnects right after pairing.",
                    "蓝牙支持服务异常，或设备处于已配对但未连接的僵死状态。",
                    "The Bluetooth support service is stuck, or the device is paired but in a dead not-connected state.",
                    new[]{
                        "先让目标设备进入配对模式（多数耳机需长按电源键至指示灯快闪）。",
                        "在设置里把该设备删除，然后点下方重启蓝牙服务。",
                        "服务重启后重新搜索并配对。",
                        "始终搜不到任何设备，检查设备管理器里蓝牙适配器是否有黄色感叹号。"
                    },
                    new[]{
                        "Put the target device into pairing mode first, usually a long press until the LED flashes fast.",
                        "Remove the device in Settings, then restart the Bluetooth service below.",
                        "Search and pair again after the service restarts.",
                        "If nothing is ever found, check the Bluetooth adapter in Device Manager for a warning icon."
                    },
                    "蓝牙 连不上 搜不到 配对 bthserv 断开",
                    new FixAction("重启蓝牙服务", "Restart Bluetooth service",
                        "net stop bthserv && net start bthserv",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Caution,
                        warnZh: "已连接的蓝牙设备会全部断开，需要重新连接。",
                        warnEn: "All connected Bluetooth devices disconnect and must reconnect.")),

                new IssueEntry("dev-usb", "🔌",
                    "USB 设备不识别 / 黄色感叹号", "USB device not recognised",
                    "插上 U 盘或外设后提示无法识别的 USB 设备，设备管理器里有黄色感叹号。",
                    "Plugging in a USB device shows an unrecognised device error with a warning icon in Device Manager.",
                    "驱动缺失或损坏，或 USB 口供电不足。",
                    "A missing or broken driver, or insufficient power from the port.",
                    new[]{
                        "先换一个 USB 口测试，优先用主板后置 USB 口而非机箱前置口。",
                        "移动硬盘等大功率设备不要接 USB 集线器，直插主板。",
                        "点下方列出所有存在问题的设备及其错误码。",
                        "针对报错设备，在设备管理器里右键卸载设备后重新插拔，让系统重装驱动。"
                    },
                    new[]{
                        "Try another port first, preferring rear motherboard ports over front-panel ones.",
                        "Plug power-hungry devices such as portable drives directly into the board, not a hub.",
                        "Click below to list every problem device with its error code.",
                        "For each one, uninstall it in Device Manager and replug so Windows reinstalls the driver."
                    },
                    "usb 无法识别 黄色感叹号 驱动 设备管理器 u盘",
                    new FixAction("列出所有问题设备", "List problem devices", "pnputil /enum-devices /problem",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Safe))
            ),

            // ═══════════════ ⑤ 存储与磁盘 ═══════════════
            new IssueCategory("disk", "💽", "存储与磁盘", "Storage & Disk",

                new IssueEntry("disk-temp", "🧹",
                    "C 盘飘红 / 临时文件堆积", "C drive is full of temporary files",
                    "C 盘变红，可用空间只剩几个 G，清空回收站也没用。",
                    "The C drive turns red with only a few gigabytes left, and emptying the Recycle Bin does not help.",
                    "软件安装包、更新缓存与临时文件长期堆积在临时目录里没有清理。",
                    "Installers, update caches and temp files pile up in the temp directory and are never cleaned.",
                    new[]{
                        "点下方清空当前用户的临时文件目录。",
                        "正在被程序占用的文件会自动跳过，属正常现象。",
                        "还想深度清理可用本工具箱的系统清理模块，能一并处理更新缓存与大文件。",
                        "关闭休眠也能释放数 GB，见性能分类里的对应条目。"
                    },
                    new[]{
                        "Click below to empty the current user temp directory.",
                        "Files currently in use are skipped automatically, which is normal.",
                        "For a deeper clean use the System Cleaner module, which also handles update caches and large files.",
                        "Disabling hibernation frees several more gigabytes, see the performance category."
                    },
                    "c盘满 飘红 临时文件 temp 清理 空间不足",
                    new FixAction("清空临时文件目录", "Clear temp directory",
                        @"del /f /s /q ""%temp%\*.*""",
                        needAdmin: false, timeoutSec: TNormal, risk: RiskLevel.Caution,
                        warnZh: "会删除临时目录下的全部文件。正在被程序使用的文件会自动跳过，不影响已安装软件。",
                        warnEn: "Deletes everything in the temp directory. Files in use are skipped and installed software is unaffected.")),

                new IssueEntry("disk-chkdsk", "🔎",
                    "怀疑硬盘有坏道", "Suspect the disk has bad sectors",
                    "复制文件时卡住或报错、开机偶尔提示正在检查磁盘、系统频繁无故卡顿。",
                    "File copies stall or error out, boot sometimes runs a disk check, or the system freezes randomly.",
                    "磁盘出现逻辑错误或物理坏道，读写时反复重试导致卡顿。",
                    "Logical errors or physical bad sectors cause repeated read retries and stalls.",
                    new[]{
                        "先点下方做只读扫描，不会修改任何数据，安全无风险。",
                        "看输出末尾是否报告坏扇区（bad sectors）数量大于 0。",
                        "有坏道且是机械硬盘，立刻备份重要数据，硬盘随时可能彻底损坏。",
                        "确认有错误后再执行下一条的修复，注意修复需要重启且耗时很长。"
                    },
                    new[]{
                        "Run the read-only scan below first, it changes nothing and is completely safe.",
                        "Check the end of the output for a bad sector count greater than zero.",
                        "If a mechanical drive has bad sectors, back up important data immediately.",
                        "Only after confirming errors run the repair below, which needs a reboot and takes a long time."
                    },
                    "硬盘 坏道 chkdsk 磁盘错误 检查磁盘 卡顿",
                    new FixAction("只读扫描 C 盘", "Read-only scan of C drive", "chkdsk C:",
                        needAdmin: true, timeoutSec: TLong, risk: RiskLevel.Safe,
                        warnZh: "只读扫描，不会修改任何数据。大容量硬盘可能需要数分钟。",
                        warnEn: "Read-only scan that modifies nothing. Large drives may take several minutes.")),

                new IssueEntry("disk-chkdskfix", "🔧",
                    "磁盘错误需要修复", "Repair confirmed disk errors",
                    "只读扫描已确认存在文件系统错误或坏扇区，需要实际修复。",
                    "The read-only scan confirmed file system errors or bad sectors that need fixing.",
                    "文件系统元数据损坏，需要 chkdsk 独占磁盘进行修复。",
                    "File system metadata is damaged and chkdsk needs exclusive access to repair it.",
                    new[]{
                        "务必先备份重要数据，修复过程中断电有丢数据的风险。",
                        "点下方安排开机磁盘修复，系统盘无法在运行时修复，只能排到下次开机。",
                        "重启后会在开机画面执行，机械硬盘可能耗时数小时，请预留时间。",
                        "修复期间绝对不能断电或强制关机。"
                    },
                    new[]{
                        "Back up important data first, a power loss during repair risks data loss.",
                        "Click below to schedule the repair; the system drive can only be fixed at boot.",
                        "It runs on the next boot screen and may take hours on a mechanical drive.",
                        "Never cut power or force a shutdown while it runs."
                    },
                    "chkdsk 修复 坏道 磁盘错误 开机检查 f r",
                    new FixAction("安排开机修复磁盘", "Schedule disk repair at boot",
                        "echo Y| chkdsk C: /f /r",
                        needAdmin: true, needReboot: true, timeoutSec: TNormal, risk: RiskLevel.Danger,
                        warnZh: "会在下次开机时独占执行磁盘修复，机械硬盘可能耗时数小时且期间无法使用电脑。请务必先备份重要数据。",
                        warnEn: "Runs an exclusive disk repair at next boot that may take hours on a mechanical drive. Back up important data first.")),

                new IssueEntry("disk-health", "❤️‍🩹",
                    "想查看硬盘健康状态与型号", "Check disk health and model",
                    "想确认电脑装的是固态还是机械硬盘，以及健康状况是否正常。",
                    "You want to know whether the drive is an SSD or HDD and whether it is healthy.",
                    "属于日常检查，用于评估是否需要更换硬盘。",
                    "Routine inspection to decide whether the drive needs replacing.",
                    new[]{
                        "点下方列出所有物理磁盘的型号、介质类型、健康状态与容量。",
                        "MediaType 为 SSD 表示固态硬盘，HDD 表示机械硬盘。",
                        "HealthStatus 应为 Healthy，若显示 Warning 或 Unhealthy 请立刻备份数据。",
                        "更详细的通电时间与坏块统计需要用第三方 SMART 检测工具查看。"
                    },
                    new[]{
                        "Click below to list every physical disk with model, media type, health and size.",
                        "MediaType SSD means solid state, HDD means mechanical.",
                        "HealthStatus should read Healthy; Warning or Unhealthy means back up immediately.",
                        "For power-on hours and reallocated sectors use a third-party SMART tool."
                    },
                    "硬盘 健康 型号 ssd 机械硬盘 smart 检测",
                    new FixAction("查看硬盘健康与型号", "Show disk health and model",
                        @"powershell -NoProfile -Command ""Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,Size | Format-List""",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Safe)),

                new IssueEntry("disk-vss", "🗂️",
                    "系统还原点占用大量空间", "Restore points are eating disk space",
                    "C 盘空间莫名被占用，但找不到大文件，系统保护里显示占用几十 G。",
                    "The C drive is full but no large files are visible; System Protection shows tens of gigabytes used.",
                    "系统还原点与卷影副本会长期占用磁盘配额。",
                    "Restore points and volume shadow copies hold a long-term disk quota.",
                    new[]{
                        "点下方查看各盘卷影副本的已用空间与最大配额。",
                        "在系统属性 → 系统保护里可以调小最大使用量，或删除旧还原点。",
                        "建议保留至少一个还原点以备系统故障时回滚。",
                        "配额建议设为磁盘容量的 3% 到 5%。"
                    },
                    new[]{
                        "Click below to show the used space and maximum quota of shadow copies per volume.",
                        "In System Properties, System Protection you can shrink the max usage or delete old points.",
                        "Keep at least one restore point so you can roll back after a failure.",
                        "A quota of 3 to 5 percent of the drive size is a reasonable setting."
                    },
                    "还原点 系统保护 卷影副本 vssadmin 占用空间",
                    new FixAction("查看还原点占用空间", "Show shadow copy usage", "vssadmin list shadowstorage",
                        needAdmin: true, timeoutSec: TQuick, risk: RiskLevel.Safe))
            ),

            // ═══════════════ ⑥ 账户与安全 ═══════════════
            new IssueCategory("acct", "🔐", "账户与安全", "Account & Security",

                new IssueEntry("acct-activate", "🏷️",
                    "提示系统未激活", "Windows is not activated",
                    "桌面右下角出现激活水印，个性化设置被锁定无法修改。",
                    "An activation watermark appears and personalisation settings are locked.",
                    "许可证过期、硬件更换导致数字许可失效，或从未激活过。",
                    "The licence expired, a hardware change invalidated the digital licence, or it was never activated.",
                    new[]{
                        "点下方查看当前激活状态与到期时间。",
                        "显示永久激活表示已正确激活，水印可能是残留，重启即可。",
                        "更换过主板的话，登录 Microsoft 账户后用设置里的激活疑难解答重新绑定。",
                        "请通过正规渠道购买许可证，不要使用来路不明的激活工具，风险极高。"
                    },
                    new[]{
                        "Click below to check the current activation state and expiry.",
                        "Permanently activated means it is fine and the watermark is just a leftover; reboot to clear it.",
                        "After a motherboard change, sign in with your Microsoft account and use the activation troubleshooter.",
                        "Buy licences through official channels and avoid unknown activation tools, they are high risk."
                    },
                    "激活 未激活 水印 slmgr 许可证 正版",
                    new FixAction("查看激活状态与到期时间", "Check activation status",
                        @"cscript //nologo %windir%\System32\slmgr.vbs /xpr",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("acct-license", "📜",
                    "想查看激活详细信息与版本", "View detailed licence information",
                    "需要确认系统版本、许可证类型与部分产品密钥信息。",
                    "You need the edition, licence type and partial product key.",
                    "属于日常查询，常用于售后或重装前的信息确认。",
                    "Routine lookup, typically before a reinstall or when contacting support.",
                    new[]{
                        "点下方查看详细许可证信息。",
                        "描述里含 RETAIL 为零售版，OEM 为随机版，VOLUME 为批量授权版。",
                        "OEM 版绑定主板，换主板后需要重新购买；零售版可迁移到新电脑。"
                    },
                    new[]{
                        "Click below to view the detailed licence information.",
                        "RETAIL means a retail licence, OEM is preinstalled, VOLUME is volume licensing.",
                        "OEM licences are tied to the motherboard; retail licences can move to a new PC."
                    },
                    "激活信息 许可证 版本 slmgr dli 零售版 oem",
                    new FixAction("查看许可证详细信息", "Show licence details",
                        @"cscript //nologo %windir%\System32\slmgr.vbs /dli",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe)),

                new IssueEntry("acct-defupd", "🦠",
                    "病毒库过期无法更新", "Antivirus definitions are outdated",
                    "安全中心提示病毒和威胁防护定义已过期。",
                    "Windows Security reports that virus and threat protection definitions are out of date.",
                    "更新服务异常或长期未联网，导致特征库落后。",
                    "The update service failed or the PC was offline for a long time.",
                    new[]{
                        "确保电脑已联网。",
                        "点下方手动触发病毒库更新。",
                        "若装了第三方杀毒软件，Defender 会自动关闭，此时提示可忽略。",
                        "更新失败可先执行系统分类里的重建 Windows 更新缓存。"
                    },
                    new[]{
                        "Make sure the PC is online.",
                        "Click below to trigger a manual definition update.",
                        "If a third-party antivirus is installed, Defender turns off and the warning can be ignored.",
                        "If the update fails, first rebuild the Windows Update cache from the system category."
                    },
                    "病毒库 defender 过期 更新 安全中心 特征库",
                    new FixAction("更新病毒特征库", "Update virus definitions",
                        @"""%ProgramFiles%\Windows Defender\MpCmdRun.exe"" -SignatureUpdate",
                        needAdmin: true, timeoutSec: TNormal, risk: RiskLevel.Safe)),

                new IssueEntry("acct-scan", "🔬",
                    "怀疑中毒想快速查杀", "Suspect malware and want a quick scan",
                    "电脑莫名变慢、弹广告、浏览器主页被改、出现陌生进程。",
                    "The PC slowed down, shows pop-up ads, the browser home page changed, or unknown processes appeared.",
                    "可能感染了广告软件、挖矿木马或浏览器劫持程序。",
                    "Possible adware, a mining trojan or a browser hijacker.",
                    new[]{
                        "点下方运行 Defender 快速扫描，检查内存与常见感染路径。",
                        "快速扫描通常 5 到 15 分钟，扫描期间可以继续使用电脑。",
                        "发现威胁后在安全中心里选择删除或隔离。",
                        "快速扫描没问题但症状仍在，在安全中心执行完全扫描或 Microsoft Defender 脱机扫描。"
                    },
                    new[]{
                        "Click below to run a Defender quick scan of memory and common infection paths.",
                        "A quick scan takes 5 to 15 minutes and you can keep using the PC.",
                        "Remove or quarantine anything it finds via Windows Security.",
                        "If symptoms persist after a clean quick scan, run a full or offline scan from Windows Security."
                    },
                    "中毒 病毒 查杀 木马 广告 defender 扫描 劫持",
                    new FixAction("Defender 快速扫描", "Defender quick scan",
                        @"""%ProgramFiles%\Windows Defender\MpCmdRun.exe"" -Scan -ScanType 1",
                        needAdmin: true, timeoutSec: TVeryLong, risk: RiskLevel.Safe,
                        warnZh: "预计耗时 5 到 15 分钟，扫描期间可正常使用电脑。",
                        warnEn: "Takes 5 to 15 minutes; you can keep using the PC while it runs.")),

                new IssueEntry("acct-users", "👥",
                    "想确认本机有哪些账户", "List the local accounts on this PC",
                    "怀疑被人偷偷建了账户，或忘记了本机管理员账户的名字。",
                    "You suspect a hidden account was created, or forgot the local administrator name.",
                    "属于安全自查，用于发现异常账户。",
                    "A security self-check to spot unexpected accounts.",
                    new[]{
                        "点下方列出本机全部用户账户。",
                        "Administrator、Guest、DefaultAccount、WDAGUtilityAccount 都是系统内置账户，属正常。",
                        "若发现不认识的账户，在计算机管理 → 本地用户和组里禁用或删除。",
                        "同时建议给自己的账户设置强密码并开启 Windows Hello。"
                    },
                    new[]{
                        "Click below to list every local user account.",
                        "Administrator, Guest, DefaultAccount and WDAGUtilityAccount are built-in and normal.",
                        "If you find an account you do not recognise, disable or delete it in Computer Management.",
                        "Also set a strong password on your own account and enable Windows Hello."
                    },
                    "账户 用户 net user 本机账户 安全 异常账户",
                    new FixAction("列出本机所有账户", "List local accounts", "net user",
                        needAdmin: false, timeoutSec: TQuick, risk: RiskLevel.Safe))
            ),
        };

        /// <summary>全部问题条目（按分类顺序摊平）。</summary>
        public static readonly IReadOnlyList<IssueEntry> AllIssues = Flatten();

        /// <summary>全部修复命令（去重），用于注册白名单。</summary>
        public static readonly IReadOnlyList<string> AllCommands = CollectCommands();

        private static bool _registered;

        /// <summary>
        /// 把本目录中的全部命令注册进 <see cref="CommandRunner"/> 白名单。幂等，可重复调用。
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            CommandRunner.RegisterAllowed(AllCommands);
        }

        /// <summary>按 Id 查找条目，不存在返回 null。</summary>
        public static IssueEntry? Find(string id)
        {
            foreach (var e in AllIssues)
                if (e.Id == id) return e;
            return null;
        }

        private static IReadOnlyList<IssueEntry> Flatten()
        {
            var list = new List<IssueEntry>();
            foreach (var c in Categories) list.AddRange(c.Items);
            return list;
        }

        private static IReadOnlyList<string> CollectCommands()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var e in AllIssues)
                foreach (var f in e.Fixes)
                    if (set.Add(f.Command)) list.Add(f.Command);
            return list;
        }
    }
}
