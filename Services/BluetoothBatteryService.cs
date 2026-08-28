using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace Notchless.Services;

/// <summary>
/// Sadeleştirilmiş Bluetooth pil: yalnızca standart GATT Battery Service (0x180F, 0x2A19) yayınlayan cihazlar.
/// Üretici-özel protokol kapsam dışı; pil yoksa hiçbir şey gösterilmez.
/// </summary>
public sealed class BluetoothBatteryService : IDisposable
{
    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");

    public event Action<IReadOnlyList<BtBattery>>? BatteriesChanged;
    private readonly System.Windows.Threading.DispatcherTimer _pollTimer;
    private readonly System.Threading.SemaphoreSlim _gate = new(1,1);
    private bool _disposed;

    public record BtBattery(string Name, int Percent, string DeviceId);

    public BluetoothBatteryService()
    {
        _pollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _pollTimer.Tick += OnTick;
        _ = RefreshAsync();
        _pollTimer.Start();
    }
    private async void OnTick(object? s, EventArgs e) { if (!await _gate.WaitAsync(0)) return; try { await RefreshAsyncCore(); } finally { _gate.Release(); } }

    public Task RefreshAsync() => RefreshAsyncCore();
    private async Task RefreshAsyncCore()
    {
        if (_disposed) return;
        var list = new List<BtBattery>();
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var di in devices)
            {
                BluetoothLEDevice? bt = null;
                try
                {
                    bt = await BluetoothLEDevice.FromIdAsync(di.Id);
                    if (bt == null) continue;
                    var services = await bt.GetGattServicesAsync();
                    if (services.Status != GattCommunicationStatus.Success) continue;
                    foreach (var svc in services.Services)
                    {
                        using (svc)
                        {
                            if (svc.Uuid != BatteryServiceUuid) continue;
                            var chars = await svc.GetCharacteristicsAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                            if (chars.Status != GattCommunicationStatus.Success) continue;
                            foreach (var ch in chars.Characteristics)
                            {
                                if (ch.Uuid != BatteryLevelUuid) continue;
                                var read = await ch.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                                if (read.Status == GattCommunicationStatus.Success)
                                {
                                    using var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                                    byte lvl = reader.ReadByte();
                                    list.Add(new BtBattery(bt.Name ?? di.Name, lvl, di.Id));
                                }
                                break;
                            }
                            // GattCharacteristicsResult karakterleri zaten dispose edildi; servis using ile dispose olacak
                        }
                    }
                }
                catch { }
                finally { try { bt?.Dispose(); } catch { } }
            }
        }
        catch { }
        if (_disposed) return;
        // UI'ye marshal gerekebilir ama hafif — direkt invoke
        try { BatteriesChanged?.Invoke(list); } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer.Tick -= OnTick;
        _pollTimer.Stop();
        _gate.Dispose();
    }
}
