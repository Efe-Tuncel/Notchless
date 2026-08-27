using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Notchless.Services;

public sealed class FileShelfService
{
    private const int MaxFiles = 6;
    private readonly string _storePath;
    public ObservableCollection<ShelfItem> Items { get; } = new();

    public record ShelfItem(string Path, string Name, string Extension);

    public FileShelfService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
        Directory.CreateDirectory(appData);
        _storePath = Path.Combine(appData, "shelf.txt");
        Load();
    }

    public bool TryAdd(string filePath)
    {
        if (Items.Count >= MaxFiles) return false;
        if (!File.Exists(filePath) && !Directory.Exists(filePath)) return false;
        if (Items.Any(i => string.Equals(i.Path, filePath, StringComparison.OrdinalIgnoreCase))) return false;
        Items.Add(new ShelfItem(filePath, System.IO.Path.GetFileName(filePath), System.IO.Path.GetExtension(filePath)));
        Save();
        return true;
    }

    public void Remove(ShelfItem item)
    {
        Items.Remove(item);
        Save();
    }

    public void PruneMissing()
    {
        var missing = Items.Where(i => !File.Exists(i.Path) && !Directory.Exists(i.Path)).ToList();
        foreach (var m in missing) Items.Remove(m);
        if (missing.Count > 0) Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            foreach (var line in File.ReadAllLines(_storePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (Items.Count >= MaxFiles) break;
                if (File.Exists(line) || Directory.Exists(line))
                    Items.Add(new ShelfItem(line, System.IO.Path.GetFileName(line), System.IO.Path.GetExtension(line)));
            }
        }
        catch { }
    }

    private void Save()
    {
        try { File.WriteAllLines(_storePath, Items.Select(i => i.Path)); } catch { }
    }
}
