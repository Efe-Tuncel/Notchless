using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Notchless.Services;

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

    public MediaService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (_, _) => PollProgress();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            UpdateSession(_manager.GetCurrentSession());
        }
        catch { }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager s, SessionsChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => UpdateSession(s.GetCurrentSession()));

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager s, CurrentSessionChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => UpdateSession(s.GetCurrentSession()));

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
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => PollProgress();

    private async Task RefreshPropsAsync()
    {
        if (_currentSession == null) return;
        try
        {
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            var playback = _currentSession.GetPlaybackInfo();
            var timeline = _currentSession.GetTimelineProperties();
            BitmapImage? thumb = null;
            if (props.Thumbnail != null)
            {
                try
                {
                    var stream = await props.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    var dr = new DataReader(stream.GetInputStreamAt(0));
                    await dr.LoadAsync((uint)stream.Size);
                    var bytes = new byte[stream.Size];
                    dr.ReadBytes(bytes);
                    dr.DetachStream();
                    thumb = new BitmapImage();
                    thumb.BeginInit();
                    thumb.StreamSource = new MemoryStream(bytes);
                    thumb.CacheOption = BitmapCacheOption.OnLoad;
                    thumb.EndInit();
                    thumb.Freeze();
                }
                catch { }
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
            _dispatcher.BeginInvoke(() => MediaChanged?.Invoke(info));
        }
        catch { }
    }

    private void PollProgress()
    {
        if (_currentSession == null) return;
        try
        {
            var t = _currentSession.GetTimelineProperties();
            ProgressChanged?.Invoke(t.Position, t.EndTime - t.StartTime);
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
        _progressTimer.Stop();
        if (_manager != null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }
}
