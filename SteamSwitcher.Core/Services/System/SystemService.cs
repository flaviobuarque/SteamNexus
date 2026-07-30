using Microsoft.Win32;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public class SystemService : ISystemService
{
    private const string AppName = "SteamSwitcher";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private static System.Threading.Mutex? _mutex;

    public void SetStartWithWindows(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enable)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            key.SetValue(AppName, $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public bool GetStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }

    public bool IsSingleInstance(out bool brought)
    {
        brought = false;
        _mutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: $"Global\\{AppName}_SingleInstance",
            out bool createdNew);

        if (!createdNew)
        {
            // Já existe outra instância — envia sinal pra ela
            BringExistingInstanceToFront();
            brought = true;
            return false;
        }

        return true;
    }

    public void BringExistingInstanceToFront()
    {
        // Busca a janela da instância já aberta pelo título
        var hwnd = NativeMethods.FindWindow(null, AppName);
        if (hwnd != nint.Zero)
        {
            NativeMethods.ShowWindow(hwnd, 9); // SW_RESTORE
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);
}