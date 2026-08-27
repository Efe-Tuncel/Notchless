using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Notchless.Services;

/// <summary>
/// İndirilenler klasörünü izler — SHGetKnownFolderPath(FOLDERID_Downloads) ile yol alınır (sabit değil).
/// .crdownload/.tmp/.partial uzantıları takibi + tamamlanma bildirimi.
/// </summary>
public sealed class DownloadWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    public event Action<string, string>? DownloadChanged; // file, state: "progress" | "completed"

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    private static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public string? DownloadsPath { get; private set; }

    public DownloadWatcherService()
    {
        DownloadsPath = GetDownloadsPath();
        if (DownloadsPath == null || !Directory.Exists(DownloadsPath)) return;
        try
        {
            _watcher = new FileSystemWatcher(DownloadsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += (s, e) => DownloadChanged?.Invoke(e.Name ?? "", "completed");
            _watcher.Renamed += (s, e) =>
            {
                // .crdownload -> gerçek dosya rename = tamamlandı
                if (e.OldName != null && IsTemp(e.OldName) && !IsTemp(e.Name ?? "")) DownloadChanged?.Invoke(e.Name ?? "", "completed");
                else DownloadChanged?.Invoke(e.Name ?? "", "progress");
            };
        }
        catch { }
    }

    private void OnChanged(object s, FileSystemEventArgs e)
    {
        var name = e.Name ?? "";
        if (IsTemp(name)) DownloadChanged?.Invoke(name, "progress");
        else if (e.ChangeType == WatcherChangeTypes.Created) DownloadChanged?.Invoke(name, "completed");
    }

    private static bool IsTemp(string n) => n.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
        || n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        || n.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
        || n.EndsWith(".download", StringComparison.OrdinalIgnoreCase);

    private static string? GetDownloadsPath()
    {
        try
        {
            if (SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out var p) == 0)
            {
                var s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
        }
        catch { }
        // fallback
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(d) ? d : null;
    }

    public void Dispose() { _watcher?.Dispose(); }
}
