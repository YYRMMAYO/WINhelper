using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WINHELP;

/// <summary>
/// 共享便签存储：主界面便签模块（NotesPage）与陪伴运行小窗（CompanionWindow）
/// 共用同一套 per-note .txt 文件（位于 %APPDATA%/WINHELP/notes/），并可选择
/// 在桌面建立副本。通过 Changed 事件实现两端实时同步。
/// </summary>
public static class NotesStore
{
    public static readonly string NotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WINHELP", "notes");
    public static readonly string DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    public sealed class NoteEntry
    {
        public string FilePath { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string? DesktopPath { get; set; }
        public string TimeText => CreatedAt.ToString("yyyy-MM-dd HH:mm");
    }

    public static event Action? Changed;

    public static IReadOnlyList<NoteEntry> LoadAll()
    {
        var list = new List<NoteEntry>();
        try
        {
            Directory.CreateDirectory(NotesDir);
            var files = Directory.GetFiles(NotesDir, "*.txt")
                .OrderByDescending(f => new FileInfo(f).LastWriteTime);
            foreach (var f in files)
            {
                string text;
                try { text = File.ReadAllText(f).Trim(); }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new NoteEntry
                {
                    FilePath = f,
                    Text = text,
                    CreatedAt = File.GetLastWriteTime(f),
                    DesktopPath = FindDesktopCopy(f)
                });
            }
        }
        catch { }
        return list;
    }

    /// <summary>添加一条便签。toDesktop=true 时在桌面建立副本。</summary>
    public static NoteEntry Add(string text, bool toDesktop)
    {
        Directory.CreateDirectory(NotesDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        int seq = 1;
        string filePath;
        do { filePath = Path.Combine(NotesDir, $"note_{stamp}_{seq:D3}.txt"); seq++; }
        while (File.Exists(filePath));

        File.WriteAllText(filePath, text, Encoding.UTF8);

        string? desktopPath = null;
        if (toDesktop)
        {
            try
            {
                desktopPath = Path.Combine(DesktopDir, $"WINHELP_{stamp}.txt");
                File.WriteAllText(desktopPath, text, Encoding.UTF8);
            }
            catch { desktopPath = null; }
        }

        var entry = new NoteEntry
        {
            FilePath = filePath,
            Text = text,
            CreatedAt = DateTime.Now,
            DesktopPath = desktopPath
        };
        Changed?.Invoke();
        return entry;
    }

    public static void Delete(NoteEntry entry)
    {
        if (entry == null) return;
        try { if (File.Exists(entry.FilePath)) File.Delete(entry.FilePath); } catch { }
        if (entry.DesktopPath != null)
        {
            try { if (File.Exists(entry.DesktopPath)) File.Delete(entry.DesktopPath); } catch { }
        }
        Changed?.Invoke();
    }

    /// <summary>从 notes 文件名反推桌面副本路径（文件名规则：note_yyyyMMdd_HHmmss_seq.txt）</summary>
    private static string? FindDesktopCopy(string noteFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(noteFilePath);
        var parts = name.Split('_');
        if (parts.Length >= 3 && parts[0] == "note")
        {
            var stamp = $"{parts[1]}_{parts[2]}";
            var desk = Path.Combine(DesktopDir, $"WINHELP_{stamp}.txt");
            return File.Exists(desk) ? desk : null;
        }
        return null;
    }
}
