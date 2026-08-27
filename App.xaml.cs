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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show($"Notchless hata:\n{ex.Exception}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            try { System.Windows.MessageBox.Show($"Notchless fatal:\n{ex.ExceptionObject}", "Notchless", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
        };

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
            // trigger expand via reflection: call AnimateTo ControlCenter - simply show
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

        _island = new IslandWindow();
        _island.Show();

        SystemEvents.DisplaySettingsChanged += (_, _) => _island?.Dispatcher.BeginInvoke(() => { /* reposition handled in island */ });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}

