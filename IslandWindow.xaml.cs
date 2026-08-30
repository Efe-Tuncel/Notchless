using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Notchless.Helpers;
using Notchless.Services;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace Notchless;

public partial class IslandWindow : Window
{
    private enum IslandState { Compact, Expanded, ControlCenter, Settings, Notification }

    private IslandState _state = IslandState.Compact;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _isHovering;
    private readonly DispatcherTimer _hudTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _camMicTimer;

    private readonly AudioService _audio = new();
    private readonly BrightnessService _brightness = new();
    private readonly PowerService _power = new();
    private readonly MediaService _media = new();
    private readonly FileShelfService _shelf = new();
    private readonly CalendarService _calendar = new();
    private readonly AudioKeyHookService _audioHook = new();
    private readonly BluetoothBatteryService _bt = new();
    private readonly NotificationService _notif = new();
    private readonly ThemeService _theme = new();
    private WColor _custom1 = WColor.FromRgb(0x0F,0x0F,0x10);
    private WColor _custom2 = WColor.FromRgb(0x1A,0x1A,0x1E);
    private int _customActive = 1;
    private bool _wheelGenerated = false;
    private DownloadWatcherService? _dlWatcher;
    private FullscreenService? _fullscreen;
    private DispatcherTimer? _notifHideTimer;

    private DispatcherTimer? _countdownTimer;
    private TimeSpan _remaining = TimeSpan.Zero;
    private bool _timerRunning;
    private DispatcherTimer? _activeAnimTimer;

    // animation targets — CC genişletildi (Pil/Zaman sığması için 440)
    private static readonly (double w, double h, double r) CompactSize = (128, 36, 18);
    private static readonly (double w, double h, double r) ExpandedSize = (380, 168, 20);
    private static readonly (double w, double h, double r) ControlCenterSize = (440, 480, 24);
    private static readonly (double w, double h, double r) SettingsSize = (440, 520, 24);
    private static readonly (double w, double h, double r) NotificationSize = (360, 78, 20);

    public IslandWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        // DragDrop
        DragEnter += OnDragEnter;
        Drop += OnDrop;
        AllowDrop = true;

        _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _hudTimer.Tick += (_, _) => HudToast.Visibility = Visibility.Collapsed;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => CompactTimeText.Text = DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();
        CompactTimeText.Text = DateTime.Now.ToString("HH:mm");

        _camMicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _camMicTimer.Tick += (_, _) => PollCamMic();
        _camMicTimer.Start();

