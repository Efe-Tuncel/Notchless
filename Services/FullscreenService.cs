using System;
using System.Windows;
using Notchless.Helpers;

namespace Notchless.Services;

/// <summary>
/// Tam ekranda ada gizlenir (ShowWindow SW_HIDE), çıkınca geri gelir.
/// GetForegroundWindow + pencere/ekran boyutu karşılaştırması.
/// DPI-aware karşılaştırma için GetWindowRect fiziksel piksel kullanır, Screen.Bounds da fiziksel piksel olduğundan uyumludur.
/// </summary>
public sealed class FullscreenService : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly IntPtr _hwnd;
    private bool _isHidden;

    public FullscreenService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private void Poll()
    {
        try
        {
            if (_hwnd == IntPtr.Zero) return;
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) { Show(); return; }
            if (!NativeMethods.GetWindowRect(fg, out var r)) { Show(); return; }

            // Masaüstü / shell pencereleri fullscreen sanılmasın — tıklayınca ada kaybolma bug'ı
            try
            {
                var sb = new System.Text.StringBuilder(256);
                NativeMethods.GetClassName(fg, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                { Show(); return; }
                // Explorer desktop'ı filtrele
                NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                    if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && (cls == "Progman" || cls == "WorkerW"))
                    { Show(); return; }
                }
                catch { }
            }
            catch { }

            // Tüm ekranları kontrol et — herhangi biriyle tam eşleşiyorsa fullscreen
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (r.Left == screen.Bounds.Left && r.Top == screen.Bounds.Top
                    && r.Width == screen.Bounds.Width && r.Height == screen.Bounds.Height)
                {
                    Hide(); return;
                }
                // Borderless fullscreen bazen 1px fark yapar — toleranslı kontrol
                if (Math.Abs(r.Width - screen.Bounds.Width) <= 2 && Math.Abs(r.Height - screen.Bounds.Height) <= 2
                    && Math.Abs(r.Left - screen.Bounds.Left) <= 2 && Math.Abs(r.Top - screen.Bounds.Top) <= 2)
                {
                    Hide(); return;
                }
            }
            Show();
        }
        catch { }
    }

    private void Hide()
    {
        if (_isHidden) return;
        try
        {
            // WPF ile senkronize: Dispatcher üzerinden Visibility ayarla, ardından native Hide
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            {
                if (System.Windows.Application.Current.Windows.Count > 0)
                {
                    foreach (Window w in System.Windows.Application.Current.Windows)
                    {
                        if (new System.Windows.Interop.WindowInteropHelper(w).Handle == _hwnd)
                        {
                            w.Visibility = Visibility.Hidden;
                            break;
                        }
                    }
                }
            }
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }
        catch { try { NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE); } catch { } }
        _isHidden = true;
    }
    private void Show()
    {
        if (!_isHidden) return;
        try
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
            // WPF Visibility senkronizasyonu
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            {
                foreach (Window w in System.Windows.Application.Current.Windows)
                {
                    if (new System.Windows.Interop.WindowInteropHelper(w).Handle == _hwnd)
                    {
                        if (w.Visibility != Visibility.Visible) w.Visibility = Visibility.Visible;
                        break;
                    }
                }
            }
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        catch { try { NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW); } catch { } }
        _isHidden = false;
    }

    public void Dispose() { try { _timer.Stop(); } catch { } Show(); }
}
