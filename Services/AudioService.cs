using System;
using NAudio.CoreAudioApi;
using System.Windows.Threading;

namespace Notchless.Services;

public sealed class AudioService : IDisposable
{
    private MMDevice? _device;
    private MMDeviceEnumerator? _enumerator;
    private readonly DispatcherTimer _pollTimer;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    public event Action<float, bool>? VolumeChanged; // 0..1, muted
    public float Volume { get; private set; }
    public bool IsMuted { get; private set; }

    public AudioService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            Volume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            IsMuted = _device.AudioEndpointVolume.Mute;
        }
        catch { /* no audio device */ }

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += PollFallback;
        _pollTimer.Start();
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        Volume = data.MasterVolume;
        IsMuted = data.Muted;
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        _ = _dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(Volume, IsMuted));
    }

    private void PollFallback(object? s, EventArgs e)
    {
        try
        {
            if (_device == null)
            {
                // varsayılan cihaz değişmiş olabilir — yeniden dene
                try
                {
                    _enumerator ??= new MMDeviceEnumerator();
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                    Volume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    IsMuted = _device.AudioEndpointVolume.Mute;
                    VolumeChanged?.Invoke(Volume, IsMuted);
                }
                catch { return; }
                return;
            }
            var dev = _device;
            if (dev == null) return;
            var v = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
            var m = dev.AudioEndpointVolume.Mute;
            if (Math.Abs(v - Volume) > 0.001f || m != IsMuted)
            {
                Volume = v; IsMuted = m;
                VolumeChanged?.Invoke(Volume, IsMuted);
            }
        }
        catch
        {
            // cihaz kayboldu — temizle, sonraki poll'de yeniden oluştur
            try { if (_device != null) _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            try { _device?.Dispose(); } catch { }
            _device = null;
        }
    }

    public void SetVolume(float v)
    {
        if (_device == null) return;
        try { v = Math.Clamp(v, 0f, 1f); _device.AudioEndpointVolume.MasterVolumeLevelScalar = v; } catch { }
    }

    public void SetMute(bool mute)
    {
        if (_device == null) return;
        try { _device.AudioEndpointVolume.Mute = mute; } catch { }
    }

    public void Dispose()
    {
        _pollTimer.Tick -= PollFallback;
        _pollTimer.Stop();
        if (_device != null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            _device.Dispose(); _device = null;
        }
        _enumerator?.Dispose(); _enumerator = null;
    }
}
