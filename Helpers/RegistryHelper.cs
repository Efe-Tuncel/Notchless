using System;
using Microsoft.Win32;

namespace Notchless.Helpers;

internal static class RegistryHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Notchless";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var v = k?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(v);
        }
        catch { return false; }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (k == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exe)) return;
                k.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            }
            else
            {
                try { k.DeleteValue(ValueName, false); } catch { }
            }
        }
        catch { }
    }

    public static string GetExePath()
    {
        try { return Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""; } catch { return ""; }
    }
}
