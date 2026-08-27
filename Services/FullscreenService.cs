using System;
using System.Windows;
using Notchless.Helpers;

namespace Notchless.Services;

/// <summary>
/// Tam ekranda ada gizlenir (ShowWindow SW_HIDE), çıkınca geri gelir.
/// GetForegroundWindow + pencere/ekran boyutu karşılaştırması.
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
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) { Show(); return; }
            if (!NativeMethods.GetWindowRect(fg, out var r)) { Show(); return; }
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
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        _isHidden = true;
    }
    private void Show()
    {
        if (!_isHidden) return;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
        // Topmost'u koru
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        _isHidden = false;
    }

    public void Dispose() { _timer.Stop(); Show(); }
}
