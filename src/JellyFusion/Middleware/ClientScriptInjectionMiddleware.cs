using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Middleware;

/// <summary>
/// Intercepts requests for Jellyfin's main web shell (/, /web/, /web/index.html)
/// and injects a single &lt;script src="/JellyFusion/bootstrap.js"&gt; tag just
/// before &lt;/body&gt; so that the plugin's client-side features (banner, rails,
/// theme CSS, studios, localisation) are loaded on every Jellyfin client.
///
/// IMPORTANT implementation detail: Jellyfin serves /web/index.html through
/// ASP.NET Core's UseStaticFiles middleware, which in turn calls
/// HttpContext.Response.SendFileAsync. SendFileAsync bypasses any stream we
/// swap into Response.Body unless we ALSO replace the
/// IHttpResponseBodyFeature on the Features collection. That's why v3.0.1's
/// middleware (which only replaced Response.Body) had zero visible effect:
/// the file was sent straight to the socket, our MemoryStream stayed empty,
/// and we had nothing to inject into. This version wraps the response body
/// feature as well, which correctly redirects SendFileAsync to our buffer.
/// </summary>
public class ClientScriptInjectionMiddleware : IMiddleware
{
    private readonly ILogger<ClientScriptInjectionMiddleware> _logger;

    // Idempotency guard: we check for this marker before injecting so repeat
    // passes (response caching, double-registration) cannot stack the tag.
    private const string InjectionMarker = "jellyfusion-bootstrap";
    private const string InjectionTag =
        "<script src=\"/JellyFusion/bootstrap.js\" id=\"" + InjectionMarker + "\" defer></script>";

