using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WINHELP
{
    /// <summary>
    /// 桌面便签模块 — 与主界面共享 NotesStore（%APPDATA%/WINHELP/notes/ 每条约一个 .txt）。
    /// 勾选"同时保存到桌面文件"时额外在桌面创建副本。陪伴运行小窗添加的便签也会
    /// 通过 NotesStore.Changed 事件同步显示在这里。
    /// </summary>
    public partial class NotesPage : UserControl
    {
        private readonly ObservableCollection<NotesStore.NoteEntry> _notes = new();

        public NotesPage()
        {
            InitializeComponent();
            ApplyTheme();
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke(ApplyTheme);

            NotesList.ItemsSource = _notes;
            LoadNotes();

            // 与陪伴运行小窗实时同步
            NotesStore.Changed += OnNotesChanged;
            Unloaded += (_, _) => NotesStore.Changed -= OnNotesChanged;

            TxtNoteInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    BtnAddNote_Click(s, e);
                    e.Handled = true;
                }
            };
        }

        private void ApplyTheme()
        {
            ThemeManager.ApplyButtonTheme(BtnAddNote, Color.FromRgb(0x4A, 0x90, 0xD9));
        }

        private void OnNotesChanged()
        {
            Dispatcher.Invoke(LoadNotes);
        }

        // ===== 加载 =====

        private void LoadNotes()
        {
            _notes.Clear();
            foreach (var n in NotesStore.LoadAll())
                _notes.Add(n);
            UpdateCountText();
        }

        private void UpdateCountText()
        {
            TxtNoteCount.Text = UiLanguage.L($"共 {_notes.Count} 条便签", $"{_notes.Count} note(s)");
        }

        // ===== 添加便签 =====

        private void BtnAddNote_Click(object sender, RoutedEventArgs e)
        {
            var text = TxtNoteInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                NotesStore.Add(text, ChkAlsoDesktop.IsChecked == true);
                TxtNoteInput.Text = "";
                UpdateCountText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLanguage.L($"保存失败：{ex.Message}", $"Save failed: {ex.Message}"),
                    UiLanguage.L("错误", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== 删除便签 / 打开文件 =====

        private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                var item = _notes.FirstOrDefault(n => n.FilePath == filePath);
                if (item == null) return;
                var confirm = MessageBox.Show(
                    UiLanguage.L("确定删除此条便签？", "Delete this note?"),
                    UiLanguage.L("确认", "Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                NotesStore.Delete(item);
                UpdateCountText();
            }
        }

        private void BtnOpenNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                OpenInExplorer(filePath);
            }
        }

        private void NoteItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 右键可扩展上下文菜单
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
            }
            catch { }
        }
    }
}
