using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace WINHELP
{
    /// <summary>
    /// 公共清理工具类 —— 供「一键优化」(O6)、定时计划(N5)、还原点(N7)、隐私痕迹(N13)、
    /// 磁盘 Treemap(N2) 等多个模块复用，避免各页面重复实现文件扫描/删除逻辑。
    /// 所有方法对异常均静默吞掉，避免单文件失败阻断整批清理。
    /// </summary>
    public static class Cleaner
    {
        // ===== 路径工具 =====

        public static IEnumerable<string> TempDirs()
        {
            yield return Path.GetTempPath();
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        }

        public static IEnumerable<string> BrowserCacheDirs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache");
            yield return Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache");
            yield return Path.Combine(local, "Google", "Chrome", "User Data", "Default", "GPUCache");
            yield return Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache");
            yield return Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache");
            yield return Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "GPUCache");
            yield return Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache");
            yield return Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache");
        }

        /// <summary>隐私痕迹目录（最近文档 / 跳转列表 / 历史记录等）</summary>
        public static IEnumerable<string> PrivacyTraceDirs()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "Microsoft", "Windows", "Recent");
            yield return Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations");
            yield return Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WebCache");
        }

        public static IEnumerable<string> UpdateCacheDirs()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DataStore");
        }

        public static IEnumerable<string> ThumbnailCacheDirs()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
        }

        // ===== 统计 / 删除 =====

        public static (long size, int count) SumMatching(IEnumerable<string> dirs, string pattern, SearchOption opt)
        {
            long size = 0; int count = 0;
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, pattern, opt))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (fi.Exists) { size += fi.Length; count++; }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return (size, count);
        }

        /// <summary>判断路径是否为重解析点（junction / 符号链接 / 挂载点），是则返回 true —— 安全：删除前必须跳过，
        /// 否则递归删除会跟随链接删除目标目录中的真实文件（安全审计建议 P2）。</summary>
        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }

        public static void DeleteDirs(IEnumerable<string> dirs, SearchOption opt)
        {
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                // 目标目录本身是重解析点 → 跳过，不跟随删除目标内容
                if (IsReparsePoint(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
                    {
                        try { var fi = new FileInfo(f); if (fi.Exists) fi.Delete(); } catch { }
                    }
                }
                catch { }
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        // 跳过子目录中的重解析点（junction / 符号链接），避免删除链接指向的真实数据
                        if (IsReparsePoint(d)) continue;
                        try { Directory.Delete(d, true); } catch { }
                    }
                }
                catch { }
            }
        }

        public static void DeleteMatching(IEnumerable<string> dirs, string pattern)
        {
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                if (IsReparsePoint(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 递归删除临时目录下的全部文件与子目录（含子目录，O6 一键优化用）。
        /// protectLargeBytes &gt; 0 时，单个大小或目录总大小超过该阈值的项目会被跳过
        /// （大文件保护：超过阈值的临时文件/目录不自动删除，交由用户手动确认后再处理）。
        /// </summary>
        public static long CleanTempRecursively(long protectLargeBytes = 0)
        {
            long freed = 0;
            foreach (var dir in TempDirs())
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (!fi.Exists) continue;
                            if (protectLargeBytes > 0 && fi.Length > protectLargeBytes) continue; // 大文件保护
                            freed += fi.Length; fi.Delete();
                        }
                        catch { }
                    }
                    foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        // 跳过重解析点（junction / 符号链接），避免递归删除链接指向的真实数据
                        if (IsReparsePoint(d)) continue;
                        try
                        {
                            long ds = DirSize(d);
                            if (protectLargeBytes > 0 && ds > protectLargeBytes) continue; // 大目录保护
                            freed += ds;
                            Directory.Delete(d, true);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return freed;
        }

        /// <summary>
        /// 扫描临时目录中超过阈值的单个大文件（仅 TopDirectoryOnly，不深入子目录），
        /// 用于一键优化前的大文件手动确认。返回 (路径, 大小) 列表，按大小降序。
        /// </summary>
        public static List<(string Path, long Size)> FindLargeTempFiles(long thresholdBytes)
        {
            var result = new List<(string, long)>();
            foreach (var dir in TempDirs())
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (fi.Exists && fi.Length >= thresholdBytes)
                                result.Add((fi.FullName, fi.Length));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return result;
        }

        private static long DirSize(string dir)
        {
            long s = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { var fi = new FileInfo(f); if (fi.Exists) s += fi.Length; } catch { }
                }
            }
            catch { }
            return s;
        }

        // ===== 回收站（Shell32） =====

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pinfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        public static (long size, long count) QueryRecycleBin()
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                if (SHQueryRecycleBin(null, ref info) == 0)
                    return (info.i64Size, info.i64NumItems);
            }
            catch { }
            return (0, 0);
        }

        /// <summary>清空回收站，返回释放字节数（O6 / N5 复用）。</summary>
        public static long EmptyRecycleBin()
        {
            var (size, _) = QueryRecycleBin();
            try
            {
                // 0x1=不确认 / 0x2=无进度条 / 0x4=无提示音
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            }
            catch { }
            return size;
        }

        // ===== 一键优化（O6） =====

        /// <summary>
        /// 一键优化 = 递归清理临时文件 + 清空回收站。返回释放字节数。
        /// protectLargeTempBytes &gt; 0 时跳过超过该阈值的临时文件/目录（大文件保护，需手动确认）；
        /// emptyRecycleBin=false 时跳过回收站清空（如用户未确认大体积回收站）。
        /// </summary>
        public static long OneClickOptimize(long protectLargeTempBytes = 0, bool emptyRecycleBin = true)
        {
            long freed = CleanTempRecursively(protectLargeTempBytes);
            if (emptyRecycleBin) freed += EmptyRecycleBin();
            return freed;
        }

        // ===== 隐私痕迹（N13） =====

        public static (long size, int count) QueryPrivacyTraces()
        {
            long size = 0; int count = 0;
            foreach (var dir in PrivacyTraceDirs())
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { var fi = new FileInfo(f); if (fi.Exists) { size += fi.Length; count++; } } catch { }
                    }
                }
                catch { }
            }
            return (size, count);
        }

        public static long CleanPrivacyTraces()
        {
            long freed = 0;
            foreach (var dir in PrivacyTraceDirs())
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { var fi = new FileInfo(f); if (fi.Exists) { freed += fi.Length; fi.Delete(); } } catch { }
                    }
                }
                catch { }
            }
            return freed;
        }

        // ===== 系统还原点（N7） =====

        /// <summary>
        /// 调用 WMI 创建系统还原点。需要相应权限（通常管理员）；失败返回 false，调用方应优雅降级。
        /// </summary>
        public static bool CreateSystemRestorePoint(string description)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                var path = new ManagementPath("SystemRestore");
                using var obj = new ManagementClass(scope, path, null);
                var inParams = obj.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["EventType"] = 100;      // BEGIN_SYSTEM_CHANGE
                inParams["RestorePointType"] = 0; // APPLICATION_INSTALL
                obj.InvokeMethod("CreateRestorePoint", inParams, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===== 磁盘 Treemap 扫描（N2） =====

        /// <summary>单个文件夹节点（供 Treemap 渲染）</summary>
        public class TreeNode
        {
            public string Name = "";
            public string FullPath = "";
            public long Size;
            public bool IsDirectory;
            public readonly List<TreeNode> Children = new();
        }

        /// <summary>安全上限：单个磁盘扫描最多保留的节点数，防止超大目录把内存打爆导致进程异常退出。</summary>
        private const int MaxTreeNodes = 30000;

        /// <summary>
        /// 异步递归扫描指定目录，统计每个子目录/文件大小。
        /// maxDepth 限制递归深度避免超大目录卡死（默认 4，足以覆盖绝大多数"大目录"定位需求）；
        /// 扫描时会跳过重解析点（junction/符号链接）以避免循环引用与重复计数，并对节点总数封顶。
        /// cancellationToken 支持外部取消扫描（如切换驱动器时自动停止上一个盘的扫描）。
        /// </summary>
        public static async Task<TreeNode> ScanDirectoryAsync(string root, int maxDepth = 4, IProgress<int>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            var rootNode = new TreeNode { Name = Path.GetFileName(root) ?? root, FullPath = root, IsDirectory = true };
            int scanned = 0;
            int nodeCount = 1;
            await Task.Run(() => ScanInternal(rootNode, root, 0, maxDepth, ref scanned, ref nodeCount, progress, cancellationToken), cancellationToken);
            SortTree(rootNode);
            return rootNode;
        }

        private static void ScanInternal(TreeNode node, string path, int depth, int maxDepth, ref int scanned, ref int nodeCount, IProgress<int>? progress, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // 检查是否已取消
                if (cancellationToken.IsCancellationRequested) return;

                // 跳过重解析点（junction / 符号链接）：这类目录会指向其它位置，
                // 递归进入会造成循环引用与重复计数，甚至让树无限膨胀把内存打爆。
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                }
                catch { }

                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var fi = new FileInfo(f);
                        if (!fi.Exists) continue;
                        node.Children.Add(new TreeNode
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            Size = fi.Length,
                            IsDirectory = false
                        });
                        node.Size += fi.Length;
                        scanned++;
                        if (scanned % 200 == 0) progress?.Report(scanned);
                    }
                    catch { }
                }

                if (depth < maxDepth && !cancellationToken.IsCancellationRequested)
                {
                    foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        try
                        {
                            // 跳过重解析点（junction / 符号链接）
                            try
                            {
                                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                            }
                            catch { }

                            // 节点数封顶：达到上限后不再深入，避免内存爆炸。
                            if (nodeCount >= MaxTreeNodes) continue;

                            var child = new TreeNode
                            {
                                Name = Path.GetFileName(d) ?? d,
                                FullPath = d,
                                IsDirectory = true
                            };
                            node.Children.Add(child);
                            nodeCount++;
                            ScanInternal(child, d, depth + 1, maxDepth, ref scanned, ref nodeCount, progress, cancellationToken);
                            node.Size += child.Size;
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        private static void SortTree(TreeNode node)
        {
            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
            foreach (var c in node.Children) SortTree(c);
        }
    }
}
