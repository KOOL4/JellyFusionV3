using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace JellyFusion.Middleware;

/// <summary>
/// Wires <see cref="ClientScriptInjectionMiddleware"/> into the ASP.NET Core
/// request pipeline. Registered as an <see cref="IStartupFilter"/> so Jellyfin
/// auto-picks it up without requiring a custom Startup class.
/// </summary>
public class ClientScriptInjectionStartup : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<ClientScriptInjectionMiddleware>();
            next(app);
        };
}
