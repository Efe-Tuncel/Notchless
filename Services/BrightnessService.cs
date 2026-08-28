using System;
using System.Management;
using System.Windows.Threading;

namespace Notchless.Services;

public sealed class BrightnessService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private ManagementEventWatcher? _watcher;
    private ManagementObject? _brightnessObj;
    public event Action<int>? BrightnessChanged; // 0..100
    public event Action<bool>? AvailabilityChanged;
    public int Brightness { get; private set; } = 50;
    public bool IsSupported { get; private set; } = true;

    public BrightnessService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        TryInitWmi();
        TryInitEventWatcher();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        Poll();
    }
    private void OnTimerTick(object? s, EventArgs e) => Poll();

    private void TryInitEventWatcher()
    {
        try
        {
            var query = new WqlEventQuery("__InstanceModificationEvent", TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'WmiMonitorBrightness'");
            _watcher = new ManagementEventWatcher(@"root\WMI", query.QueryString);
            _watcher.EventArrived += OnWmiEvent;
            _watcher.Start();
        }
        catch
        {
            _watcher?.Dispose(); _watcher = null;
        }
    }
    private void OnWmiEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            using var target = (ManagementBaseObject?)e.NewEvent["TargetInstance"];
            if (target == null) return;
            var v = Convert.ToInt32(target["CurrentBrightness"]);
            if (v != Brightness)
            {
                Brightness = v;
                if (_dispatcher.HasShutdownStarted) return;
                _dispatcher.BeginInvoke(() => BrightnessChanged?.Invoke(Brightness));
            }
        }
        catch { }
    }

    private void TryInitWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness");
            var coll = searcher.Get();
            bool found = false;
            ManagementObject? keep = null;
            foreach (ManagementObject o in coll)
            {
                if (!found)
                {
                    keep = o;
                    found = true;
                }
                else
                {
                    o.Dispose();
                }
            }
            coll.Dispose();
            bool supported = found;
            if (supported)
            {
                _brightnessObj?.Dispose();
                _brightnessObj = keep;
            }
            else
            {
                keep?.Dispose();
                _brightnessObj?.Dispose();
                _brightnessObj = null;
            }
            if (IsSupported != supported)
            {
                IsSupported = supported;
                // marshal to UI thread
                if (_dispatcher.CheckAccess()) AvailabilityChanged?.Invoke(IsSupported);
                else _dispatcher.BeginInvoke(() => AvailabilityChanged?.Invoke(IsSupported));
            }
        }
        catch
        {
            _brightnessObj?.Dispose();
            _brightnessObj = null;
            if (IsSupported)
            {
                IsSupported = false;
                if (_dispatcher.CheckAccess()) AvailabilityChanged?.Invoke(false);
                else _dispatcher.BeginInvoke(() => AvailabilityChanged?.Invoke(false));
            }
        }
    }

    private void Poll()
    {
        try
        {
            if (_brightnessObj == null) { TryInitWmi(); if (_brightnessObj == null) return; }
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var coll = searcher.Get();
            foreach (ManagementObject o in coll)
            {
                using (o)
                {
                    var v = Convert.ToInt32(o["CurrentBrightness"]);
                    if (v != Brightness)
                    {
                        Brightness = v;
                        if (_dispatcher.CheckAccess()) BrightnessChanged?.Invoke(Brightness);
                        else _dispatcher.BeginInvoke(() => BrightnessChanged?.Invoke(Brightness));
                    }
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
            using var coll = searcher.Get();
            foreach (ManagementObject o in coll)
            {
                using (o)
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)level });
                }
                break;
            }
            Brightness = level;
            if (_dispatcher.CheckAccess()) BrightnessChanged?.Invoke(Brightness);
            else _dispatcher.BeginInvoke(() => BrightnessChanged?.Invoke(Brightness));
        }
        catch { }
    }

    public void Dispose()
    {
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        if (_watcher != null)
        {
            try { _watcher.EventArrived -= OnWmiEvent; } catch { }
            try { _watcher.Stop(); } catch { }
            _watcher.Dispose(); _watcher = null;
        }
        _brightnessObj?.Dispose(); _brightnessObj = null;
    }
}
