// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WINHELP
{
    /// <summary>
    /// 重复文件查找（v5.3.0 新增）：
    /// - 策略：仅比较「文件名 + 大小」相同 → 再用 SHA-256 逐文件校验，最大限度降低误报；
    /// - 安全：删除前两次确认（RiskGuard.ConfirmTwice），且一律移入回收站（可恢复）；
    /// - 每个分组保留 1 个文件，其余视为可删。
    /// </summary>
    public partial class DuplicateFilePage : UserControl
    {
        // 只扫描不小于该大小的文件（默认 1 MB），避免海量小文件拖垮扫描
        private const long MinBytes = 1L * 1024 * 1024;
        private CancellationTokenSource? _cts;

        public DuplicateFilePage()
        {
            InitializeComponent();
            Localize();
            UiLanguage.Changed += () => Dispatcher.Invoke(Localize);
        }

        private void Localize()
        {
            // 动态生成的按钮文本按需在构建时设置；静态 loc:Loc 已自动处理。
        }

        // ===== 选择目录 =====

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = UiLanguage.L("选择要扫描的文件夹", "Select the folder to scan"),
                ShowNewFolderButton = false,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtFolder.Text = dlg.SelectedPath;
            }
        }

        // ===== 扫描 =====

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            string root = TxtFolder.Text.Trim();
            if (root.Length < 2 || !Directory.Exists(root))
            {
                MessageBox.Show(
                    UiLanguage.L("请先选择一个存在的文件夹。", "Please choose an existing folder first."),
                    UiLanguage.L("提示", "Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 切到「取消」状态
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            BtnScan.Content = UiLanguage.L("取消", "Cancel");
            ProgressPanel.Visibility = Visibility.Visible;
            ScanProgress.Value = 0;
            TxtProgress.Text = UiLanguage.L("正在枚举文件…", "Enumerating files…");
            GroupsPanel.Children.Clear();
            TxtEmpty.Visibility = Visibility.Collapsed;
            TxtSummary.Text = "";
            BtnPick.IsEnabled = false;

            try
            {
                // 1) 枚举 + 分组
                var (groups, skipped) = await Task.Run(() => BuildCandidateGroups(root, ct), ct);
                if (groups.Count == 0)
                {
                    TxtSummary.Text = "";
                    TxtEmpty.Text = UiLanguage.L(
                        "未发现重复文件（已跳过小于 1 MB 的文件）。",
                        "No duplicate files found (files smaller than 1 MB are skipped).");
                    TxtEmpty.Visibility = Visibility.Visible;
                    return;
                }

                // 2) SHA-256 校验
                var verified = await Task.Run(() => VerifyGroups(groups, ct), ct);
                verified.Sort((a, b) => b.ReclaimBytes.CompareTo(a.ReclaimBytes));
                RenderGroups(verified);

                long reclaim = verified.Sum(g => g.ReclaimBytes);
                TxtSummary.Text = UiLanguage.L(
                    $"发现 {verified.Count} 组重复 · 可释放 {MainWindow.FmtSize(reclaim)}",
                    $"{verified.Count} group(s) · reclaim {MainWindow.FmtSize(reclaim)}");
            }
            catch (OperationCanceledException)
            {
                TxtProgress.Text = UiLanguage.L("已取消扫描。", "Scan cancelled.");
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "DuplicateFilePage.Scan");
                TxtProgress.Text = UiLanguage.L("扫描出错：", "Scan error: ") + ex.Message;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                BtnScan.Content = UiLanguage.L("开始扫描", "Scan");
                BtnPick.IsEnabled = true;
            }
        }

        /// <summary>枚举文件并按（文件名小写, 大小）分组；返回候选组（≥2 个且不小于 MinBytes）。</summary>
        private static (List<DupGroup> groups, int skipped) BuildCandidateGroups(string root, CancellationToken ct)
        {
            var buckets = new Dictionary<(string, long), List<string>>();
            int skipped = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                IEnumerable<string> subdirs;
                try { subdirs = Directory.EnumerateDirectories(dir); }
                catch (UnauthorizedAccessException) { subdirs = Array.Empty<string>(); }
                catch (IOException) { subdirs = Array.Empty<string>(); }

                foreach (var sd in subdirs)
                {
                    // 跳过 Windows 系统保留目录，避免无谓耗时与提权风险
                    string name = Path.GetFileName(sd).ToLowerInvariant();
                    if (name is "windows" or "system32" or "program files" or "program files (x86)"
                        or "programdata" or "$recycle.bin" or "system volume information"
                        or "node_modules" or ".git" or "appdata") continue;
                    stack.Push(sd);
                }

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir); }
                catch (UnauthorizedAccessException) { files = Array.Empty<string>(); }
                catch (IOException) { files = Array.Empty<string>(); }

                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length < MinBytes) { skipped++; continue; }
                        var key = (fi.Name.ToLowerInvariant(), fi.Length);
                        if (!buckets.TryGetValue(key, out var list))
                            buckets[key] = list = new List<string>();
                        list.Add(f);
                    }
                    catch { /* 无法访问的文件跳过 */ }
                }
            }

            var groups = buckets
                .Where(kv => kv.Value.Count >= 2)
                .Select(kv => new DupGroup(kv.Key.Item1, kv.Key.Item2, kv.Value))
                .ToList();
            return (groups, skipped);
        }

        /// <summary>对候选组逐文件计算 SHA-256，剔除实际内容不同的误报组。</summary>
        private static List<DupGroup> VerifyGroups(List<DupGroup> candidates, CancellationToken ct)
        {
            var result = new List<DupGroup>();
            double total = Math.Max(candidates.Count, 1);
            int done = 0;
            foreach (var g in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var verified = g.Files
                    .Select(f => (Path: f, Hash: Sha256Of(f, ct)))
                    .GroupBy(x => x.Hash)
                    .Where(grp => grp.Count() >= 2)
                    .SelectMany(grp => grp.Select(x => x.Path))
                    .ToList();
                if (verified.Count >= 2)
                    result.Add(new DupGroup(g.Name, g.Size, verified));
                done++;
            }
            return result;
        }

        private static string Sha256Of(string path, CancellationToken ct)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        // ===== 渲染结果 =====

        private void RenderGroups(List<DupGroup> groups)
        {
            GroupsPanel.Children.Clear();
            TxtProgress.Text = UiLanguage.L(
                $"校验完成，共 {groups.Count} 组重复文件。",
                $"Verification done — {groups.Count} duplicate group(s).");
            int idx = 1;
            foreach (var g in groups)
            {
                GroupsPanel.Children.Add(BuildGroupCard(g, idx++));
            }
        }

        private Border BuildGroupCard(DupGroup g, int index)
        {
            var card = new Border
            {
                Style = (Style)FindResource("GlassCard"),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16, 12, 16, 12),
            };
            var root = new StackPanel();

            // 组头
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = UiLanguage.L(
                    $"第 {index} 组 · {g.Files.Count} 个相同文件 · {MainWindow.FmtSize(g.Size)}/个",
                    $"Group {index} · {g.Files.Count} copies · {MainWindow.FmtSize(g.Size)} each"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            var delBtn = new Button
            {
                Content = UiLanguage.L(
                    $"删除除保留文件外的 {g.Files.Count - 1} 个（可释放 {MainWindow.FmtSize(g.ReclaimBytes)}）",
                    $"Remove {g.Files.Count - 1} extra copy/copies (free {MainWindow.FmtSize(g.ReclaimBytes)})"),
                FontSize = 12,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = g,
            };
            delBtn.Click += DeleteGroup_Click;
            Grid.SetColumn(delBtn, 2);
            header.Children.Add(delBtn);
            root.Children.Add(header);

            // 文件列表（保留项第一个，其余为可删项）
            var filesPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            int keep = 0;
            foreach (var f in g.Files)
            {
                bool isKeep = keep++ == 0;
                filesPanel.Children.Add(BuildFileRow(f, g.Size, isKeep));
            }
            root.Children.Add(filesPanel);

            card.Child = root;
            return card;
        }

        private StackPanel BuildFileRow(string path, long size, bool isKeep)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7,
                Fill = (Brush)FindResource(isKeep ? "AccentBrush" : "TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0),
            };
            row.Children.Add(dot);

            var pathTb = new TextBlock
            {
                Text = path,
                FontSize = 12,
                Foreground = isKeep
                    ? (Brush)FindResource("TextPrimaryBrush")
                    : (Brush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = path,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(pathTb);

            var sizeTb = new TextBlock
            {
                Text = MainWindow.FmtSize(size),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(sizeTb);

            if (isKeep)
            {
                var keepTag = new TextBlock
                {
                    Text = UiLanguage.L("（保留）", " (kept)"),
                    FontSize = 11,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(keepTag);
            }
            return row;
        }

        // ===== 删除（回收站 + 双重确认） =====

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not DupGroup g) return;
            btn.IsEnabled = false;
            try
            {
                string detail = UiLanguage.L(
                    $"将删除第 {g.Files.Count - 1} 个重复文件（保留 1 个）：\n\n{string.Join("\n", g.Files.Skip(1).Take(8))}" +
                    (g.Files.Count - 1 > 8 ? "\n…" : ""),
                    $"Will delete {g.Files.Count - 1} duplicate file(s) (keeping 1):\n\n{string.Join("\n", g.Files.Skip(1).Take(8))}" +
                    (g.Files.Count - 1 > 8 ? "\n…" : ""));
                if (!RiskGuard.ConfirmTwice(
                        UiLanguage.L("删除重复文件", "Delete duplicate files"), detail))
                    return;

                int removed = 0;
                foreach (var f in g.Files.Skip(1))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            f,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                        removed++;
                    }
                    catch (Exception ex) { App.LogCrash(ex, "DuplicateFilePage.Delete"); }
                }

                if (removed > 0)
                {
                    MessageBox.Show(
                        UiLanguage.L(
                            $"已将 {removed} 个重复文件移入回收站（可恢复）。",
                            $"{removed} duplicate file(s) moved to the Recycle Bin (recoverable)."),
                        UiLanguage.L("完成", "Done"),
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // 从面板移除已处理的分组
                    if (btn.Parent is Grid headerGrid && headerGrid.Parent is StackPanel cardRoot
                        && cardRoot.Parent is Border card && card.Parent is StackPanel owner)
                    {
                        owner.Children.Remove(card);
                        RefreshSummary();
                    }
                }
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private void RefreshSummary()
        {
            // 重算剩余组数与可释放空间
            var remaining = new List<DupGroup>();
            foreach (var child in GroupsPanel.Children)
            {
                if (child is Border b && b.Child is StackPanel sp
                    && sp.Children.OfType<Grid>().FirstOrDefault() is Grid h
                    && h.Children.OfType<Button>().FirstOrDefault() is Button d
                    && d.Tag is DupGroup g)
                {
                    remaining.Add(g);
                }
            }
            if (remaining.Count == 0)
            {
                TxtSummary.Text = "";
                TxtEmpty.Text = UiLanguage.L("已全部清理完毕。", "All duplicates cleaned up.");
                TxtEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                long reclaim = remaining.Sum(g => g.ReclaimBytes);
                TxtSummary.Text = UiLanguage.L(
                    $"剩余 {remaining.Count} 组 · 可释放 {MainWindow.FmtSize(reclaim)}",
                    $"{remaining.Count} group(s) left · reclaim {MainWindow.FmtSize(reclaim)}");
            }
        }

        /// <summary>重复分组（仅展示与删除，不做序列化）。</summary>
        private sealed class DupGroup
        {
            public string Name { get; }
            public long Size { get; }
            public List<string> Files { get; }

            /// <summary>删除除保留文件外可释放的字节数</summary>
            public long ReclaimBytes => Size * Math.Max(Files.Count - 1, 0);

            public DupGroup(string name, long size, List<string> files)
            {
                Name = name;
                Size = size;
                Files = files;
            }
        }
    }
}
