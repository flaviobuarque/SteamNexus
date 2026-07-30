using Microsoft.Extensions.DependencyInjection;

namespace SteamSwitcher;

public partial class App
{
    public static T GetService<T>() where T : class
    {
        if (Current is App app && app._host is not null)
            return app._host.Services.GetRequiredService<T>();

        throw new InvalidOperationException("Host não inicializado.");
    }
}