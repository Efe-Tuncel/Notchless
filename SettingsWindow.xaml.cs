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

    private static string GetCurrentVersion()
    {
        try
        {
            var attr = System.Attribute.GetCustomAttribute(System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyInformationalVersionAttribute)) as System.Reflection.AssemblyInformationalVersionAttribute;
            var info = attr?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var v = info.Trim().TrimStart('v').Split('+')[0].Split('-')[0].Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        try
        {
            var v = typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            if (v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
            return v;
        }
        catch { return "0.0.0"; }
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
            var cur = GetCurrentVersion();
            var tagNorm = tag.Trim().TrimStart('v').Split('+')[0].Split('-')[0].Trim();
            // Version normalize: "1.3.5.0" vs "1.3.5" eşit say
            string Norm(string s) { try { if (s.EndsWith(".0")) s = s.Substring(0, s.Length - 2); return s; } catch { return s; } }
            cur = Norm(cur); tagNorm = Norm(tagNorm);
            UpdateStatusText.Text = $"Son sürüm: {tag} • Şu an: v{cur} • {DateTime.Now:HH:mm}";
            if (tagNorm != cur)
            {
                var res = System.Windows.MessageBox.Show($"Yeni sürüm var: {tag}\nŞu an: v{cur}\nİndirip otomatik kurulsun mu?\n(Ayarlar korunur)", "Notchless Güncelleme", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        string assetUrl = "";
                        if (doc.RootElement.TryGetProperty("assets", out var assets))
                        {
                            foreach (var a in assets.EnumerateArray())
                            {
                                var n = a.GetProperty("name").GetString() ?? "";
                                if (n.Contains("Setup", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                { assetUrl = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
                            }
                            if (string.IsNullOrEmpty(assetUrl))
                                foreach (var a in assets.EnumerateArray())
                                {
                                    var n2 = a.GetProperty("name").GetString() ?? "";
                                    if (n2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { assetUrl = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
                                }
                        }
                        if (string.IsNullOrEmpty(assetUrl))
                        {
                            System.Windows.MessageBox.Show("Setup dosyası bulunamadı, GitHub Releases sayfasına yönlendiriliyorsunuz.", "Notchless");
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Efe-Tuncel/Notchless/releases/latest") { UseShellExecute = true });
                            return;
                        }
                        UpdateStatusText.Text = "İndiriliyor...";
                        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Notchless-Setup-" + tag + ".exe");
                        using (var hc = new System.Net.Http.HttpClient())
                        {
                            hc.DefaultRequestHeaders.UserAgent.ParseAdd("Notchless/1.0");
                            var bytes = await hc.GetByteArrayAsync(assetUrl);
                            System.IO.File.WriteAllBytes(tmp, bytes);
                        }
                        UpdateStatusText.Text = "Kuruluyor...";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tmp) { UseShellExecute = true, Arguments = "/SILENT" });
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch (Exception dlEx) { System.Windows.MessageBox.Show($"İndirme hatası: {dlEx.Message}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); }
                }
            }
            else
                System.Windows.MessageBox.Show($"En güncel sürümdesiniz. (v{cur})", "Notchless", MessageBoxButton.OK, MessageBoxImage.Information);
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