    public ClientScriptInjectionMiddleware(ILogger<ClientScriptInjectionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Feature toggle, defensive. Default TRUE.
        var slider            = Plugin.Instance?.Configuration?.Slider;
        bool injectionEnabled = slider is null ? true : slider.ClientScriptInjection;
        if (!injectionEnabled)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        // Only intercept the main Jellyfin web shell. Anything else passes
        // through untouched so we never corrupt CSS/JS/font responses.
        bool isWebShell =
            path.Equals("/",               StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web",            StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web/",           StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase);

        if (!isWebShell || !HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        // ── CRITICAL (v3.0.2 hotfix): Jellyfin's pipeline has
        //    UseResponseCompression upstream of UseStaticFiles. Because
        //    ClientScriptInjectionStartup registers us via IStartupFilter,
        //    we wrap the ENTIRE pipeline - which means by the time the
        //    response lands in our buffer it may already be gzipped.
        //    Decoding gzip as UTF-8 produced the mojibake page the user
        //    saw in v3.0.1/v3.0.2 ("ï¿½" everywhere) while our injected
        //    script still loaded at the end (the "loaded" badge was the
        //    only readable thing on screen).
        //
        //    The simplest, safest fix is to strip Accept-Encoding from
        //    the REQUEST before we call next(). The compression middleware
        //    then sees the client as not supporting compression and
        //    passes the static file through as raw UTF-8. Because this
        //    only runs on GET / /web /web/ /web/index.html (the whole
        //    pipeline returns early for everything else), the cost is
        //    negligible - one uncompressed ~300 KB HTML per client load.
        var origAcceptEnc = context.Request.Headers["Accept-Encoding"];
        context.Request.Headers.Remove("Accept-Encoding");

        // ── Swap BOTH Response.Body and the IHttpResponseBodyFeature so
        //    SendFileAsync / StartAsync / CompleteAsync all land in buffer.
        var originalBody    = context.Response.Body;
        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>();
        using var buffer    = new MemoryStream();
        var replacement     = new StreamResponseBodyFeature(buffer);

        context.Response.Body = buffer;
        context.Features.Set<IHttpResponseBodyFeature>(replacement);

        try
        {
            await next(context);
            // Finalise any writes the downstream middleware made.
            await replacement.CompleteAsync();

            // Restore the real body / feature BEFORE we write anything back.
            context.Response.Body = originalBody;
            context.Features.Set<IHttpResponseBodyFeature>(originalFeature!);

            buffer.Seek(0, SeekOrigin.Begin);

            // Only rewrite successful HTML responses.
            var contentType = context.Response.ContentType ?? "";
            bool isHtml     = contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

            // Safety net for the v3.0.2 mojibake bug: if for ANY reason the
            // buffer content is compressed (Content-Encoding gzip/br/deflate),
            // DO NOT try to decode it as UTF-8. Just pass it through and
            // skip injection for this response - better no banner than a
            // corrupted Jellyfin shell.
            var contentEncoding = (context.Response.Headers["Content-Encoding"].ToString() ?? "").Trim();
            bool isCompressed = contentEncoding.Length > 0 &&
                                !contentEncoding.Equals("identity", StringComparison.OrdinalIgnoreCase);

            if (!isHtml || isCompressed || context.Response.StatusCode != StatusCodes.Status200OK || buffer.Length == 0)
            {
                // Flush untouched (handles 304, errors, empty SendFile,
                // gzipped responses we refused to touch, etc.)
                if (buffer.Length > 0)
                {
                    await buffer.CopyToAsync(originalBody);
                }
                if (isCompressed)
                {
                    _logger.LogWarning(
                        "JellyFusion injection SKIPPED on {Path}: response was {Encoding}-encoded (Accept-Encoding strip did not take effect upstream).",
                        path, contentEncoding);
                }
                return;
            }

            // Decode as UTF-8 (Jellyfin ships index.html as UTF-8).
            string html;
            buffer.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync();
            }

            // Already injected? Write back verbatim.
            if (html.Contains(InjectionMarker, StringComparison.OrdinalIgnoreCase))
            {
                var asIs = Encoding.UTF8.GetBytes(html);
                // Clear Content-Length so Kestrel recomputes / uses chunked;
                // otherwise a stale length from the upstream file serve can
                // truncate the body on the wire.
                context.Response.Headers.Remove("Content-Length");
                await originalBody.WriteAsync(asIs);
                return;
            }

            int bodyIdx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            string rewritten = bodyIdx >= 0
                ? html.Insert(bodyIdx, InjectionTag)
                : html + InjectionTag;

            byte[] bytes = Encoding.UTF8.GetBytes(rewritten);

            // Header hygiene:
            //  - Content-Length was set for the ORIGINAL file size and would
            //    cause a truncation / tail-garbage once we append the script.
            //  - Content-Encoding should be empty because we're writing raw
            //    UTF-8 (not gzipped). Jellyfin's compression middleware will
            //    recompress on the way out if enabled.
            context.Response.Headers.Remove("Content-Length");
            context.Response.Headers.Remove("Content-Encoding");

            await originalBody.WriteAsync(bytes);

            _logger.LogInformation(
                "JellyFusion bootstrap.js injected into {Path} ({Size} bytes)",
                path, bytes.Length);
        }
        catch (Exception ex)
        {
            // Never let the plugin break Jellyfin's main page. Restore, flush,
            // log and move on.
            _logger.LogWarning(ex, "ClientScriptInjectionMiddleware failed on {Path}", path);
            try
            {
                context.Response.Body = originalBody;
                context.Features.Set<IHttpResponseBodyFeature>(originalFeature!);
                buffer.Seek(0, SeekOrigin.Begin);
                if (buffer.Length > 0)
                {
                    await buffer.CopyToAsync(originalBody);
                }
            }
            catch
            {
                // Response already started. Nothing we can do here.
            }
        }
        finally
        {
            // Make absolutely sure the real body/feature are back in place.
            if (context.Features.Get<IHttpResponseBodyFeature>() is StreamResponseBodyFeature)
            {
                context.Features.Set<IHttpResponseBodyFeature>(originalFeature!);
            }
            if (context.Response.Body != originalBody)
            {
                context.Response.Body = originalBody;
            }
        }
    }
}
