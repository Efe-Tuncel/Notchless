using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Notchless.Services;

internal static class MediaLog
{
    public static void Write(string msg)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [Media] {msg}\n");
        }
        catch { }
    }
}

public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _progressTimer;

    public event Action<MediaInfo?>? MediaChanged;
    public event Action<TimeSpan, TimeSpan>? ProgressChanged; // position, duration

    public MediaInfo? Current { get; private set; }

    public record MediaInfo(string Title, string Artist, string Album, BitmapImage? Thumbnail, TimeSpan Duration, bool IsPlaying);

    private bool _disposed;
    public MediaService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += OnProgressTick;
        _ = InitAsync();
    }
    private void OnProgressTick(object? s, EventArgs e) => PollProgress();

    private async Task InitAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = mgr;
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            UpdateSession(PickBestSession(_manager));
        }
        catch { }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager s, SessionsChangedEventArgs e) =>
        _ = _dispatcher.BeginInvoke(() => UpdateSession(PickBestSession(s)));

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager s, CurrentSessionChangedEventArgs e) =>
        _ = _dispatcher.BeginInvoke(() => UpdateSession(PickBestSession(s)));

    private GlobalSystemMediaTransportControlsSession? PickBestSession(GlobalSystemMediaTransportControlsSessionManager? mgr = null)
    {
        try
        {
            var m = mgr ?? _manager;
            if (m == null) return null;
            var sessions = m.GetSessions();
            // Önce Playing olan, özellikle chrome
            foreach (var s in sessions)
            {
                try { if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return s; } catch { }
            }
            // Yoksa ilk session
            if (sessions.Count > 0) return sessions[0];
            return m.GetCurrentSession();
        }
        catch { return _manager?.GetCurrentSession(); }
    }

    private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnPropsChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackChanged;
            _currentSession.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _currentSession = session;
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnPropsChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackChanged;
            _currentSession.TimelinePropertiesChanged += OnTimelineChanged;
            _ = RefreshPropsAsync();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
            Current = null;
            MediaChanged?.Invoke(null);
        }
    }

    private void OnPropsChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshPropsAsync().ContinueWith(t => { if (t.IsFaulted) System.Diagnostics.Debug.WriteLine(t.Exception); }, TaskScheduler.Default);
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = RefreshPropsAsync().ContinueWith(t => { if (t.IsFaulted) System.Diagnostics.Debug.WriteLine(t.Exception); }, TaskScheduler.Default);
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ = _dispatcher.BeginInvoke(() => PollProgress());

    private async Task RefreshPropsAsync()
    {
        if (_currentSession == null) return;
        try
        {
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            if (props == null) return;
            var playback = _currentSession.GetPlaybackInfo();
            var timeline = _currentSession.GetTimelineProperties();
            BitmapImage? thumb = null;
            if (props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    if (stream.Size == 0 || stream.Size > 10_000_000) { /* too large */ }
                    else
                    {
                        using var dr = new DataReader(stream.GetInputStreamAt(0));
                        await dr.LoadAsync((uint)stream.Size);
                        var bytes = new byte[stream.Size];
                        dr.ReadBytes(bytes);
                        dr.DetachStream();
                        var tmp = new BitmapImage();
                        tmp.BeginInit();
                        tmp.StreamSource = new MemoryStream(bytes);
                        tmp.CacheOption = BitmapCacheOption.OnLoad;
                        tmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        tmp.DecodePixelWidth = 88;
                        tmp.EndInit();
                        if (tmp.CanFreeze) tmp.Freeze();
                        thumb = tmp;
                    }
                }
                catch { thumb = null; }
            }
            else
            {
                try { MediaLog.Write($"Thumbnail null for '{props.Title}' artist='{props.Artist}' session={_currentSession.SourceAppUserModelId}"); } catch { }
                try { System.Diagnostics.Debug.WriteLine($"[Media] Thumbnail null for '{props.Title}' session={_currentSession.SourceAppUserModelId}"); } catch { }
            }
            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var info = new MediaInfo(
                props.Title ?? "",
                props.Artist ?? "",
                props.AlbumTitle ?? "",
                thumb,
                timeline.EndTime - timeline.StartTime,
                isPlaying);
            Current = info;
            if (_dispatcher.CheckAccess()) MediaChanged?.Invoke(info);
            else _ = _dispatcher.BeginInvoke(() => MediaChanged?.Invoke(info));
        }
        catch { }
    }

    private void PollProgress()
    {
        if (_currentSession == null) return;
        try
        {
            var t = _currentSession.GetTimelineProperties();
            var pos = t.Position; var dur = t.EndTime - t.StartTime;
            if (_dispatcher.CheckAccess()) ProgressChanged?.Invoke(pos, dur);
            else _ = _dispatcher.BeginInvoke(() => ProgressChanged?.Invoke(pos, dur));
        }
        catch { }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession != null)
        {
            bool ok = await _currentSession.TryTogglePlayPauseAsync();
            if (!ok) SendMediaKey(Helpers.NativeMethods.VK_MEDIA_PLAY_PAUSE);
        }
        else SendMediaKey(Helpers.NativeMethods.VK_MEDIA_PLAY_PAUSE);
    }
    public async Task NextAsync()
    {
        if (_currentSession != null)
        {
            bool ok = await _currentSession.TrySkipNextAsync();
            if (!ok) SendMediaKey(Helpers.NativeMethods.VK_MEDIA_NEXT_TRACK);
        }
        else SendMediaKey(Helpers.NativeMethods.VK_MEDIA_NEXT_TRACK);
    }
    public async Task PreviousAsync()
    {
        if (_currentSession != null)
        {
            bool ok = await _currentSession.TrySkipPreviousAsync();
            if (!ok) SendMediaKey(Helpers.NativeMethods.VK_MEDIA_PREV_TRACK);
        }
        else SendMediaKey(Helpers.NativeMethods.VK_MEDIA_PREV_TRACK);
    }
    private static void SendMediaKey(int vk)
    {
        try
        {
            Helpers.NativeMethods.keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
            Helpers.NativeMethods.keybd_event((byte)vk, 0, Helpers.NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        _progressTimer.Tick -= OnProgressTick;
        _progressTimer.Stop();
        if (_currentSession != null)
        {
            try { _currentSession.MediaPropertiesChanged -= OnPropsChanged; } catch { }
            try { _currentSession.PlaybackInfoChanged -= OnPlaybackChanged; } catch { }
            try { _currentSession.TimelinePropertiesChanged -= OnTimelineChanged; } catch { }
            _currentSession = null;
        }
        if (_manager != null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}
