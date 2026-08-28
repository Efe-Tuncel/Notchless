using System;
using System.Windows;
using System.Windows.Media;
using Notchless.Helpers;
using Notchless.Services;

namespace Notchless;

public partial class SettingsWindow : Window
{
    private readonly ThemeService _theme = new();
    private string _originalTheme = "Graphite";
    private readonly IslandWindow? _ownerIsland;

    public SettingsWindow(IslandWindow? owner = null)
    {
        InitializeComponent();
        _ownerIsland = owner;
        _theme.Load();
        _originalTheme = _theme.Current.Name;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object s, RoutedEventArgs e)
    {
        ThemePicker.ItemsSource = ThemeService.Themes;
        ThemePicker.SelectedItem = ThemeService.Themes.FirstOrDefault(t => t.Name == _theme.Current.Name) ?? ThemeService.Themes[0];
        UpdatePreview(_theme.Current);

        AutoStartCheck.IsChecked = RegistryHelper.IsAutoStartEnabled();
        ExcludeCaptureCheck2.IsChecked = GetExcludeCapture();
        AudioHookCheck2.IsChecked = GetAudioHook();
        VersionText.Text = $"v{GetType().Assembly.GetName().Version} • {ThemeService.Themes.Count} tema";
        try
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "notif_duration.txt");
            if (System.IO.File.Exists(p) && double.TryParse(System.IO.File.ReadAllText(p).Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                NotifDurationSlider.Value = Math.Clamp(d, 2, 8);
                NotifDurationText.Text = $"{d:0.0} sn";
            }
        }
        catch { }
    }

    private void ThemePicker_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemePicker.SelectedItem is ThemeDefinition td)
        {
            _theme.SetTheme(td.Name);
            UpdatePreview(td);
            // canlı önizleme adada
            if (_ownerIsland != null) _theme.ApplyTo(_ownerIsland);
        }
    }

    private void UpdatePreview(ThemeDefinition t)
    {
        try
        {
            PreviewIsland.Background = new LinearGradientBrush(t.IslandGrad1, t.IslandGrad2, 45);
            PreviewIsland.BorderBrush = new SolidColorBrush(t.BorderBrush);
            PreviewCard.Background = new SolidColorBrush(t.CardBg);
            PreviewCard.BorderBrush = new SolidColorBrush(t.CardBorder);
        }
        catch { }
    }

    private void AutoStart_Checked(object s, RoutedEventArgs e) => RegistryHelper.SetAutoStart(true);
    private void AutoStart_Unchecked(object s, RoutedEventArgs e) => RegistryHelper.SetAutoStart(false);

    private bool GetExcludeCapture()
    {
        try
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "exclude_capture.txt");
            if (System.IO.File.Exists(p)) return System.IO.File.ReadAllText(p).Trim() == "1";
        }
        catch { }
        return false;
    }
    private bool GetAudioHook()
    {
        try
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "audiohook.txt");
            if (System.IO.File.Exists(p)) return System.IO.File.ReadAllText(p).Trim() == "1";
        }
        catch { }
        return false;
    }
    private void ExcludeCapture2_Checked(object s, RoutedEventArgs e) => SaveSimple("exclude_capture.txt", "1");
    private void ExcludeCapture2_Unchecked(object s, RoutedEventArgs e) => SaveSimple("exclude_capture.txt", "0");
    private void AudioHook2_Checked(object s, RoutedEventArgs e) => SaveSimple("audiohook.txt", "1");
    private void AudioHook2_Unchecked(object s, RoutedEventArgs e) => SaveSimple("audiohook.txt", "0");
    private static void SaveSimple(string file, string val)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), val);
        }
        catch { }
    }

    private void NotifDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NotifDurationText == null) return;
        NotifDurationText.Text = $"{e.NewValue:0.0} sn";
        SaveSimple("notif_duration.txt", e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = "Kontrol ediliyor...";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Notchless/1.0");
            var json = await http.GetStringAsync("https://api.github.com/repos/Efe-Tuncel/Notchless/releases/latest");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
            var cur = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
            UpdateStatusText.Text = $"Son sürüm: {tag} • Şu an: v{cur} • {DateTime.Now:HH:mm}";
            // basit karşılaştırma
            if (tag.TrimStart('v') != cur)
                System.Windows.MessageBox.Show($"Yeni sürüm var: {tag}\nGitHub Releases'ten indirin.", "Notchless Güncelleme", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                System.Windows.MessageBox.Show("En güncel sürümdesiniz.", "Notchless", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { UpdateStatusText.Text = $"Hata: {ex.Message}"; }
        finally { CheckUpdateBtn.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _theme.Save();
        if (_ownerIsland != null) _theme.ApplyTo(_ownerIsland);
        _originalTheme = _theme.Current.Name;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // preview rollback if not saved
        _theme.SetTheme(_originalTheme);
        if (_ownerIsland != null) _theme.ApplyTo(_ownerIsland);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // ensure preview not leaked: if closed via X, rollback
        if (_theme.Current.Name != _originalTheme)
        {
            // if not saved, keep preview? Save_Click already handled, here rollback to original if needed
            // check if file still old: if user didn't Save, revert visual
            _theme.SetTheme(_originalTheme);
            if (_ownerIsland != null) _theme.ApplyTo(_ownerIsland);
        }
    }
}
