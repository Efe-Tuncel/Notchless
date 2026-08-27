using System;
using NAudio.CoreAudioApi;
using System.Windows.Threading;

namespace Notchless.Services;

public sealed class AudioService : IDisposable
{
    private MMDevice? _device;
    private readonly DispatcherTimer _pollTimer;
    public event Action<float, bool>? VolumeChanged; // 0..1, muted
    public float Volume { get; private set; }
    public bool IsMuted { get; private set; }

    public AudioService()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            Volume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            IsMuted = _device.AudioEndpointVolume.Mute;
        }
        catch { /* no audio device */ }

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => PollFallback();
        _pollTimer.Start();
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        Volume = data.MasterVolume;
        IsMuted = data.Muted;
        // Marshal to UI thread via Dispatcher if needed by caller; event is already on COM thread, use App dispatcher
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(Volume, IsMuted));
    }

    private void PollFallback()
    {
        if (_device == null) return;
        try
        {
            var v = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            var m = _device.AudioEndpointVolume.Mute;
            if (Math.Abs(v - Volume) > 0.001f || m != IsMuted)
            {
                Volume = v; IsMuted = m;
                VolumeChanged?.Invoke(Volume, IsMuted);
            }
        }
        catch { }
    }

    public void SetVolume(float v)
    {
        if (_device == null) return;
        v = Math.Clamp(v, 0f, 1f);
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void SetMute(bool mute)
    {
        if (_device == null) return;
        _device.AudioEndpointVolume.Mute = mute;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        if (_device != null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }
    }
}
