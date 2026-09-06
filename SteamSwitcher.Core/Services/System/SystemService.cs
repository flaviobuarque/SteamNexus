using Microsoft.Win32;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public class SystemService : ISystemService
{
    private const string AppName = "SteamSwitcher";
    private const string ActivationEventName = "Local\\SteamSwitcher_Activate";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private static System.Threading.Mutex? _mutex;
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
        _mutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: $"Global\\{AppName}_SingleInstance",
            out bool createdNew);

        if (!createdNew)
        {
            // A janela pode estar no tray e não é confiável usar FindWindow.
            // Sinalizamos a instância dona para ela se restaurar no próprio UI thread.
            SignalExistingInstance();
            brought = true;
            return false;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ExistingInstanceActivated?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    private static void SignalExistingInstance()
    {
        // Há uma janela mínima entre a criação do mutex e do evento na primeira
        // inicialização. Repetimos brevemente para que a segunda execução nunca
        // abra uma janela vazia nesse intervalo.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
        }
    }
}
