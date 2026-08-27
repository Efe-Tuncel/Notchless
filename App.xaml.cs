using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Notchless;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _tray;
    private IslandWindow? _island;

    private static void Log(string msg)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("=== OnStartup begin ===");
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) =>
            {
                Log($"DispatcherUnhandled: {ex.Exception}");
                try { System.Windows.MessageBox.Show($"Notchless hata:\n{ex.Exception}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Log($"AppDomainUnhandled: {ex.ExceptionObject}");
                try { System.Windows.MessageBox.Show($"Notchless fatal:\n{ex.ExceptionObject}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            };

            Log("Creating tray");
            _tray = new NotifyIcon
            {
                Text = "Notchless — Dinamik Ada",
                Visible = true,
                Icon = SystemIcons.Application
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Kontrol Merkezini Aç", null, (_, _) => _island?.Dispatcher.BeginInvoke(() =>
            {
                _island.Show();
                _island.Activate();
            }));
            menu.Items.Add("Ekran Görüntüsünden Gizle", null, (_, _) =>
            {
                if (_island == null) return;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_island).Handle;
                if (hwnd != IntPtr.Zero) Helpers.NativeMethods.SetWindowDisplayAffinity(hwnd, Helpers.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, _) => Shutdown());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => _island?.Activate();
            Log("Tray created");

            Log("Creating IslandWindow");
            _island = new IslandWindow();
            Log("IslandWindow ctor done, calling Show()");
            _island.Show();
            Log("Show() done");

            SystemEvents.DisplaySettingsChanged += (_, _) => _island?.Dispatcher.BeginInvoke(() => { });
            Log("OnStartup success");
        }
        catch (Exception ex)
        {
            Log($"OnStartup catch: {ex}");
            try { System.Windows.MessageBox.Show($"Notchless OnStartup hata:\n{ex}\n\nLog: %LocalAppData%\\Notchless\\startup.log", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}

