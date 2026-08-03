using System.Collections.Generic;

namespace WINHELP
{
    /// <summary>
    /// 装机助手（SetupPage）的单个软件 / 官网条目。
    /// 把原先写在 SetupPage.xaml 里的卡片硬编码为 C# 实例，便于人工增删改与维护。
    /// </summary>
    public class SiteEntry
    {
        /// <summary>显示名称（含 emoji 图标）</summary>
        public string Name { get; }
        /// <summary>中文描述</summary>
        public string DescZh { get; }
        /// <summary>英文描述</summary>
        public string DescEn { get; }
        /// <summary>官方网址</summary>
        public string Url { get; }

        public SiteEntry(string name, string descZh, string descEn, string url)
        {
            Name = name;
            DescZh = descZh;
            DescEn = descEn;
            Url = url;
        }
    }

    /// <summary>
    /// 装机助手的一个分类（如「主板官网」），含中英标题与条目列表。
    /// </summary>
    public class SiteGroup
    {
        public string TitleZh { get; }
        public string TitleEn { get; }
        public IReadOnlyList<SiteEntry> Items { get; }

        public SiteGroup(string titleZh, string titleEn, params SiteEntry[] items)
        {
            TitleZh = titleZh;
            TitleEn = titleEn;
            Items = items;
        }
    }

