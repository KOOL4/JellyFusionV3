using JellyFusion.Middleware;
using JellyFusion.Modules.Badges;
using JellyFusion.Modules.Home;
using JellyFusion.Modules.Notifications;
using JellyFusion.Modules.Slider;
using JellyFusion.Modules.Studios;
using JellyFusion.Modules.Themes;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace JellyFusion;

/// <summary>
/// Registers all JellyFusion services into the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    // Jellyfin 10.10 signature takes IServerApplicationHost, not IServiceProvider.
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Core services
        serviceCollection.AddSingleton<LocalizationService>();

        // Module: Editor's Choice slider
        serviceCollection.AddSingleton<SliderService>();
        serviceCollection.AddSingleton<TrailerService>();
        serviceCollection.AddHostedService<SliderHostedService>();

        // Module: JellyTag badges
        serviceCollection.AddSingleton<BadgeService>();
        serviceCollection.AddSingleton<BadgeRenderService>();
        serviceCollection.AddSingleton<ImageCacheService>();

        // Module: Studios
        serviceCollection.AddSingleton<StudiosService>();

        // Module: Home rails
        serviceCollection.AddSingleton<HomeService>();

        // Module: Themes
        serviceCollection.AddSingleton<ThemeService>();

        // Module: Notifications
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddHostedService<NotificationHostedService>();

        // Middleware (IMiddleware requires DI registration)
        serviceCollection.AddTransient<BadgeMiddleware>();
        // Wire BadgeMiddleware into the ASP.NET Core pipeline via IStartupFilter
        serviceCollection.AddSingleton<IStartupFilter, BadgeMiddlewareStartup>();

        // HTTP client for TMDB / external APIs
        serviceCollection.AddHttpClient("JellyFusion");
    }
}
