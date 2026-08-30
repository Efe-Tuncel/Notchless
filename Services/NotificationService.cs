using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Notchless.Services;

public class NotificationInfo
{
    public string AppName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public BitmapImage? AppIcon { get; set; }
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

    private static async Task<BitmapImage?> TryGetAppLogoAsync(Windows.ApplicationModel.AppInfo appInfo)
    {
        try
        {
            var logoRef = appInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(44, 44));
            if (logoRef == null) return null;
            using var stream = await logoRef.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 5_000_000) return null;
            using var dr = new DataReader(stream.GetInputStreamAt(0));
            await dr.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            dr.ReadBytes(bytes);
            dr.DetachStream();
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 34;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }
        catch { return null; }
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
                try
                {
                    _listener.NotificationChanged += OnNotificationChanged;
                    Log("NotificationChanged subscribed");
                }
                catch (Exception ex)
                {
                    Log($"Subscribe failed (unpackaged 0x80070490 expected): {ex.Message} — polling+UIA fallback kullanılacak");
                }
                // Polling yedek — unpackaged'de event çalışmaz ama GetNotificationsAsync yine çalışabilir
                _seen.Clear();
                try
                {
                    var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                    foreach (var n in existing) _seen.Add(n.Id);
                    Log($"Seeded {_seen.Count} existing notifications");
                }
                catch (Exception ex) { Log($"Seed failed: {ex.Message}"); }
                _pollTimer = new System.Threading.Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
                Log($"Listener polling started (2s)");
                // Unpackaged'de GetNotificationsAsync da zaman zaman yetkisiz kalıyor — UIA'yı her zaman yedek olarak başlat
                StartUIAFallback();
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

            // Unpackaged fallback — sadece gerçek toast host'larını tara, genel pencereleri değil.
            // Windows 11'de toast'lar ShellExperienceHost.exe içinde, class whitelist ile geliyor.
            // Eski kodda ControlType.Window fallback tüm pencereleri (CabinetWClass, Chrome_WidgetWin_1) "bildirim" sanıyordu — düzeltildi.
            var whitelist = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Windows.UI.Notifications.ToastWindow",
                "Windows.UI.Core.CoreWindow",
                "XamlExplorerHostIslandWindow"
            };

            // Tüm top-level Window'ları al, sonra process + class ile filtrele
            var winCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
            var allWins = root.FindAll(TreeScope.Children, winCond);
            foreach (AutomationElement el in allWins)
            {
                try
                {
                    string className = el.Current.ClassName ?? "";
                    if (!whitelist.Contains(className)) continue;

                    // Process filtre — sadece ShellExperienceHost (Win11) / explorer host edenler, Chrome/Explorer'ı ele
                    int pid = 0;
                    try { pid = el.Current.ProcessId; } catch { continue; }
                    string procName = "";
                    try { using var p = System.Diagnostics.Process.GetProcessById(pid); procName = p.ProcessName; } catch { continue; }
                    bool isHost = procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                               || procName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
                    if (!isHost) continue;

                    // Boyut heuristiği — toast'lar küçük ve sağ-alt köşede, tam ekran pencere değil
                    try
                    {
                        var rect = el.Current.BoundingRectangle;
                        if (rect.Width > 600 || rect.Height > 250 || rect.Width < 200 || rect.Height < 40) continue;
                        // Ekran sağ-alt kontrolü (opsiyonel, fazla kısıtlamamak için sadece çok büyükleri eledik)
                    }
                    catch { }

                    string name = el.Current.Name ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // Name genellikle "AppName\nTitle\nBody" formatında — CabinetWClass gibi tek kelimelikleri ele
                    if (!name.Contains("\n") && !name.Contains("\r")) continue;
                    if (name.Contains("Notchless", StringComparison.OrdinalIgnoreCase)) continue;

                    string hash = $"{name}|{el.Current.AutomationId}|{className}";
                    if (hash == _lastToastHash) continue;
                    _lastToastHash = hash;

                    var parts = name.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    string appName = parts.Length > 0 ? parts[0] : "Bildirim";
                    string title = parts.Length > 1 ? parts[1] : name;
                    string body = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "";
                    if (title.Length > 80) { body = title.Substring(80); title = title.Substring(0, 80); }
                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) continue;
                    Log($"UIA found: {appName} | {title} [{className}/{procName}]");
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
                    var icon = await TryGetAppLogoAsync(n.AppInfo);
                    var info = new NotificationInfo { AppName = appName, Title = title, Text = body, AppIcon = icon };
                    Log($"Poll found: {appName} | {title}");
                    NotificationReceived?.Invoke(info);
                }
                catch (Exception ex) { Log($"Poll parse err: {ex.Message}"); }
            }
            // temizlik: artık aktif olmayan ID'leri unut — listeyi tamamen boşaltmak
            // hâlâ duran bildirimleri yeniden gösteriyordu, onun yerine mevcutlarla kesiş
            if (_seen.Count > 200) _seen.IntersectWith(list.Select(n => n.Id));
        }
        catch (Exception ex) { Log($"PollAsync err: {ex.Message}"); }
    }

    private async void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
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

            var icon = await TryGetAppLogoAsync(notif.AppInfo);
            var info = new NotificationInfo { AppName = appName, Title = title, Text = body, AppIcon = icon };
            NotificationReceived?.Invoke(info);
        }
        catch { }
    }

    public void Disable()
    {
        try { if (_listener != null) _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        _pollTimer?.Dispose(); _pollTimer = null;
        _uiaTimer?.Dispose(); _uiaTimer = null;
        IsSupported = false;
        Log("Disabled");
    }

    public void Dispose() => Disable();
}