    /// <summary>
    /// 装机助手全部软件 / 官网分类清单（C# 实例数据）。
    /// <para>新增 / 调整装机推荐软件，只需在此文件编辑，无需改动 SetupPage.xaml。</para>
    /// </summary>
    public static class SiteCatalog
    {
        public static readonly IReadOnlyList<SiteGroup> Groups = new SiteGroup[]
        {
            // ============ 通讯社交 ============
            new("💬 通讯社交", "💬 Communication and Social",
                new SiteEntry("💬 QQ",          "腾讯即时通讯软件",            "Tencent instant messaging app",                 "https://im.qq.com"),
                new SiteEntry("💚 微信",         "国民社交 / 移动支付",          "Social networking / mobile payments",            "https://weixin.qq.com"),
                new SiteEntry("🏢 企业微信",      "企业沟通与协同办公",          "Enterprise communication and collaboration",     "https://work.weixin.qq.com"),
                new SiteEntry("📞 钉钉",         "阿里巴巴团队协作平台",        "Alibaba team collaboration platform",            "https://www.dingtalk.com"),
                new SiteEntry("🚀 飞书",         "字节跳动办公协作套件",        "ByteDance office collaboration suite",           "https://www.feishu.cn"),
                new SiteEntry("📺 哔哩哔哩",      "年轻人喜爱的视频社区",        "Video community loved by young people",          "https://www.bilibili.com"),
                new SiteEntry("🎵 抖音",         "记录美好生活的短视频",        "Short videos to record beautiful moments",       "https://www.douyin.com")),

            // ============ 驱动与硬件 ============
            new("🖥️ 驱动与硬件", "🖥️ Drivers and Hardware",
                new SiteEntry("🟢 NVIDIA 驱动", "英伟达显卡官方驱动下载",      "Official NVIDIA GPU driver download",            "https://www.nvidia.com/Download/index.aspx"),
                new SiteEntry("🔴 AMD 驱动",    "AMD 显卡 / 处理器驱动",       "AMD GPU / processor drivers",                    "https://www.amd.com/zh-hans/support"),
                new SiteEntry("🔵 Intel 驱动",  "Intel 处理器 / 核显驱动",     "Intel processor / integrated GPU drivers",       "https://www.intel.cn/content/www/cn/zh/download-center"),
                new SiteEntry("🔊 Realtek 声卡", "瑞昱声卡 / 网卡驱动",         "Realtek audio / network card drivers",           "https://www.realtek.com/Download/List?cate_id=593"),
                new SiteEntry("🧰 图吧工具箱",    "硬件检测 / 系统工具箱合集",    "Hardware detection / system tools collection",    "https://www.tbtool.cn/"),
                new SiteEntry("🇨🇳 联想官网",     "联想电脑驱动 / 售后支持",      "Lenovo PC drivers / after-sales support",        "https://www.lenovo.com.cn"),
                new SiteEntry("⬛ 戴尔官网",      "戴尔电脑驱动 / 技术支持",      "Dell PC drivers / technical support",            "https://www.dell.com")),

            // ============ 主板官网 ============
            // 新增更多主板厂商官网（Task #3）：映泰 / 昂达 / 梅捷 / 精英 / 影驰 / 索泰
            new("🔧 主板官网", "🔧 Motherboard Official Sites",
                new SiteEntry("🟦 华硕 ASUS",    "主板 / 显卡 / 整机官网",      "Motherboard / GPU / PC official site",           "https://www.asus.com.cn"),
                new SiteEntry("🔴 微星 MSI",     "主板 / 游戏本 / 外设官网",    "Motherboard / gaming laptop / gear",             "https://cn.msi.com"),
                new SiteEntry("🟢 技嘉 GIGABYTE", "主板 / 显卡官网",             "Motherboard / GPU official site",                "https://www.gigabyte.cn"),
                new SiteEntry("🟠 华擎 ASRock",  "主板 / 迷你主机官网",         "Motherboard / mini PC official site",            "https://www.asrock.com"),
                new SiteEntry("🌈 七彩虹 Colorful", "显卡 / 主板官网",          "GPU / motherboard official site",                "https://www.colorful.cn"),
                new SiteEntry("🟡 铭瑄 Maxsun",  "显卡 / 主板官网",             "GPU / motherboard official site",                "https://www.maxsun.com.cn"),
                new SiteEntry("🟣 映泰 Biostar", "主板 / 工控官网",             "Motherboard / industrial PC official site",      "https://www.biostar.com.cn"),
                new SiteEntry("🔵 昂达 ONDA",    "主板 / 显卡官网",             "Motherboard / GPU official site",                "https://www.onda.cn"),
                new SiteEntry("🟤 梅捷 SOYO",    "主板官网",                    "Motherboard official site",                     "https://www.soyo.com.cn"),
                new SiteEntry("⬜ 精英 ECS",     "主板官网",                    "Motherboard official site",                     "https://www.ecs.com.tw"),
                new SiteEntry("🟩 影驰 GALAX",   "显卡 / 主板官网",             "GPU / motherboard official site",                "https://www.galax.com"),
                new SiteEntry("🟧 索泰 ZOTAC",   "显卡 / 主板官网",             "GPU / motherboard official site",                "https://www.zotac.com")),

            // ============ 浏览器与办公 ============
            new("🌐 浏览器与办公", "🌐 Browsers and Office",
                new SiteEntry("🟡 Google Chrome", "全球最流行的浏览器",        "The most popular browser in the world",          "https://www.google.cn/chrome"),
                new SiteEntry("🟦 Microsoft Edge", "Win11 内置 Chromium 内核",  "Built-in Chromium engine in Windows 11",         "https://www.microsoft.com/edge"),
                new SiteEntry("🦊 Firefox",      "开源隐私友好浏览器",          "Open-source privacy-friendly browser",           "https://www.mozilla.org/zh-CN/firefox"),
                new SiteEntry("📄 Microsoft 365", "Word / Excel / PowerPoint",  "Word / Excel / PowerPoint",                      "https://www.microsoft.com/microsoft-365"),
                new SiteEntry("📝 WPS Office",   "国产免费办公套件",            "Free domestic office suite",                     "https://www.wps.cn"),
                new SiteEntry("📦 7-Zip",        "开源免费压缩 / 解压",         "Open-source free compression / extraction",      "https://www.7-zip.org"),
                new SiteEntry("⌨️ 搜狗输入法",    "智能词库中文输入法",          "Smart-dictionary Chinese input method",          "https://pinyin.sogou.com"),
                new SiteEntry("🎥 腾讯会议",      "高清流畅的视频会议",          "HD smooth video conferencing",                   "https://meeting.tencent.com")),

            // ============ 影音与工具 ============
            new("🎬 影音与工具", "🎬 Media and Tools",
                new SiteEntry("🎮 Steam",        "全球最大 PC 游戏平台",        "The largest PC gaming platform in the world",    "https://store.steampowered.com"),
                new SiteEntry("🐯 WeGame",       "腾讯游戏平台",                "Tencent gaming platform",                       "https://www.wegame.com.cn"),
                new SiteEntry("🎥 PotPlayer",    "轻量强大的视频播放器",        "Lightweight and powerful video player",          "https://potplayer.daum.net"),
                new SiteEntry("🎵 网易云音乐",    "音乐 / 歌单 / 播客",          "Music / playlists / podcasts",                  "https://music.163.com"),
                new SiteEntry("🎶 QQ 音乐",      "腾讯正版音乐平台",            "Tencent licensed music platform",                "https://y.qq.com"),
                new SiteEntry("✂️ 剪映",         "字节跳动免费视频剪辑",        "ByteDance free video editing",                  "https://www.capcut.cn"),
                new SiteEntry("☁️ 百度网盘",      "文件存储 / 备份 / 分享",      "File storage / backup / sharing",               "https://pan.baidu.com"),
                new SiteEntry("⚡ 夸克浏览器",    "阿里智能极速浏览器",          "Alibaba smart fast browser",                    "https://www.quark.cn"),
                new SiteEntry("🎬 OBS Studio",   "开源直播 / 录屏软件",         "Open-source live streaming / screen recording", "https://obsproject.com")),
        };
    }
}
