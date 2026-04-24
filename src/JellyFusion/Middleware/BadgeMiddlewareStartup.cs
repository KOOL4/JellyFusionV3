using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace JellyFusion.Middleware;

/// <summary>
/// Wires <see cref="BadgeMiddleware"/> into the ASP.NET Core request pipeline so that
/// badge overlays are composited on every Jellyfin image request.
/// Registered as an <see cref="IStartupFilter"/> so Jellyfin picks it up automatically.
/// </summary>
public class BadgeMiddlewareStartup : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<BadgeMiddleware>();
            next(app);
        };
}
