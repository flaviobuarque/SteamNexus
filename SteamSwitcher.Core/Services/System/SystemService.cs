using Microsoft.Win32;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public class SystemService : ISystemService, IDisposable
{
    private const string AppName = "SteamSwitcher";
    private const string ActivationEventName = "Local\\SteamSwitcher_Activate";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private System.Threading.Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;

    public event EventHandler? ExistingInstanceActivated;

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
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _mutex = new System.Threading.Mutex(
            initiallyOwned: false,
            name: $"Local\\{AppName}_SingleInstance",
            out bool createdNew);

        if (!createdNew)
        {
            // A janela pode estar no tray e não é confiável usar FindWindow.
            // Sinalizamos a instância dona para ela se restaurar no próprio UI thread.
            _activationEvent.Set();
            brought = true;
            return false;
        }

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ExistingInstanceActivated?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    public void Dispose()
    {
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        _mutex?.Dispose();
    }
}