        WireServices();
        ShelfItemsControl.ItemsSource = _shelf.Items;
        _shelf.PruneMissing();
        UpdateShelfEmpty();
        RefreshCalendar();
        BuildMonitorPicker();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;
            if (_state == IslandState.Compact) AnimateTo(IslandState.Expanded);
            else if (_state == IslandState.Notification)
            {
                _notifHideTimer?.Stop();
                AnimateTo(IslandState.Compact);
            }
        };
        // outside click to collapse -> handle root mouse down outside island
        RootGrid.MouseDown += (s, e) =>
        {
            var pos = e.GetPosition(IslandBorder);
            bool inside = pos.X >= 0 && pos.X <= IslandBorder.ActualWidth && pos.Y >= 0 && pos.Y <= IslandBorder.ActualHeight;
            if (!inside && _state != IslandState.Compact)
            {
                AnimateTo(IslandState.Compact);
                e.Handled = true;
            }
        };
    }

    private void WireServices()
    {
        _audio.VolumeChanged += (v, muted) => Dispatcher.BeginInvoke(() =>
        {
            VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
            VolumeSlider.Value = v * 100;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
            VolumeLabel.Text = muted ? "Muted" : $"{(int)(v * 100)}%";
            MuteDot.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
            ShowHud("🔊", (int)(v * 100), muted);
        });
        // NOTE: BrightnessService EventArrived WMI thread'inden gelir; caller (burada) Dispatcher'a marshal eder
        _brightness.BrightnessChanged += b => Dispatcher.BeginInvoke(() =>
        {
            BrightnessSlider.ValueChanged -= BrightnessSlider_ValueChanged;
            BrightnessSlider.Value = b;
            BrightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;
            BrightnessLabel.Text = $"{b}%";
            ShowHud("☀", b, false);
        });
        _brightness.AvailabilityChanged += supported => Dispatcher.BeginInvoke(() => ApplyBrightnessAvailability(supported));
        _power.PowerChanged += (pct, charging, saver, hasBattery) => Dispatcher.BeginInvoke(() =>
        {
            if (!hasBattery)
            {
                CompactBatteryPanel.Visibility = Visibility.Collapsed;
                CompactAcPanel.Visibility = Visibility.Visible;
                CCBatteryText.Text = "Masaüstü • AC";
                CCBatterySub.Text = "Pil yok — AC bağlı";
                // küçült: compact ada pil göstermeyince daha dar olabilir ama şimdilik aynı boyut
                CompactBatteryFill.Width = 0;
                return;
            }
            CompactBatteryPanel.Visibility = Visibility.Visible;
            CompactAcPanel.Visibility = Visibility.Collapsed;
            CompactBatteryText.Text = $"{pct}%";
            CompactBatteryFill.Width = Math.Max(0, (pct / 100.0) * 18);
            CCBatteryText.Text = $"{pct}% — {(charging ? "Şarj oluyor" : "Pilde")}";
            CCBatterySub.Text = saver ? "Pil koruyucu açık" : charging ? "AC bağlı" : "~hesaplanıyor";
        });
        _media.MediaChanged += info => Dispatcher.BeginInvoke(() =>
        {
            if (info == null)
            {
                MediaTitle.Text = "Çalan bir şey yok";
                MediaArtist.Text = "—";
                AlbumArt.Source = null;
                AlbumPlaceholder.Visibility = Visibility.Visible;
                CompactWave.Visibility = Visibility.Collapsed;
                PlayPauseBtn.Content = "▶";
                return;
            }
            MediaTitle.Text = string.IsNullOrWhiteSpace(info.Title) ? "Bilinmeyen parça" : info.Title;
            MediaArtist.Text = string.IsNullOrWhiteSpace(info.Artist) ? info.Album : info.Artist;
            if (info.Thumbnail != null) { AlbumArt.Source = info.Thumbnail; AlbumPlaceholder.Visibility = Visibility.Collapsed; }
            else { AlbumArt.Source = null; AlbumPlaceholder.Visibility = Visibility.Visible; }
            CompactWave.Visibility = info.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
            PlayPauseBtn.Content = info.IsPlaying ? "⏸" : "▶";
        });
        _media.ProgressChanged += (pos, dur) => Dispatcher.BeginInvoke(() =>
        {
            if (dur.TotalSeconds > 0) MediaProgress.Value = pos.TotalSeconds / dur.TotalSeconds * 100;
        });
        // init values
        VolumeSlider.Value = _audio.Volume * 100;
        VolumeLabel.Text = $"{(int)(_audio.Volume * 100)}%";
        BrightnessSlider.Value = _brightness.Brightness;
        BrightnessLabel.Text = $"{_brightness.Brightness}%";
        ApplyBrightnessAvailability(_brightness.IsSupported);
        // ilk güç durumu masaüstü ise direkt uygula
        if (!_power.HasBattery)
        {
            CompactBatteryPanel.Visibility = Visibility.Collapsed;
            CompactAcPanel.Visibility = Visibility.Visible;
            CCBatteryText.Text = "Masaüstü • AC";
            CCBatterySub.Text = "Pil yok — AC bağlı";
        }
        // Faz 5 ses tuşu hook — Windows native ~%2 adımı taklit edilir (önce %5 idi)
        _audioHook.VolumeKeyPressed += vk => Dispatcher.BeginInvoke(() =>
        {
            if (vk == NativeMethods.VK_VOLUME_MUTE) _audio.SetMute(!_audio.IsMuted);
            else if (vk == NativeMethods.VK_VOLUME_UP) _audio.SetVolume(Math.Min(1f, _audio.Volume + 0.02f));
            else if (vk == NativeMethods.VK_VOLUME_DOWN) _audio.SetVolume(Math.Max(0f, _audio.Volume - 0.02f));
        });
        // Bluetooth GATT Battery — sadece standart 0x180F, yoksa gizli
        _bt.BatteriesChanged += list => Dispatcher.BeginInvoke(() =>
        {
            if (list.Count == 0) { BtBatteryText.Visibility = Visibility.Collapsed; BtBatteryText.Text = ""; return; }
            var top = list[0];
            BtBatteryText.Text = $"🎧 {top.Name}: {top.Percent}%";
            BtBatteryText.Visibility = Visibility.Visible;
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // extended styles: TOPMOST | TOOLWINDOW — x64 safe
        var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex = new IntPtr(ex.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        // PerMonitorV2 already via manifest; position
        PositionOnPrimary();
        UpdateRegionForState(_state);
        // dpi changed handling is via WndProc
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        // Tema — ThemeService (10 tema) + eski theme.txt migrate
        try
        {
            _theme.Load();
            _theme.ApplyTo(this);
            // eski transparent checkbox ile uyum: Graphite≈transparent, Midnight≈opaque
            try { TransparentModeCheck.IsChecked = _theme.Current.Name != "Midnight"; } catch { }
        }
        catch { try { _theme.ApplyTo(this); } catch { } }
        LoadTimerPresets();
        // Hep üste: WPF Topmost + Win32 TOPMOST (masaüstüne tıklayınca gizlenmesin)
        Topmost = true;
        var topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        topmostTimer.Tick += (_, _) =>
        {
            if (!Topmost) Topmost = true;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        };
        topmostTimer.Start();

        // Faz 5 tam ekran gizleme (GetForegroundWindow + SW_HIDE)
        _fullscreen = new FullscreenService(_hwnd);
        // Faz 5 indirme izleme (FOLDERID_Downloads + .crdownload/.tmp/.partial)
        _dlWatcher = new DownloadWatcherService();
        _dlWatcher.DownloadChanged += (file, state) => Dispatcher.BeginInvoke(() =>
        {
            if (state == "completed") ShowHud("⬇", 100, false);
            HudValue.Text = state == "completed" ? $"{file} indi" : $"{file} indiriliyor…";
            HudToast.Visibility = Visibility.Visible;
        });
        // Bildirim dinleyici — gerçek Windows NotificationListener
        _notif.NotificationReceived += info => Dispatcher.BeginInvoke(() => ShowNotification(info));
        _ = _notif.TryEnableAsync().ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!t.Result)
                {
                    // İzin yok — kullanıcıya 1 kez göster
                    try { System.Diagnostics.Debug.WriteLine($"[Notif] not enabled: {_notif.Status}"); } catch { }
                }
            });
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _source?.RemoveHook(WndProc);
        _audio.Dispose(); _brightness.Dispose(); _power.Dispose(); _media.Dispose(); _audioHook.Dispose(); _bt.Dispose();
        _notif.Dispose();
        _dlWatcher?.Dispose(); _fullscreen?.Dispose();
        _clockTimer.Stop(); _camMicTimer.Stop(); _hudTimer.Stop();
        _countdownTimer?.Stop(); _notifHideTimer?.Stop(); _activeAnimTimer?.Stop();
    }

    private void OnDisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(PositionOnPrimary);

    private void PositionOnPrimary()
    {
        // Pencere üstte 800x540 şerit, ada ortada — fullscreen değil (screenshot tool'u engellememek için)
        double left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
        Left = left;
        Top = 0;
        // Ensure width covers screen width for region testing: keep as is 800 centered.
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEMOVE)
        {
            if (!_isHovering)
            {
                _isHovering = true;
                TrackLeave();
                if (_state == IslandState.Compact) AnimateTo(IslandState.Expanded);
            }
        }
        else if (msg == NativeMethods.WM_MOUSELEAVE)
        {
            _isHovering = false;
            if (_state == IslandState.Expanded)
            {
                // small delay to allow moving into expanded area without flicker
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                t.Tick += (_, _) => { t.Stop(); if (!_isHovering && _state == IslandState.Expanded) AnimateTo(IslandState.Compact); };
                t.Start();
            }
        }
        else if (msg == NativeMethods.WM_DPICHANGED)
        {
            // lParam is suggested rect
            PositionOnPrimary();
            UpdateRegionForState(_state);
        }
        return IntPtr.Zero;
    }

    private void TrackLeave()
    {
        var tme = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = _hwnd,
            dwHoverTime = 0
        };
        NativeMethods.TrackMouseEvent(ref tme);
    }

    private void AnimateTo(IslandState target)
    {
        if (_state == target) return;
        _state = target;

        var (tw, th, tr) = target switch
        {
            IslandState.Compact => CompactSize,
            IslandState.Expanded => ExpandedSize,
            IslandState.ControlCenter => ControlCenterSize,
            IslandState.Settings => SettingsSize,
            IslandState.Notification => NotificationSize,
            _ => CompactSize
        };

        // Visibility switch immediately for content — Notification Windows tarzı oval
        bool isNotif = target == IslandState.Notification;
        CompactGrid.Visibility = target == IslandState.Compact ? Visibility.Visible : Visibility.Collapsed;
        ExpandedGrid.Visibility = target == IslandState.Expanded ? Visibility.Visible : Visibility.Collapsed;
        ControlCenterGrid.Visibility = target == IslandState.ControlCenter ? Visibility.Visible : Visibility.Collapsed;
        SettingsGrid.Visibility = target == IslandState.Settings ? Visibility.Visible : Visibility.Collapsed;
        NotificationGrid.Visibility = isNotif ? Visibility.Visible : Visibility.Collapsed;
        // Windows tarzı arka plan: bildirimde Fluent koyu + mavi border
        if (isNotif)
        {
            IslandBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x1F, 0x1F));
            IslandBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
            IslandBorder.BorderThickness = new Thickness(1.2);
        }
        else
        {
            // bildirim sonrası mavi border kaldı hatası — orijinal temaya dön
            bool isTrans = TransparentModeCheck.IsChecked == true;
            if (isTrans)
            {
                IslandBorder.Background = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new WPoint(0,0), EndPoint = new WPoint(1,1),
                    GradientStops = new System.Windows.Media.GradientStopCollection
                    {
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0C), 0),
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x14, 0x14, 0x18), 1)
                    }
                };
                IslandBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                IslandBorder.Background = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new WPoint(0,0), EndPoint = new WPoint(1,1),
                    GradientStops = new System.Windows.Media.GradientStopCollection
                    {
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0B), 0),
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0x14, 0x14, 0x16), 1)
                    }
                };
                IslandBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            }
            IslandBorder.BorderThickness = new Thickness(1);
        }
        // Bildirimde sadece çerçeve sallansın (aura kaldırıldı)
        if (isNotif)
        {
            var shake = new DoubleAnimationUsingKeyFrames();
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190))));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            IslandShake.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, shake);
        }
        else
        {
            IslandShake.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            IslandShake.X = 0;
        }

        var dur = new Duration(TimeSpan.FromMilliseconds(target == IslandState.ControlCenter ? 420 : 360));

        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        if (target == IslandState.Compact) ease = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseInOut };

        var wa = new DoubleAnimation(IslandBorder.Width, tw, dur) { EasingFunction = ease };
        var ha = new DoubleAnimation(IslandBorder.Height, th, dur) { EasingFunction = ease };
        // Corner radius animation via code (since CornerRadius not directly animatable with DoubleAnimation, lerp manually)
        var fromR = IslandBorder.CornerRadius.TopLeft;

        IslandBorder.BeginAnimation(WidthProperty, wa);
        IslandBorder.BeginAnimation(HeightProperty, ha);
        IslandBorder.BeginAnimation(FrameworkElement.TagProperty, null); // dummy
        // Tek timer: corner + region senkron + Spike B ölçümü (60fps / %5 CPU eşiği için)
        var startR = fromR;
        var startTime = DateTime.UtcNow;
        bool isOut = target != IslandState.Compact;
        double amp = isOut ? 0.35 : 0.2;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastTicks = sw.ElapsedTicks;
        double maxFrameMs = 0;
        _activeAnimTimer?.Stop();
        var unifiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _activeAnimTimer = unifiedTimer;
        unifiedTimer.Tick += (_, _) =>
        {
            long now = sw.ElapsedTicks;
            double frameMs = (now - lastTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lastTicks = now;
            if (frameMs > maxFrameMs) maxFrameMs = frameMs;
#if DEBUG
            if (frameMs > 20) System.Diagnostics.Debug.WriteLine($"[SpikeB] frame {frameMs:F1}ms >20ms (p95 hedef 20ms)");
#endif
            double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double prog = Math.Min(1, elapsed / dur.TimeSpan.TotalMilliseconds);
            double eased = isOut ? BackEaseOut(prog, amp) : BackEaseInOut(prog, amp);
            IslandBorder.CornerRadius = new CornerRadius(startR + (tr - startR) * eased);
            UpdateRegionForState(target);
            if (prog >= 1)
            {
                unifiedTimer.Stop();
                if (_activeAnimTimer == unifiedTimer) _activeAnimTimer = null;
                sw.Stop();
                UpdateRegionForState(target);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[SpikeB] anim {dur.TimeSpan.TotalMilliseconds}ms maxFrame {maxFrameMs:F1}ms (hedef avg<16.6ms p95<20ms)");
#endif
            }
        };
        unifiedTimer.Start();
    }

    private void ShowNotification(NotificationInfo info)
    {
        NotifAppText.Text = info.AppName.Length > 28 ? info.AppName.Substring(0, 28) : info.AppName;
        NotifTitleText.Text = info.Title.Length > 52 ? info.Title.Substring(0, 52) + "…" : info.Title;
        if (!string.IsNullOrWhiteSpace(info.Text))
        {
            NotifBodyText.Text = info.Text.Length > 64 ? info.Text.Substring(0, 64) + "…" : info.Text;
            NotifBodyText.Visibility = Visibility.Visible;
            // body varsa biraz daha uzun göster
        }
        else NotifBodyText.Visibility = Visibility.Collapsed;
        // App ikonu — gerçek logo varsa göster, yoksa mavi ◧ fallback
        if (info.AppIcon != null)
        {
            NotifAppIcon.Source = info.AppIcon;
            NotifAppIcon.Visibility = Visibility.Visible;
            NotifIconFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotifAppIcon.Source = null;
            NotifAppIcon.Visibility = Visibility.Collapsed;
            NotifIconFallback.Visibility = Visibility.Visible;
        }

        // Apple'dan alınmış morph (BackEase) ama Windows renkleri ile
        AnimateTo(IslandState.Notification);
        _notifHideTimer?.Stop();
        _notifHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(string.IsNullOrWhiteSpace(info.Text) ? 3.2 : 4.5) };
        _notifHideTimer.Tick += (_, _) =>
        {
            _notifHideTimer?.Stop();
            if (_state == IslandState.Notification) AnimateTo(IslandState.Compact);
        };
        _notifHideTimer.Start();
    }

    private static void TaskDelay(TimeSpan d, Action a)
    {
        var t = new DispatcherTimer { Interval = d };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
    }

    private void UpdateRegionForState(IslandState state)
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var islandPos = IslandBorder.TranslatePoint(new WPoint(0, 0), this);
            int x = (int)Math.Round(islandPos.X * dpi.DpiScaleX);
            int y = (int)Math.Round(islandPos.Y * dpi.DpiScaleY);
            int w = (int)Math.Round(IslandBorder.ActualWidth * dpi.DpiScaleX);
            int h = (int)Math.Round(IslandBorder.ActualHeight * dpi.DpiScaleY);
            if (w <= 0 || h <= 0) return;
            double rDip = IslandBorder.CornerRadius.TopLeft;
            int r = (int)Math.Round(rDip * dpi.DpiScaleX);
            int rx = Math.Max(0, r * 2);
            IntPtr rgn = NativeMethods.CreateRoundRectRgn(x, y, x + w, y + h, rx, rx);
            int ok = NativeMethods.SetWindowRgn(_hwnd, rgn, true);
            if (ok == 0) NativeMethods.DeleteObject(rgn);
        }
        catch { }
    }

    private void ShowHud(string icon, int value, bool muted)
    {
        HudIcon.Text = icon;
        HudBar.Value = value;
        HudValue.Text = muted ? "Muted" : $"{value}%";
        HudToast.Visibility = Visibility.Visible;
        _hudTimer.Stop(); _hudTimer.Start();
    }

    // Slider handlers
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _audio.SetVolume((float)(e.NewValue / 100.0));
        VolumeLabel.Text = $"{(int)e.NewValue}%";
    }
    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brightness.SetBrightness((int)e.NewValue);
        BrightnessLabel.Text = $"{(int)e.NewValue}%";
    }

    // Media — tıklama güvenliği: IsInteractive guard ile ada zıplaması engellendi, butonlar artık doğrudan GSMTC'ye gider
    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        try { await _media.TogglePlayPauseAsync(); } catch { ShowHud("♪", 0, false); HudValue.Text = "Kontrol edilemedi"; }
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_media.Current == null) { ShowHud("♪", 0, false); HudValue.Text = "Çalan bir şey yok"; return; }
            await _media.NextAsync();
        }
        catch { ShowHud("♪", 0, false); HudValue.Text = "Sonraki başarısız"; }
    }
    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_media.Current == null) { ShowHud("♪", 0, false); HudValue.Text = "Çalan bir şey yok"; return; }
            await _media.PreviousAsync();
        }
        catch { ShowHud("♪", 0, false); HudValue.Text = "Önceki başarısız"; }
    }

    // File shelf drag drop
    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) e.Effects = System.Windows.DragDropEffects.Copy;
        else e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
        if (_state == IslandState.Compact) AnimateTo(IslandState.Expanded);
    }
    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files)
            {
                if (!_shelf.TryAdd(f))
                {
                    // if control center not open, open it to show limit
                    break;
                }
            }
            UpdateShelfEmpty();
            AnimateTo(IslandState.ControlCenter);
        }
    }
    private void ShelfRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is FileShelfService.ShelfItem item)
        { _shelf.Remove(item); UpdateShelfEmpty(); }
    }
    private WPoint _shelfDragStart;
    private string? _shelfDragPath;
    private bool _shelfDragging;
    private void ShelfItem_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBlock tb && tb.Tag is string p)
        {
            _shelfDragStart = e.GetPosition(null);
            _shelfDragPath = p;
            _shelfDragging = false;
        }
    }
    private void ShelfItem_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _shelfDragPath == null) return;
        var pos = e.GetPosition(null);
        if (!_shelfDragging && (Math.Abs(pos.X - _shelfDragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(pos.Y - _shelfDragStart.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            _shelfDragging = true;
            try
            {
                var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[] { _shelfDragPath });
                // Gerçek sürükleme oturumu — hedef klasöre bırakınca Explorer kopyalar/taşır
                System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Link);
            }
            catch { }
            e.Handled = true;
        }
    }
    private void ShelfItem_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Sürükleme olmadıysa tık = aç
        if (!_shelfDragging && sender is System.Windows.Controls.TextBlock tb && tb.Tag is string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
        _shelfDragging = false;
        _shelfDragPath = null;
    }
    private void UpdateShelfEmpty() => ShelfEmptyText.Visibility = _shelf.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // Timer
    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && int.TryParse(b.Tag?.ToString(), out int mins))
        {
            _remaining = TimeSpan.FromMinutes(mins);
            TimerDisplay.Text = Fmt(_remaining);
            TimerToggleBtn.Visibility = Visibility.Visible;
            TimerCancelBtn.Visibility = Visibility.Visible;
            TimerToggleBtn.Content = "Başlat";
            _timerRunning = false;
            _countdownTimer?.Stop();
        }
    }
    private void CustomTimer_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CustomTimerBox.Text.Trim(), out int mins) && mins > 0 && mins <= 1440)
        {
            _remaining = TimeSpan.FromMinutes(mins);
            TimerDisplay.Text = Fmt(_remaining);
            TimerToggleBtn.Visibility = Visibility.Visible;
            TimerCancelBtn.Visibility = Visibility.Visible;
            TimerToggleBtn.Content = "Başlat";
            _timerRunning = false;
            _countdownTimer?.Stop();
        }
        else
        {
            CustomTimerBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x55, 0x55));
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            t.Tick += (_, _) => { CustomTimerBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty); t.Stop(); };
            t.Start();
        }
    }
    private void TimerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_remaining == TimeSpan.Zero) return;
        if (_timerRunning) { _countdownTimer?.Stop(); TimerToggleBtn.Content = "Devam"; _timerRunning = false; }
        else
        {
            _countdownTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick -= CountdownTick;
            _countdownTimer.Tick += CountdownTick;
            _countdownTimer.Start();
            TimerToggleBtn.Content = "Durdur";
            _timerRunning = true;
        }
    }
    private void TimerCancel_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer?.Stop(); _timerRunning = false; _remaining = TimeSpan.Zero;
        TimerDisplay.Text = "00:00"; TimerToggleBtn.Visibility = Visibility.Collapsed; TimerCancelBtn.Visibility = Visibility.Collapsed;
    }
    private void CountdownTick(object? s, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);
        if (_remaining <= TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero; _countdownTimer?.Stop(); _timerRunning = false;
            TimerDisplay.Text = "00:00"; TimerToggleBtn.Content = "Başlat";
            // flash + sound
            ShowHud("⏱", 100, false); HudValue.Text = "Süre doldu!";
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        TimerDisplay.Text = Fmt(_remaining);
    }

    // Calendar
    private void RefreshCalendar()
    {
        var evs = _calendar.LoadUpcomingEvents(3);
        if (evs.Count == 0) CalendarText.Text = "Yakın etkinlik yok.\n.ics dosyasını\n" + _calendar.WatchFolder + "\nklasörüne bırakın.";
        else CalendarText.Text = string.Join("\n", evs.Select(ev => $"• {ev.Start:HH:mm} {ev.Title}"));
    }
    private void OpenCalendarFolder_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_calendar.WatchFolder) { UseShellExecute = true }); } catch { }
        RefreshCalendar();
    }

    // Monitors — DeviceName reboot'ta değişebilir, WmiMonitorID (EDID seri) kalıcı
    private Dictionary<string,string> _monitorIdMap = new();
    private void BuildMonitorPicker()
    {
        MonitorPicker.Items.Clear();
        _monitorIdMap.Clear();
        var wmiIds = GetWmiMonitorIds(); // index -> seri
        for (int i = 0; i < System.Windows.Forms.Screen.AllScreens.Length; i++)
        {
            var s = System.Windows.Forms.Screen.AllScreens[i];
            string wmi = i < wmiIds.Count ? wmiIds[i] : "EDID yok";
            string id = $"{wmi}"; // kalıcı ID
            _monitorIdMap[s.DeviceName] = id;
            MonitorPicker.Items.Add($"{s.DeviceName} [{wmi}] — {s.Bounds.Width}x{s.Bounds.Height} {(s.Primary ? "(Birincil)" : "")}");
        }
        if (MonitorPicker.Items.Count > 0) MonitorPicker.SelectedIndex = 0;
    }
    private static List<string> GetWmiMonitorIds()
    {
        var list = new List<string>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, SerialNumberID FROM WmiMonitorID");
            foreach (System.Management.ManagementObject o in searcher.Get())
            {
                try
                {
                    var serialArr = o["SerialNumberID"] as ushort[];
                    string serial = serialArr != null ? new string(Array.ConvertAll(serialArr, c => (char)c)).Trim('\0').Trim() : "";
                    if (string.IsNullOrWhiteSpace(serial)) serial = o["InstanceName"]?.ToString() ?? "unknown";
                    list.Add(serial);
                }
                catch { list.Add("unknown"); }
            }
        }
        catch { }
        if (list.Count == 0) list.Add("unknown");
        return list;
    }
    private void MonitorPicker_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MonitorPicker.SelectedItem is string sel && MonitorPicker.SelectedIndex >= 0)
        {
            var idx = MonitorPicker.SelectedIndex;
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (idx < screens.Length)
            {
                var device = screens[idx].DeviceName;
                var wmiId = _monitorIdMap.TryGetValue(device, out var id) ? id : device;
                try
                {
                    var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "monitor.txt"), wmiId);
                }
                catch { }
            }
        }
    }

    private void ExcludeCapture_Checked(object sender, RoutedEventArgs e)
    {
        if (_hwnd != IntPtr.Zero) NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }
    private void ExcludeCapture_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_hwnd != IntPtr.Zero) NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_NONE);
    }

    private void AudioHook_Checked(object sender, RoutedEventArgs e)
    {
        if (!_audioHook.TryEnable())
            System.Windows.MessageBox.Show("Ses tuşu hook'u kurulamadı. Yönetici gerekebilir veya AV engelliyor.", "Notchless", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void AudioHook_Unchecked(object sender, RoutedEventArgs e) => _audioHook.Disable();

    private void CloseControlCenter_Click(object sender, RoutedEventArgs e) => AnimateTo(IslandState.Compact);
    private void OpenControlCenter_Click(object sender, RoutedEventArgs e) => AnimateTo(IslandState.ControlCenter);
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Kontrol Merkezi → Ayarlar morph (ayrı pencere yerine ada içinde)
        AnimateTo(IslandState.Settings);
        InitSettingsGrid();
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) => AnimateTo(IslandState.ControlCenter);

    private void InitSettingsGrid()
    {
        try
        {
            var list = ThemeService.Themes.ToList();
            if (_theme.CustomTheme != null && !list.Any(x => x.Name == "Custom")) list.Add(_theme.CustomTheme);
            SettingsThemePicker.ItemsSource = list;
            SettingsThemePicker.SelectedItem = list.FirstOrDefault(t => t.Name == _theme.Current.Name) ?? list[0];
            // custom preview — meşhur renk dairesi (ColorDialog)
            try
            {
                if (_theme.CustomTheme != null) { _custom1 = _theme.CustomTheme.IslandGrad1; _custom2 = _theme.CustomTheme.IslandGrad2; }
                CustomColor1Preview.Background = new System.Windows.Media.SolidColorBrush(_custom1);
                CustomColor2Preview.Background = new System.Windows.Media.SolidColorBrush(_custom2);
                EnsureColorWheel();
                UpdateCustomActiveBorder();
            }
            catch { }
            SettingsAutoStartCheck.IsChecked = Helpers.RegistryHelper.IsAutoStartEnabled();
            try
            {
                var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "exclude_capture.txt");
                SettingsExcludeCheck.IsChecked = System.IO.File.Exists(p) && System.IO.File.ReadAllText(p).Trim() == "1";
            }
            catch { SettingsExcludeCheck.IsChecked = false; }
            try
            {
                var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "audiohook.txt");
                SettingsAudioCheck.IsChecked = System.IO.File.Exists(p) && System.IO.File.ReadAllText(p).Trim() == "1";
            }
            catch { SettingsAudioCheck.IsChecked = false; }
            try
            {
                var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "notif_duration.txt");
                if (System.IO.File.Exists(p) && double.TryParse(System.IO.File.ReadAllText(p).Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    SettingsNotifSlider.Value = Math.Clamp(d, 2, 8);
                    SettingsNotifText.Text = $"{d:0.0} sn";
                }
            }
            catch { }
        }
        catch { }
    }
    private void SettingsThemePicker_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SettingsThemePicker.SelectedItem is ThemeDefinition td)
        {
            _theme.SetTheme(td.Name);
            _theme.Save();
            _theme.ApplyTo(this);
        }
    }
    private void SettingsAutoStart_Checked(object s, RoutedEventArgs e) => Helpers.RegistryHelper.SetAutoStart(true);
    private void SettingsAutoStart_Unchecked(object s, RoutedEventArgs e) => Helpers.RegistryHelper.SetAutoStart(false);
    private void SettingsExclude_Checked(object s, RoutedEventArgs e) { try { var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Notchless"); System.IO.Directory.CreateDirectory(d); System.IO.File.WriteAllText(System.IO.Path.Combine(d,"exclude_capture.txt"),"1"); } catch { } }
    private void SettingsExclude_Unchecked(object s, RoutedEventArgs e) { try { var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Notchless"); System.IO.Directory.CreateDirectory(d); System.IO.File.WriteAllText(System.IO.Path.Combine(d,"exclude_capture.txt"),"0"); } catch { } }
    private void SettingsAudio_Checked(object s, RoutedEventArgs e) { try { var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Notchless"); System.IO.Directory.CreateDirectory(d); System.IO.File.WriteAllText(System.IO.Path.Combine(d,"audiohook.txt"),"1"); } catch { } }
    private void SettingsAudio_Unchecked(object s, RoutedEventArgs e) { try { var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Notchless"); System.IO.Directory.CreateDirectory(d); System.IO.File.WriteAllText(System.IO.Path.Combine(d,"audiohook.txt"),"0"); } catch { } }
    private void SettingsNotifSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SettingsNotifText == null) return;
        SettingsNotifText.Text = $"{e.NewValue:0.0} sn";
        try { var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Notchless"); System.IO.Directory.CreateDirectory(d); System.IO.File.WriteAllText(System.IO.Path.Combine(d,"notif_duration.txt"), e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)); } catch { }
    }
    private void CustomApply_Click(object sender, RoutedEventArgs e)
    {
        _theme.SetCustomTheme(_custom1, _custom2);
        _theme.ApplyTo(this);
        try
        {
            var list = ThemeService.Themes.ToList();
            if (_theme.CustomTheme != null && !list.Any(x => x.Name == "Custom")) list.Add(_theme.CustomTheme);
            SettingsThemePicker.ItemsSource = list;
            SettingsThemePicker.SelectedItem = _theme.CustomTheme;
        }
        catch { }
    }
    private void CustomPreview1_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { _customActive = 1; UpdateCustomActiveBorder(); }
    private void CustomPreview2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { _customActive = 2; UpdateCustomActiveBorder(); }
    private void UpdateCustomActiveBorder()
    {
        try
        {
            CustomColor1Preview.BorderThickness = new Thickness(_customActive == 1 ? 2 : 1);
            CustomColor1Preview.BorderBrush = new System.Windows.Media.SolidColorBrush(_customActive == 1 ? WColor.FromArgb(0xFF,0xFF,0xFF,0xFF) : WColor.FromArgb(0x44,0xFF,0xFF,0xFF));
            CustomColor2Preview.BorderThickness = new Thickness(_customActive == 2 ? 2 : 1);
            CustomColor2Preview.BorderBrush = new System.Windows.Media.SolidColorBrush(_customActive == 2 ? WColor.FromArgb(0xFF,0xFF,0xFF,0xFF) : WColor.FromArgb(0x44,0xFF,0xFF,0xFF));
        }
        catch { }
    }
    private void EnsureColorWheel()
    {
        if (_wheelGenerated) return;
        try
        {
            int size = 180;
            var bmp = new System.Windows.Media.Imaging.WriteableBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            int stride = size * 4;
            var pixels = new byte[size * size * 4];
            double cx = size / 2.0, cy = size / 2.0, radius = size / 2.0 - 2;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    double dist = Math.Sqrt(dx*dx + dy*dy);
                    int idx = (y * size + x) * 4;
                    if (dist > radius) { pixels[idx]=0; pixels[idx+1]=0; pixels[idx+2]=0; pixels[idx+3]=0; continue; }
                    double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    double sat = dist / radius;
                    // HSV -> RGB, V=1
                    var rgb = HsvToRgb(hue, sat, 1);
                    pixels[idx] = rgb.B; pixels[idx+1] = rgb.G; pixels[idx+2] = rgb.R; pixels[idx+3] = 255;
                }
            bmp.WritePixels(new Int32Rect(0,0,size,size), pixels, stride, 0);
            ColorWheelImage.Source = bmp;
            _wheelGenerated = true;
        }
        catch { }
    }
    private static WColor HsvToRgb(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h/60)%2 -1)), m = v - c;
        double r=0,g=0,b=0;
        if (h < 60) { r=c; g=x; }
        else if (h < 120) { r=x; g=c; }
        else if (h < 180) { g=c; b=x; }
        else if (h < 240) { g=x; b=c; }
        else if (h < 300) { r=x; b=c; }
        else { r=c; b=x; }
        return WColor.FromRgb((byte)((r+m)*255), (byte)((g+m)*255), (byte)((b+m)*255));
    }
    private void ColorWheel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => PickWheelColor(e);
    private void ColorWheel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) PickWheelColor(e); }
    private void PickWheelColor(System.Windows.Input.MouseEventArgs e)
    {
        try
        {
            var pos = e.GetPosition(ColorWheelImage);
            double cx = 90, cy = 90, radius = 88;
            double dx = pos.X - cx, dy = pos.Y - cy;
            double dist = Math.Sqrt(dx*dx+dy*dy);
            if (dist > radius) return;
            double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            double sat = dist / radius;
            var col = HsvToRgb(hue, sat, 1);
            if (_customActive == 1) { _custom1 = col; CustomColor1Preview.Background = new System.Windows.Media.SolidColorBrush(col); }
            else { _custom2 = col; CustomColor2Preview.Background = new System.Windows.Media.SolidColorBrush(col); }
            // indicator
            ColorWheelIndicator.Visibility = Visibility.Visible;
            System.Windows.Controls.Canvas.SetLeft(ColorWheelIndicator, pos.X - 5);
            System.Windows.Controls.Canvas.SetTop(ColorWheelIndicator, pos.Y - 5);
        }
        catch { }
    }
    private static string IslandGetCurrentVersion()
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
            var v = typeof(IslandWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            if (v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
            return v;
        }
        catch { return "0.0.0"; }
    }

    private async void SettingsCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SettingsCheckUpdateBtn.IsEnabled = false;
        SettingsUpdateText.Text = "Kontrol ediliyor...";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Notchless/1.0");
            var json = await http.GetStringAsync("https://api.github.com/repos/Efe-Tuncel/Notchless/releases/latest");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
            var cur = IslandGetCurrentVersion();
            var tagNorm = tag.Trim().TrimStart('v').Split('+')[0].Split('-')[0].Trim();
            string Norm(string s) { try { if (s.EndsWith(".0")) s = s.Substring(0, s.Length - 2); return s; } catch { return s; } }
            cur = Norm(cur); tagNorm = Norm(tagNorm);
            SettingsUpdateText.Text = $"Son sürüm: {tag} • Şu an: v{cur} • {DateTime.Now:HH:mm}";
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
                                var name = a.GetProperty("name").GetString() ?? "";
                                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                { assetUrl = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
                            }
                            if (string.IsNullOrEmpty(assetUrl))
                            {
                                foreach (var a in assets.EnumerateArray())
                                {
                                    var n2 = a.GetProperty("name").GetString() ?? "";
                                    if (n2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { assetUrl = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
                                }
                            }
                        }
                        if (string.IsNullOrEmpty(assetUrl))
                        {
                            System.Windows.MessageBox.Show("Setup dosyası bulunamadı, GitHub Releases sayfasına yönlendiriliyorsunuz.", "Notchless");
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Efe-Tuncel/Notchless/releases/latest") { UseShellExecute = true });
                            return;
                        }
                        SettingsUpdateText.Text = "İndiriliyor...";
                        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Notchless-Setup-" + tag + ".exe");
                        using (var hc = new System.Net.Http.HttpClient())
                        {
                            hc.DefaultRequestHeaders.UserAgent.ParseAdd("Notchless/1.0");
                            var bytes = await hc.GetByteArrayAsync(assetUrl);
                            System.IO.File.WriteAllBytes(tmp, bytes);
                        }
                        SettingsUpdateText.Text = "Kuruluyor...";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tmp) { UseShellExecute = true, Arguments = "/SILENT" });
                        // Ayarlar korunur — %LocalAppData%\Notchless silinmez (installer script)
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch (Exception dlEx) { System.Windows.MessageBox.Show($"İndirme hatası: {dlEx.Message}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); }
                }
            }
            else
                System.Windows.MessageBox.Show($"En güncel sürümdesiniz. (v{cur})", "Notchless", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { SettingsUpdateText.Text = $"Hata: {ex.Message}"; }
        finally { SettingsCheckUpdateBtn.IsEnabled = true; }
    }

    private void TransparentMode_Checked(object sender, RoutedEventArgs e)
    {
        _theme.SetTheme("Graphite");
        _theme.Save();
        _theme.ApplyTo(this);
        ApplyTransparentTheme(true);
    }
    private void TransparentMode_Unchecked(object sender, RoutedEventArgs e)
    {
        _theme.SetTheme("Midnight");
        _theme.Save();
        _theme.ApplyTo(this);
        ApplyTransparentTheme(false);
    }
    private void ApplyTransparentTheme(bool lowOpacity)
    {
        try
        {
            // Düşük opaklık (%85) vs opak — WDAC blur'suz, sadece alpha
            if (lowOpacity)
            {
                var g = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new WPoint(0,0), EndPoint = new WPoint(1,1),
                    GradientStops = new System.Windows.Media.GradientStopCollection
                    {
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0C), 0),
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x14, 0x14, 0x18), 1)
                    }
                };
                IslandBorder.Background = g;
                IslandBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                var g = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new WPoint(0,0), EndPoint = new WPoint(1,1),
                    GradientStops = new System.Windows.Media.GradientStopCollection
                    {
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0B), 0),
                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0x14, 0x14, 0x16), 1)
                    }
                };
                IslandBorder.Background = g;
                IslandBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            }
            // Bento kartlar — düşük opaklık vs opak (radius 20 — 3. görsel)
            var bentoBg = lowOpacity ? System.Windows.Media.Color.FromArgb(0xE6, 0x18, 0x18, 0x1C) : System.Windows.Media.Color.FromArgb(0xFF, 0x18, 0x18, 0x1C);
            var bentoBorder = lowOpacity ? System.Windows.Media.Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF) : System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
            foreach (var c in FindVisualChildren<System.Windows.Controls.Border>(ControlCenterGrid))
            {
                if (c.CornerRadius == new CornerRadius(20))
                {
                    c.Background = new System.Windows.Media.SolidColorBrush(bentoBg);
                    c.BorderBrush = new System.Windows.Media.SolidColorBrush(bentoBorder);
                }
            }
            // Ayarı sakla (WDAC hash değişmeden kalıcılık)
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "theme.txt"), lowOpacity ? "transparent" : "opaque");
            }
            catch { }
        }
        catch { }
    }
    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject dep) where T : DependencyObject
    {
        if (dep == null) yield break;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }
    private void LoadTimerPresets()
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
            var path = System.IO.Path.Combine(dir, "timers.json");
            int[] presets;
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                presets = System.Text.Json.JsonSerializer.Deserialize<int[]>(json) ?? new[] {1,10,60};
            }
            else
            {
                presets = new[] {1,10,60};
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(presets));
            }
            if (presets.Length > 0) { PresetBtn1.Content = $"{presets[0]} dk"; PresetBtn1.Tag = presets[0].ToString(); if (presets[0] >= 60) PresetBtn1.Content = $"{presets[0]/60} sa"; }
            if (presets.Length > 1) { PresetBtn2.Content = $"{presets[1]} dk"; PresetBtn2.Tag = presets[1].ToString(); if (presets[1] >= 60) PresetBtn2.Content = $"{presets[1]/60} sa"; }
            if (presets.Length > 2) { PresetBtn3.Content = presets[2] >= 60 ? $"{presets[2]/60} sa" : $"{presets[2]} dk"; PresetBtn3.Tag = presets[2].ToString(); }
        }
        catch { }
    }

    private void ApplyBrightnessAvailability(bool supported)
    {
        BrightnessPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (supported)
        {
            VolumePanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 1);
            VolumePanel.Margin = new Thickness(0,0,8,0);
            BrightnessLabel.Visibility = Visibility.Visible;
            BrightnessUnsupported.Visibility = Visibility.Collapsed;
        }
        else
        {
            VolumePanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 2);
            VolumePanel.Margin = new Thickness(0,0,0,0);
            BrightnessLabel.Visibility = Visibility.Collapsed;
            BrightnessUnsupported.Visibility = Visibility.Collapsed; // panel zaten gizli, yazıya gerek yok
        }
    }

    private static bool IsInteractiveControl(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Slider or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Button or System.Windows.Controls.ComboBox or System.Windows.Controls.CheckBox) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    // WPF BackEase ile birebir aynı eğriler — width/height animasyonuyla senkron
    private static double BackEaseOut(double t, double s)
    {
        t -= 1;
        return t * t * ((s + 1) * t + s) + 1;
    }
    private static double BackEaseInOut(double t, double s)
    {
        s *= 1.525;
        if (t < 0.5) return 0.5 * (Math.Pow(2 * t, 2) * ((s + 1) * 2 * t - s));
        t = 2 * t - 2;
        return 0.5 * (t * t * ((s + 1) * t + s) + 2);
    }

    private void PollCamMic()
    {
        try
        {
            bool camInUse = IsConsentInUse(@"webcam");
            bool micInUse = IsConsentInUse(@"microphone");
            CamDot.Fill = new System.Windows.Media.SolidColorBrush(camInUse ? WColor.FromRgb(0xFF, 0x3B, 0x30) : WColor.FromRgb(0x33, 0x33, 0x33));
            MicDot.Fill = new System.Windows.Media.SolidColorBrush(micInUse ? WColor.FromRgb(0xFF, 0x3B, 0x30) : WColor.FromRgb(0x33, 0x33, 0x33));
        }
        catch { }
    }
    private static bool IsConsentInUse(string kind)
    {
        try
        {
            string basePath = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{kind}";
            using var key = Registry.CurrentUser.OpenSubKey(basePath);
            if (key == null) return false;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(sub);
                if (subKey == null) continue;
                object? startObj = subKey.GetValue("LastUsedTimeStart");
                object? stopObj = subKey.GetValue("LastUsedTimeStop");
                long start = ToFileTimeTicks(startObj);
                long stop = ToFileTimeTicks(stopObj);
                if (start == 0) continue;
                if (stop == 0 || stop <= start) return true;
            }
        }
        catch { }
        return false;
    }
    private static long ToFileTimeTicks(object? v)
    {
        if (v == null) return 0;
        if (v is long l) return l;
        if (v is int i) return i;
        if (v is byte[] b && b.Length >= 8) return BitConverter.ToInt64(b, 0);
        if (v is string s)
        {
            s = s.Trim();
            if (long.TryParse(s, out var ls)) return ls;
            // hex string? try hex
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hx)) return hx;
        }
        try { return Convert.ToInt64(v); } catch { return 0; }
    }
}
