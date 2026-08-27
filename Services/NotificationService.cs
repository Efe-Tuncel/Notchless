using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.UI.Notifications;
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
    private System.Threading.Timer? _pollTimer;
    private System.Collections.Generic.HashSet<uint> _seen = new();
    private System.Threading.Timer? _uiaTimer;
    private string _lastToastHash = "";

    public event Action<NotificationInfo>? NotificationReceived;

    public bool IsSupported { get; private set; }
    public string Status { get; private set; } = "Kapalı";

    private static void Log(string msg)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [Notif] {msg}\n");
        }
        catch { }
    }

    public async Task<bool> TryEnableAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            Log($"RequestAccessAsync start");
            var access = await _listener.RequestAccessAsync();
            IsSupported = access == UserNotificationListenerAccessStatus.Allowed;
            Status = access.ToString();
            Log($"RequestAccessAsync result: {Status}");
            if (IsSupported)
            {
                _listener.NotificationChanged += OnNotificationChanged;
                // Polling yedek — bazı unpackaged senaryolarda NotificationChanged tetiklenmiyor
                _seen.Clear();
                try
                {
                    var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                    foreach (var n in existing) _seen.Add(n.Id);
                    Log($"Seeded {_seen.Count} existing notifications");
                }
                catch (Exception ex) { Log($"Seed failed: {ex.Message}"); }
                _pollTimer = new System.Threading.Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
                Log($"Listener enabled + polling started");
            }
            else
            {
                Log($"Access denied: {Status} — UIA fallback başlatılıyor (unpackaged için)");
                StartUIAFallback();
                // Denied olsa bile stub değil, UIA ile dene — IsSupported false kalsın ama polling çalışsın
            }
            return IsSupported;
        }
        catch (Exception ex)
        {
            Status = ex.ToString();
            Log($"TryEnable exception: {ex} — UIA fallback");
            StartUIAFallback();
            IsSupported = false;
            return false;
        }
    }

    private void StartUIAFallback()
    {
        try
        {
            _uiaTimer?.Dispose();
            _uiaTimer = new System.Threading.Timer(_ => PollUIA(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Log("UIA fallback timer started");
        }
        catch (Exception ex) { Log($"UIA start err: {ex.Message}"); }
    }

    private void PollUIA()
    {
        try
        {
            var root = AutomationElement.RootElement;
            if (root == null) return;
            // Toast pencereleri genellikle "Windows.UI.Notifications.ToastWindow" class'ı ile gelir
            var cond = new PropertyCondition(AutomationElement.ClassNameProperty, "Windows.UI.Notifications.ToastWindow");
            var toasts = root.FindAll(TreeScope.Children, cond);
            foreach (AutomationElement el in toasts)
            {
                try
                {
                    string name = el.Current.Name ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string hash = name.GetHashCode().ToString() + el.Current.AutomationId;
                    if (hash == _lastToastHash) continue;
                    _lastToastHash = hash;
                    // Name genellikle "AppName\nTitle\nBody" formatında
                    var parts = name.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    string appName = parts.Length > 0 ? parts[0] : "Bildirim";
                    string title = parts.Length > 1 ? parts[1] : name;
                    string body = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "";
                    if (title.Length > 80) { body = title.Substring(80); title = title.Substring(0, 80); }
                    Log($"UIA found: {appName} | {title}");
                    var info = new NotificationInfo { AppName = appName, Title = title, Text = body };
                    NotificationReceived?.Invoke(info);
                    break; // bir tane yeterli, diğer tick'te yenisi
                }
                catch { }
            }
        }
        catch (Exception ex) { Log($"PollUIA err: {ex.Message}"); }
    }

    private async Task PollAsync()
    {
        try
        {
            if (_listener == null) return;
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in list)
            {
                if (_seen.Contains(n.Id)) continue;
                _seen.Add(n.Id);
                // aynı parse mantığı
                try
                {
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (binding == null) continue;
                    string appName = n.AppInfo.DisplayInfo.DisplayName ?? n.AppInfo.AppUserModelId ?? "Bildirim";
                    if (appName.Contains("Notchless", StringComparison.OrdinalIgnoreCase)) continue;
                    var texts = binding.GetTextElements().ToList();
                    string title = texts.Count > 0 ? texts[0].Text ?? "" : "";
                    string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1).Select(t => t.Text)) : "";
                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) continue;
                    var info = new NotificationInfo { AppName = appName, Title = title, Text = body };
                    Log($"Poll found: {appName} | {title}");
                    NotificationReceived?.Invoke(info);
                }
                catch (Exception ex) { Log($"Poll parse err: {ex.Message}"); }
            }
            // temizlik: kapanan bildirimleri unutma ki liste şişmesin (30dk'dan eskiyi at)
            if (_seen.Count > 200) _seen.Clear();
        }
        catch (Exception ex) { Log($"PollAsync err: {ex.Message}"); }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        try
        {
            if (args.ChangeKind != UserNotificationChangedKind.Added) return;
            var notif = sender.GetNotification(args.UserNotificationId);
            if (notif == null) return;
            var visual = notif.Notification.Visual;
            if (visual == null) return;
            var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding == null) return;

            string appName = notif.AppInfo.DisplayInfo.DisplayName ?? notif.AppInfo.AppUserModelId ?? "Bildirim";
            if (appName.Contains("Notchless", StringComparison.OrdinalIgnoreCase)) return;

            var texts = binding.GetTextElements().ToList();
            string title = texts.Count > 0 ? texts[0].Text ?? "" : "";
            string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1).Select(t => t.Text)) : "";

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return;

            var info = new NotificationInfo { AppName = appName, Title = title, Text = body };
            NotificationReceived?.Invoke(info);
        }
        catch { }
    }

    public void Disable()
    {
        if (_listener != null) _listener.NotificationChanged -= OnNotificationChanged;
        _pollTimer?.Dispose(); _pollTimer = null;
        _uiaTimer?.Dispose(); _uiaTimer = null;
        IsSupported = false;
        Log("Disabled");
    }

    public void Dispose() => Disable();
}
