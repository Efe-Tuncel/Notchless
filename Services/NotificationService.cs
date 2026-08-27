using System;
using System.Threading.Tasks;

namespace Notchless.Services;

public class NotificationInfo
{
    public string AppName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

// Stub — gerçek OS bildirim dinleme WinRT yüzünden build patlatıyordu (v1.0.6-1.0.8).
// Şimdilik sadece fake (Ctrl+Shift+N) çalışır, build geçer. Sonra Microsoft.Windows.SDK.Contracts ile geri açacağız.
public class NotificationService : IDisposable
{
    public event Action<NotificationInfo>? NotificationReceived;

    public bool IsSupported { get; private set; }
    public string Status { get; private set; } = "Stub — OS dinleme kapalı (build fix)";

    public Task<bool> TryEnableAsync()
    {
        IsSupported = false;
        Status = "Stub";
        return Task.FromResult(false);
    }

    // İlerde WinRT açılınca buradan tetiklenecek
    public void Simulate(NotificationInfo info) => NotificationReceived?.Invoke(info);

    public void Disable() { IsSupported = false; }
    public void Dispose() => Disable();
}
