using System;
using System.Collections.Generic;
using System.Linq;

namespace WINHELP
{
    /// <summary>
    /// 术语词典（v5.0.0 新增）：把专业术语翻译成一句普通用户能看懂的解释。
    /// 普通模式下 <see cref="Hint(string)"/> 会在原始文本后追加"（术语：一句话解释）"；
    /// 专业模式返回原文。解释保持一句话，集中维护、按需扩充。
    /// </summary>
    public static class Glossary
    {
        /// <summary>术语键（小写，子串匹配）→（通俗中文解释，通俗英文解释），不含括号包装</summary>
        private static readonly Dictionary<string, (string Zh, string En)> Terms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sfc"]            = ("Windows 自带的系统文件修复工具", "System File Checker"),
            ["dism"]           = ("修复 Windows 系统映像的命令", "Deployment Imaging Service and Management"),
            ["winsxs"]         = ("Windows 组件存储，存放系统文件的文件夹", "Windows Side-by-Side component store"),
            ["netsh"]          = ("网络配置命令，用来重置网络设置", "Network Shell"),
            ["minidump"]       = ("蓝屏记录文件，保存蓝屏时内存信息供分析原因", "Minidump file"),
            ["pid"]            = ("进程标识号，区分每个运行程序的数字编号", "Process ID"),
            ["hiberfil"]       = ("休眠文件，系统休眠时保存内存内容的隐藏文件", "Hibernation file"),
            ["softwaredistribution"] = ("系统更新临时文件夹，下载更新内容的缓存", "Windows Update download cache"),
            ["eventvwr"]       = ("查看系统日志的工具", "Event Viewer"),
            ["msconfig"]       = ("管理开机启动项的系统设置面板", "System Configuration"),
            ["taskmgr"]        = ("查看进程、性能的系统工具", "Task Manager"),
            ["regedit"]        = ("Windows 设置的底层数据库，谨慎修改", "Registry Editor"),
            ["services.msc"]   = ("管理系统后台运行服务的工具", "Services manager"),
            ["hostname"]       = ("电脑名称，局域网中标识本机的名字", "Computer name"),
            ["dns"]            = ("把网址翻译成 IP 地址的机制", "Domain Name System"),
            ["ping"]           = ("网络连通测试，检查能否连上某个地址", "Network reachability test"),
            ["ipconfig"]       = ("查看本机 IP 地址的网络配置命令", "IP configuration"),
            ["tracert"]        = ("查看数据经过哪些网络节点的路径追踪工具", "Trace route"),
            ["powercfg"]       = ("管理休眠、电源计划的电源配置命令", "Power configuration"),
            ["wevtutil"]       = ("查看 Windows 日志的系统日志查询命令", "Windows Event Log utility"),
            ["netstat"]        = ("列出本机网络连接与端口的查看命令", "Network statistics"),
            ["driverquery"]    = ("列出已安装驱动程序的驱动列表命令", "Driver query"),
            ["dxdiag"]         = ("查看显卡与声音信息的 DirectX 诊断工具", "DirectX Diagnostic Tool"),
            ["bitlocker"]      = ("保护硬盘数据不被他人读取的磁盘加密功能", "Disk encryption"),
            ["uefi"]           = ("新一代主板启动固件，开机时最先运行的程序", "Unified Extensible Firmware Interface"),
            ["bios"]           = ("主板基本输入输出系统，开机时最先运行的程序", "Basic Input/Output System"),
            ["cpu"]            = ("中央处理器，电脑的“大脑”，负责计算", "Central Processing Unit"),
            ["ram"]            = ("内存，电脑的“临时工作台”，断电即清空", "Random Access Memory"),
            ["gpu"]            = ("显卡处理器，负责画面显示与图形计算", "Graphics Processing Unit"),
            ["ssd"]            = ("固态硬盘，读写快、无机械结构的新型硬盘", "Solid State Drive"),
            ["hdd"]            = ("机械硬盘，传统磁盘式硬盘，容量大、速度较慢", "Hard Disk Drive"),
            ["nvme"]           = ("高速硬盘接口，直连主板，速度远快于传统硬盘", "Non-Volatile Memory Express"),
            ["sata"]           = ("硬盘连接接口，硬盘/光驱通用", "Serial ATA"),
            ["wmi"]            = ("读取硬件/系统信息的 Windows 管理接口", "Windows Management Instrumentation"),
            ["hklm"]           = ("系统级注册表区域，影响整台电脑的设置", "HKEY_LOCAL_MACHINE"),
            ["hkcu"]           = ("当前用户注册表区域，只影响当前登录账号", "HKEY_CURRENT_USER"),
            ["runonce"]        = ("“仅运行一次”的启动项位置，开机执行一次后自动清除", "Run once startup key"),
            ["wow6432node"]    = ("64 位系统里兼容 32 位软件的注册表区域", "32-bit registry view"),
            ["startupapproved"]= ("任务管理器“启动”页面的启用/禁用状态记录", "Task Manager startup approval"),
            ["uac"]            = ("用户账户控制，改动系统设置时的授权提示", "User Account Control"),
            ["tmp"]            = ("临时文件，程序运行时的暂存数据，可安全清理", "Temporary files"),
            ["cache"]          = ("缓存，程序保存的临时数据，用于加快下次访问", "Cache"),
            ["driver"]         = ("驱动程序，让系统认识并指挥硬件的软件", "Driver"),
            ["partition"]      = ("硬盘分区，把一块硬盘划分为多个独立区域", "Disk partition"),
            ["registry"]       = ("注册表，Windows 的设置数据库，谨慎修改", "Registry"),
            ["firewall"]       = ("防火墙，控制程序能否访问网络的保护机制", "Firewall"),
            ["proxy"]          = ("代理服务器，中转网络请求的服务器", "Proxy server"),
            ["vpn"]            = ("虚拟专用网络，加密通道连接远程网络", "Virtual Private Network"),
            ["lan"]            = ("局域网，同一路由器下设备组成的本地网络", "Local Area Network"),
            ["mac"]            = ("网卡物理地址，每块网卡唯一的身份编号", "Media Access Control address"),
            ["ip"]             = ("网络地址，设备在网络中的门牌号", "Internet Protocol address"),
            ["tdp"]            = ("功耗上限，处理器标称的散热设计功耗", "Thermal Design Power"),
            ["bsod"]           = ("蓝屏，系统出现严重错误时的蓝色报错画面", "Blue screen of death"),
            ["crash"]          = ("崩溃，程序或系统异常停止运行", "Crash"),
            ["reboot"]         = ("重启，重新启动电脑", "Reboot"),
            ["antivirus"]      = ("杀毒软件，查杀病毒与恶意软件的安全工具", "Antivirus"),
        };

        /// <summary>
        /// 普通模式：若 raw 文本包含已知术语，则追加"（术语：解释）"；专业模式返回原文。
        /// 已含括号解释的文本不再重复追加（防嵌套）。
        /// </summary>
        public static string Hint(string raw)
        {
            if (UiMode.IsPro || string.IsNullOrEmpty(raw)) return raw;
            if (raw.Contains('（') && raw.Contains('）')) return raw;
            var hit = Terms.FirstOrDefault(kv => raw.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (hit.Key == null) return raw;
            return UiMode.IsPro ? raw
                : raw + UiLanguage.L("（" + hit.Value.Zh + "）", " (" + hit.Value.En + ")");
        }

        /// <summary>便捷别名：按当前界面语言返回文本</summary>
        public static string L(string zh, string en) => UiLanguage.L(zh, en);
    }
}
