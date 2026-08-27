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

    public record BtBattery(string Name, int Percent, string DeviceId);

    public BluetoothBatteryService()
    {
        _pollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _ = RefreshAsync();
        _pollTimer.Start();
    }

    public async Task RefreshAsync()
    {
        var list = new List<BtBattery>();
        try
        {
            // Eşleşmiş Bluetooth cihazlarını bul
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var di in devices)
            {
                try
                {
                    var bt = await BluetoothLEDevice.FromIdAsync(di.Id);
                    if (bt == null) continue;
                    var services = await bt.GetGattServicesAsync();
                    if (services.Status != GattCommunicationStatus.Success) { bt.Dispose(); continue; }
                    foreach (var svc in services.Services)
                    {
                        if (svc.Uuid != BatteryServiceUuid) { svc.Dispose(); continue; }
                        var chars = await svc.GetCharacteristicsAsync();
                        if (chars.Status != GattCommunicationStatus.Success) { svc.Dispose(); continue; }
                        foreach (var ch in chars.Characteristics)
                        {
                            if (ch.Uuid != BatteryLevelUuid) continue;
                            var read = await ch.ReadValueAsync();
                            if (read.Status == GattCommunicationStatus.Success)
                            {
                                var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                                byte lvl = reader.ReadByte();
                                list.Add(new BtBattery(bt.Name ?? di.Name, lvl, di.Id));
                            }
                            break;
                        }
                        svc.Dispose();
                    }
                    // Gatt characteristic/service dispose sonrası LE device dispose
                    bt.Dispose();
                }
                catch { }
            }
        }
        catch { }
        BatteriesChanged?.Invoke(list);
    }

    public void Dispose()
    {
        _pollTimer.Stop();
    }
}
