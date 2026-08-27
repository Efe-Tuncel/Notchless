using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications.Management;

namespace Notchless.Services;

public class NotificationInfo
{
    public string AppName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public class NotificationService : IDisposable
{
    private UserNotificationListener? _listener;

    public event Action<NotificationInfo>? NotificationReceived;

    public bool IsSupported { get; private set; }
    public string Status { get; private set; } = "Kapalı";

    public async Task<bool> TryEnableAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            IsSupported = access == UserNotificationListenerAccessStatus.Allowed;
            Status = access.ToString();
            if (IsSupported)
            {
                _listener.NotificationChanged += OnNotificationChanged;
                // ilk yüklemede mevcut bildirimleri tetikleme, sadece yeniler
            }
            return IsSupported;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            IsSupported = false;
            return false;
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        try
        {
            if (args.ChangeKind != UserNotificationChangedKind.Added) return;
            var notif = sender.GetNotification(args.UserNotificationId);
            if (notif == null) return;
            var binding = notif.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding == null) return;

            string title = "";
            string body = "";
            string appName = notif.AppInfo.DisplayInfo.DisplayName ?? notif.AppInfo.PackageFamilyName ?? "Bildirim";

            // ToastGeneric: ilk Text = title, ikinciler body
            var texts = binding.GetTextElements().ToList();
            if (texts.Count > 0) title = texts[0].Text ?? "";
            if (texts.Count > 1) body = string.Join(" ", texts.Skip(1).Select(t => t.Text));

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return;
            // Sistem / Notchless kendi bildirimlerini yoksay
            if (appName.Contains("Notchless", StringComparison.OrdinalIgnoreCase)) return;

            var info = new NotificationInfo { AppName = appName, Title = title, Text = body };
            NotificationReceived?.Invoke(info);
        }
        catch { }
    }

    public void Disable()
    {
        if (_listener != null) _listener.NotificationChanged -= OnNotificationChanged;
        IsSupported = false;
    }

    public void Dispose() => Disable();
}
