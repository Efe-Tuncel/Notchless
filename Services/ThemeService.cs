using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace Notchless;

public sealed record ThemeDefinition(
    string Name,
    string DisplayName,
    WColor IslandGrad1,
    WColor IslandGrad2,
    WColor CardBg,
    WColor CardBorder,
    WColor BorderBrush,
    WColor TextPrimary,
    WColor TextSecondary,
    WColor Accent,
    bool IsLight
);

public sealed class ThemeService
{
    public static readonly IReadOnlyList<ThemeDefinition> Themes = new List<ThemeDefinition>
    {
        new("Midnight", "Midnight OLED", WColor.FromArgb(0xFF,0x00,0x00,0x00), WColor.FromArgb(0xFF,0x0A,0x0A,0x0A),
            WColor.FromArgb(0xE6,0x0D,0x0D,0x0D), WColor.FromArgb(0x1A,0xFF,0xFF,0xFF), WColor.FromArgb(0x22,0xFF,0xFF,0xFF),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xAA,0xAA,0xAA), WColor.FromRgb(0x00,0xFF,0x88), false),
        new("Graphite", "Graphite", WColor.FromArgb(0xFF,0x0F,0x0F,0x10), WColor.FromArgb(0xFF,0x1A,0x1A,0x1E),
            WColor.FromArgb(0xE6,0x18,0x18,0x1C), WColor.FromArgb(0x1E,0xFF,0xFF,0xFF), WColor.FromArgb(0x22,0xFF,0xFF,0xFF),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xAA,0xAA,0xAA), WColor.FromRgb(0x9E,0xCA,0xFF), false),
        new("Snow", "Snow", WColor.FromArgb(0xFF,0xF5,0xF5,0xF7), WColor.FromArgb(0xFF,0xFF,0xFF,0xFF),
            WColor.FromArgb(0xF2,0xFF,0xFF,0xFF), WColor.FromArgb(0xFF,0xE0,0xE0,0xE0), WColor.FromArgb(0x30,0x00,0x00,0x00),
            WColor.FromRgb(0x11,0x11,0x11), WColor.FromRgb(0x66,0x66,0x66), WColor.FromRgb(0x00,0x78,0xD4), true),
        new("Ocean", "Ocean", WColor.FromArgb(0xFF,0x0A,0x15,0x20), WColor.FromArgb(0xFF,0x12,0x2A,0x40),
            WColor.FromArgb(0xE6,0x13,0x2A,0x44), WColor.FromArgb(0x1E,0x4C,0xC2,0xFF), WColor.FromArgb(0x22,0x4C,0xC2,0xFF),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0x9E,0xCA,0xFF), WColor.FromRgb(0x4C,0xC2,0xFF), false),
        new("Forest", "Forest", WColor.FromArgb(0xFF,0x0B,0x1A,0x13), WColor.FromArgb(0xFF,0x14,0x30,0x1F),
            WColor.FromArgb(0xE6,0x14,0x30,0x1F), WColor.FromArgb(0x1E,0x50,0xE6,0xA0), WColor.FromArgb(0x22,0x50,0xE6,0xA0),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xAA,0xAA,0xAA), WColor.FromRgb(0x50,0xE6,0xA0), false),
        new("Sunset", "Sunset", WColor.FromArgb(0xFF,0x1A,0x0F,0x0A), WColor.FromArgb(0xFF,0x2E,0x1A,0x10),
            WColor.FromArgb(0xE6,0x2E,0x1A,0x10), WColor.FromArgb(0x1E,0xFF,0x6B,0x35), WColor.FromArgb(0x22,0xFF,0x6B,0x35),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xCC,0xCC,0xCC), WColor.FromRgb(0xFF,0x6B,0x35), false),
        new("Neon", "Neon Cyber", WColor.FromArgb(0xFF,0x0A,0x0A,0x14), WColor.FromArgb(0xFF,0x18,0x18,0x30),
            WColor.FromArgb(0xE6,0x18,0x18,0x30), WColor.FromArgb(0x1E,0xFF,0x2E,0x97), WColor.FromArgb(0x22,0x00,0xE5,0xFF),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xAA,0xAA,0xAA), WColor.FromRgb(0xFF,0x2E,0x97), false),
        new("Frost", "Frost", WColor.FromArgb(0xB3,0xE8,0xE8,0xED), WColor.FromArgb(0xB3,0xFF,0xFF,0xFF),
            WColor.FromArgb(0xD9,0xFF,0xFF,0xFF), WColor.FromArgb(0xFF,0xD0,0xD0,0xD5), WColor.FromArgb(0x30,0x00,0x00,0x00),
            WColor.FromRgb(0x11,0x11,0x11), WColor.FromRgb(0x66,0x66,0x66), WColor.FromRgb(0x00,0x78,0xD4), true),
        new("Fluent", "Fluent", WColor.FromArgb(0xFF,0x1F,0x1F,0x1F), WColor.FromArgb(0xFF,0x1F,0x1F,0x1F),
            WColor.FromArgb(0xE6,0x1F,0x1F,0x1F), WColor.FromArgb(0x1A,0xFF,0xFF,0xFF), WColor.FromArgb(0x22,0xFF,0xFF,0xFF),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xAA,0xAA,0xAA), WColor.FromRgb(0x00,0x78,0xD4), false),
        new("Amber", "Amber Mono", WColor.FromArgb(0xFF,0x12,0x0F,0x08), WColor.FromArgb(0xFF,0x1A,0x16,0x0D),
            WColor.FromArgb(0xE6,0x1A,0x16,0x0D), WColor.FromArgb(0x1E,0xFF,0xB0,0x00), WColor.FromArgb(0x22,0xFF,0xB0,0x00),
            WColor.FromRgb(0xFF,0xFF,0xFF), WColor.FromRgb(0xFF,0xB0,0x00), WColor.FromRgb(0xFF,0xB0,0x00), false),
    };

    private static readonly string ThemePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "theme.json");

    public ThemeDefinition Current { get; private set; } = Themes.First(t => t.Name == "Graphite");

    public void Load()
    {
        try
        {
            // migrate eski theme.txt
            var oldTxt = Path.Combine(Path.GetDirectoryName(ThemePath)!, "theme.txt");
            if (!File.Exists(ThemePath) && File.Exists(oldTxt))
            {
                var v = File.ReadAllText(oldTxt).Trim();
                Current = v == "transparent" ? Themes.First(t => t.Name == "Graphite") : Themes.First(t => t.Name == "Midnight");
                Save();
                return;
            }
            if (!File.Exists(ThemePath)) return;
            var json = File.ReadAllText(ThemePath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("selected", out var sel))
            {
                var name = sel.GetString() ?? "Graphite";
                var found = Themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (found != null) Current = found;
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
            var json = JsonSerializer.Serialize(new { selected = Current.Name });
            File.WriteAllText(ThemePath, json);
        }
        catch { }
    }

    public void SetTheme(string name)
    {
        var found = Themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (found != null) Current = found;
    }

    public void ApplyTo(IslandWindow w)
    {
        try
        {
            if (w == null) return;
            var t = Current;
            var island = w.FindName("IslandBorder") as System.Windows.Controls.Border;
            var ccGrid = w.FindName("ControlCenterGrid") as System.Windows.Controls.Grid;
            var hud = w.FindName("HudToast") as System.Windows.Controls.Border;
            if (island != null)
            {
                var g = new LinearGradientBrush
                {
                    StartPoint = new WPoint(0,0), EndPoint = new WPoint(1,1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(t.IslandGrad1, 0),
                        new GradientStop(t.IslandGrad2, 1)
                    }
                };
                island.Background = g;
                island.BorderBrush = new SolidColorBrush(t.BorderBrush);
            }
            if (ccGrid != null)
            {
                foreach (var c in FindVisualChildren<System.Windows.Controls.Border>(ccGrid))
                {
                    if (c.CornerRadius == new CornerRadius(20))
                    {
                        c.Background = new SolidColorBrush(t.CardBg);
                        c.BorderBrush = new SolidColorBrush(t.CardBorder);
                    }
                }
            }
            if (hud != null)
            {
                try { hud.Background = new SolidColorBrush(t.IsLight ? WColor.FromRgb(0xF0,0xF0,0xF0) : WColor.FromRgb(0x11,0x11,0x13)); } catch { }
                try { hud.BorderBrush = new SolidColorBrush(t.CardBorder); } catch { }
            }
        }
        catch { }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dep) where T : DependencyObject
    {
        if (dep == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }
}
