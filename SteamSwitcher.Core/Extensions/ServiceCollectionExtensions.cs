using Microsoft.Extensions.DependencyInjection;
using SteamSwitcher.Core.Services;

namespace SteamSwitcher.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSteamSwitcherCore(this IServiceCollection services)
    {
        // Infraestrutura
        services.AddSingleton<ISteamInstallationService, SteamInstallationService>();
        services.AddSingleton<ISteamLocatorService, SteamLocatorService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<ISteamKnownAccountStore, SteamKnownAccountStore>();
        services.AddSingleton<ISteamDiagnosticsService, SteamDiagnosticsService>();

        // Contas
        services.AddSingleton<IAccountOverrideService, AccountOverrideService>();
        services.AddSingleton<ISteamAccountService, SteamAccountService>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();

        // Jogos
        services.AddSingleton<ISteamGameService, SteamGameService>();

        // Sistema
        services.AddSingleton<ISystemService, SystemService>();
        services.AddSingleton<IWatchdogService, WatchdogService>();
        services.AddSingleton<IModMonitorService, ModMonitorService>();

        // Onboarding
        services.AddSingleton<IOnboardingService, OnboardingService>();

        return services;
    }
}
