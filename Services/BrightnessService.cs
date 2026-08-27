using System;
using System.Management;
using System.Windows.Threading;

namespace Notchless.Services;

public sealed class BrightnessService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private ManagementEventWatcher? _watcher;
    private ManagementObject? _brightnessObj;
    public event Action<int>? BrightnessChanged; // 0..100
    public event Action<bool>? AvailabilityChanged;
    public int Brightness { get; private set; } = 50;
    public bool IsSupported { get; private set; } = true;

    public BrightnessService()
    {
        TryInitWmi();
        TryInitEventWatcher(); // olay tabanlı — WMI __InstanceModificationEvent
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void TryInitEventWatcher()
    {
        try
        {
            // Gerçek event dinleme: CurrentBrightness değişince tetiklenir
            var query = new WqlEventQuery("__InstanceModificationEvent", TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'WmiMonitorBrightness'");
            _watcher = new ManagementEventWatcher(@"root\WMI", query.QueryString);
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                    var v = Convert.ToInt32(target["CurrentBrightness"]);
                    if (v != Brightness)
                    {
                        Brightness = v;
                        // watcher thread'i UI değil — dispatcher'a post etmeden direkt invoke, dinleyici Dispatcher'a alır
                        BrightnessChanged?.Invoke(Brightness);
                    }
                }
                catch { }
            };
            _watcher.Start();
        }
        catch
        {
            // WMI event desteklenmezse polling fallback'i zaten var
            _watcher?.Dispose(); _watcher = null;
        }
    }

    private void TryInitWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness");
            bool found = false;
            foreach (ManagementObject o in searcher.Get())
            {
                _brightnessObj = o;
                found = true;
                break;
            }
            bool supported = found;
            if (IsSupported != supported)
            {
                IsSupported = supported;
                AvailabilityChanged?.Invoke(IsSupported);
            }
        }
        catch
        {
            if (IsSupported) { IsSupported = false; AvailabilityChanged?.Invoke(false); }
        }
    }

    private void Poll()
    {
        try
        {
            if (_brightnessObj == null) { TryInitWmi(); if (_brightnessObj == null) return; }
            // Need fresh query each time because CurrentBrightness may change
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
            {
                var v = Convert.ToInt32(o["CurrentBrightness"]);
                if (v != Brightness)
                {
                    Brightness = v;
                    BrightnessChanged?.Invoke(Brightness);
                }
                break;
            }
        }
        catch { }
    }

    public void SetBrightness(int level)
    {
        level = Math.Clamp(level, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in searcher.Get())
            {
                // Timeout param = 0 or 1
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)level });
                break;
            }
            Brightness = level;
            BrightnessChanged?.Invoke(Brightness);
        }
        catch { }
    }

    public void Dispose()
    {
        _timer.Stop();
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
        _brightnessObj?.Dispose();
    }
}
