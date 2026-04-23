using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Middleware;

/// <summary>
/// Intercepts requests for Jellyfin's main web shell (/, /web/, /web/index.html)
/// and injects a single &lt;script src="/JellyFusion/bootstrap.js"&gt; tag just
/// before &lt;/body&gt; so that the plugin's client-side features (banner, rails,
/// theme CSS, studios, localisation) are loaded on every Jellyfin client.
///
/// Without this middleware the only visible effect of the plugin would be the
/// badge overlays on images — the whole Netflix-style home experience is
/// delivered by bootstrap.js.
/// </summary>
public class ClientScriptInjectionMiddleware : IMiddleware
{
    private readonly ILogger<ClientScriptInjectionMiddleware> _logger;

    // Tag that will be injected. Idempotency guard: we check for this exact
    // string before injecting so a second pass through the pipeline (e.g.
    // response caching) cannot double-inject the script.
    private const string InjectionMarker = "jellyfusion-bootstrap";
    private const string InjectionTag    =
        "<script src=\"/JellyFusion/bootstrap.js\" id=\"" + InjectionMarker + "\" defer></script>";

    public ClientScriptInjectionMiddleware(ILogger<ClientScriptInjectionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // ClientScriptInjection lives on SliderConfig (historical reasons) —
        // read it defensively so a missing/broken config never crashes the
        // request pipeline. Default to TRUE so the plugin works out of the
        // box.
        var slider            = Plugin.Instance?.Configuration?.Slider;
        bool injectionEnabled = slider is null ? true : slider.ClientScriptInjection;

        if (!injectionEnabled)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        // Only the main web shell. We use a case-insensitive exact match so we
        // don't accidentally rewrite every HTML response (web fonts, chunks,
        // etc.) which would be catastrophic.
        bool isWebShell =
            path.Equals("/",                StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web",             StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web/",            StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web/index.html",  StringComparison.OrdinalIgnoreCase);

        if (!isWebShell || !HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        // Capture the upstream response into memory so we can rewrite it.
        var originalBody  = context.Response.Body;
        using var buffer  = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            // Only rewrite HTML responses. Jellyfin serves /web/index.html as
            // text/html; anything else (304, error, static asset swap) flows
            // through unchanged.
            var contentType = context.Response.ContentType ?? "";
            bool isHtml =
                contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

            buffer.Seek(0, SeekOrigin.Begin);

            if (!isHtml || context.Response.StatusCode != StatusCodes.Status200OK)
            {
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            // Decode, inject, re-encode. Jellyfin ships index.html as UTF-8.
            string html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

            if (html.Contains(InjectionMarker, StringComparison.OrdinalIgnoreCase))
            {
                // Already injected — nothing to do.
                context.Response.Body = originalBody;
                var asIs = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength = asIs.Length;
                await originalBody.WriteAsync(asIs);
                return;
            }

            int bodyIdx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            string rewritten = bodyIdx >= 0
                ? html.Insert(bodyIdx, InjectionTag)
                : html + InjectionTag; // fall back: append at the end

            byte[] bytes = Encoding.UTF8.GetBytes(rewritten);
            context.Response.Body          = originalBody;
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);

            _logger.LogDebug("JellyFusion bootstrap.js injected into {Path}", path);
        }
        catch (Exception ex)
        {
            // Never let the plugin break Jellyfin's main page. On any error
            // we flush the captured buffer to the real response and log.
            _logger.LogWarning(ex, "ClientScriptInjectionMiddleware failed on {Path}", path);
            try
            {
                context.Response.Body = originalBody;
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
            }
            catch { /* response already started — nothing we can do */ }
        }
        finally
        {
            // Ensure we don't leave the replaced stream in place if something
            // unexpected happened before we restored it.
            if (context.Response.Body != originalBody)
            {
                context.Response.Body = originalBody;
            }
        }
    }
}
