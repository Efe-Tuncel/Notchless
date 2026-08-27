using System;
using System.Runtime.InteropServices;
using Notchless.Helpers;

namespace Notchless.Services;

/// <summary>
/// Faz 5 — ses tuşu yakalama (WH_KEYBOARD_LL). Yalnızca ses tuşları (VolumeUp/Down/Mute) yakalanır, parlaklık dahil değildir (EC/BIOS).
/// Varsayılan kapalı — kullanıcı toggle ile açar. Başarıyla yakalanırsa 1 dönerek sistem OSD bastırılır.
/// Caller (IslandWindow) VolumeKeyPressed'i dispatcher üzerinden AudioService'e bağlar.
/// </summary>
public sealed class AudioKeyHookService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    public bool IsEnabled { get; private set; }
    public event Action<int>? VolumeKeyPressed; // VK_VOLUME_*

    public bool TryEnable()
    {
        if (IsEnabled) return true;
        _proc = HookCallback;
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        IsEnabled = _hookId != IntPtr.Zero;
        return IsEnabled;
    }

    public void Disable()
    {
        if (_hookId != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero; IsEnabled = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)kb.vkCode;
            if (vk == NativeMethods.VK_VOLUME_MUTE || vk == NativeMethods.VK_VOLUME_DOWN || vk == NativeMethods.VK_VOLUME_UP)
            {
                VolumeKeyPressed?.Invoke(vk);
                // 1 dönerek tuşu yut — sistem OSD bastırılır, IslandWindow kendi HUD'unu gösterecek
                return (IntPtr)1;
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Disable();
}
