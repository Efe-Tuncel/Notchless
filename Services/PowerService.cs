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

    public PowerService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Poll();
        Poll();
        _timer.Start();
    }

    private void Poll()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var s)) return;
        // Masaüstü: BatteryFlag=128, LifePercent=255 veya ACLineStatus=1 + NoSystemBattery
        bool hasBattery = s.BatteryFlag != 128 && s.BatteryLifePercent != 255;
        // Bazen masaüstünde BatteryFlag=128 gelmez ama 255 de pil yok demektir
        if (s.BatteryFlag == 128) hasBattery = false;
        HasBattery = hasBattery;
        HasBatteryChanged?.Invoke(HasBattery);

        if (!hasBattery)
        {
            BatteryPercent = 100;
            IsCharging = s.ACLineStatus == 1 || s.ACLineStatus == 255;
            IsBatterySaver = false;
        }
        else
        {
            BatteryPercent = s.BatteryLifePercent;
            IsCharging = s.ACLineStatus == 1;
            IsBatterySaver = s.SystemStatusFlag == 1;
        }
        PowerChanged?.Invoke(BatteryPercent, IsCharging, IsBatterySaver, HasBattery);
    }

    public void Dispose() => _timer.Stop();
}
