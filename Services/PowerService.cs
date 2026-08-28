using System;
using System.Runtime.InteropServices;
using Notchless.Helpers;
using System.Windows.Threading;

namespace Notchless.Services;

public sealed class PowerService : IDisposable
{
    private readonly DispatcherTimer _timer;
    public event Action<int, bool, bool, bool>? PowerChanged; // percent, isCharging, isSaver, hasBattery
    public event Action<bool>? HasBatteryChanged;

    public int BatteryPercent { get; private set; } = 100;
    public bool IsCharging { get; private set; }
    public bool IsBatterySaver { get; private set; }
    public bool HasBattery { get; private set; } = true;

    private int _lastPercent = -1;
    private bool _lastCharging, _lastSaver, _lastHasBattery;

    public PowerService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTick;
        Poll();
        _timer.Start();
    }
    private void OnTick(object? s, EventArgs e) => Poll();

    private void Poll()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var s)) return;
        bool hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryLifePercent != 255;
        bool hasBatteryChanged = hasBattery != HasBattery;
        if (hasBatteryChanged)
        {
            HasBattery = hasBattery;
        }

        if (!hasBattery)
        {
            BatteryPercent = 100;
            IsCharging = s.ACLineStatus == 1;
            IsBatterySaver = false;
        }
        else
        {
            BatteryPercent = s.BatteryLifePercent;
            IsCharging = s.ACLineStatus == 1;
            IsBatterySaver = s.SystemStatusFlag == 1;
        }
        bool percentChanged = _lastPercent != BatteryPercent;
        bool chargingChanged = _lastCharging != IsCharging;
        bool saverChanged = _lastSaver != IsBatterySaver;
        bool batteryPresenceChanged = _lastHasBattery != HasBattery;
        if (percentChanged || chargingChanged || saverChanged || batteryPresenceChanged)
        {
            _lastPercent = BatteryPercent; _lastCharging = IsCharging; _lastSaver = IsBatterySaver; _lastHasBattery = HasBattery;
            PowerChanged?.Invoke(BatteryPercent, IsCharging, IsBatterySaver, HasBattery);
            if (batteryPresenceChanged) HasBatteryChanged?.Invoke(HasBattery);
        }
    }

    public void Dispose() { _timer.Tick -= OnTick; _timer.Stop(); }
}
