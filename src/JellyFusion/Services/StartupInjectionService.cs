using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Services;

/// <summary>
/// v3.0.9: ports the on-disk index.html injection pattern from
/// <c>jellyfin-editors-choice-plugin</c> (lachlandcp/jellyfin-editors-choice-plugin)
/// to JellyFusion as the PRIMARY mechanism for landing the bootstrap.js
/// &lt;script&gt; tag on Jellyfin's web shell.
///
/// Why this exists (the v3.0.6→v3.0.8 banner saga):
///   The original injection mechanism is a runtime ASP.NET Core middleware
///   (<see cref="JellyFusion.Middleware.ClientScriptInjectionMiddleware"/>)
///   that intercepts GET /, /web, /web/, /web/index.html and rewrites the
///   HTML body to add a script tag. That approach has had repeated
///   failures across versions:
///     - v3.0.1: SendFileAsync bypassed the body swap (zero effect).
///     - v3.0.2: gzip race produced mojibake on every page.
///     - v3.0.3: Accept-Encoding strip introduced - works but fragile.
///     - v3.0.4: ClientScriptInjection gate flipping silently disabled it.
///     - v3.0.6+: reverse proxies / response compression races re-broke
///       it on some installs ("ya no se ve el banner").
///
///   Editor's Choice solves this by writing the script tag DIRECTLY into
///   <c>{WebPath}/index.html</c> on the server's filesystem at startup.
///   The browser then loads the script as a plain HTML asset reference -
///   no body interception, no gzip race, no SPA timing - just a static
///   file. We adopt the same pattern.
///
/// Design rules:
///   - Idempotent: matches the exact &lt;script&gt; tag we want to insert,
///     and ALSO any pre-existing JellyFusion injection (older id markers)
///     so re-runs don't duplicate.
///   - Never throws: any I/O error is logged and swallowed - the
///     plugin must not block Jellyfin startup.
///   - Compatible with the existing middleware: both check for the same
///     <c>id="jellyfusion-bootstrap"</c> marker before injecting, so a
///     server with both layers active won't stack tags. If the on-disk
///     injection succeeds, the middleware sees the marker and short-
///     circuits. If on-disk fails (read-only filesystem, no write
///     perms - e.g. Docker bind-mount with :ro), the middleware still
///     handles the request path as a fallback.
///   - Respects BaseUrl: if the admin set a reverse-proxy base path
///     (e.g. /jellyfin), the injected src uses that prefix.
/// </summary>
public class StartupInjectionService : IScheduledTask
{
    private readonly IApplicationPaths            _appPaths;
    private readonly IServerConfigurationManager  _serverConfig;
    private readonly ILogger<StartupInjectionService> _logger;

    // The marker id MUST match ClientScriptInjectionMiddleware.InjectionMarker
    // so the two layers cooperate (whichever runs first wins; the other
    // sees the marker and skips).
    private const string InjectionMarker = "jellyfusion-bootstrap";

    // Regex used to scrub any pre-existing JellyFusion script tag before
    // we re-insert. This catches tags written by older plugin versions
    // (different attribute order, missing defer, different src path).
    // We match by id="jellyfusion-bootstrap" because that's the stable
    // identifier we've used since v3.0.1.
    private static readonly Regex ExistingTagRegex = new(
        @"<script[^>]*\bid\s*=\s*[""']" + InjectionMarker + @"[""'][^>]*></script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public StartupInjectionService(
        IApplicationPaths appPaths,
        IServerConfigurationManager serverConfig,
        ILogger<StartupInjectionService> logger)
    {
        _appPaths     = appPaths;
        _serverConfig = serverConfig;
        _logger       = logger;
    }

    /// <inheritdoc />
    public string Name => "JellyFusion: Inject bootstrap into web shell";

    /// <inheritdoc />
    public string Key => "JellyFusionInjectBootstrap";

    /// <inheritdoc />
    public string Description =>
        "Writes the JellyFusion bootstrap.js <script> tag directly into " +
        "Jellyfin's web/index.html so the banner, rails, theme and badges " +
        "load on every client without relying on runtime middleware.";

    /// <inheritdoc />
    public string Category => "JellyFusion";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            string basePath = ResolveBasePath();
            string indexFile = Path.Combine(_appPaths.WebPath, "index.html");

            if (!File.Exists(indexFile))
            {
                _logger.LogWarning(
                    "JellyFusion injection skipped: {Path} not found. " +
                    "Server may be running with a non-standard webPath. " +
                    "The runtime middleware will still attempt injection.",
                    indexFile);
                return Task.CompletedTask;
            }

            string indexContents;
            try
            {
                indexContents = File.ReadAllText(indexFile);
            }
            catch (Exception readEx)
            {
                _logger.LogWarning(readEx,
                    "JellyFusion injection skipped: cannot read {Path}. " +
                    "Falling back to runtime middleware.", indexFile);
                return Task.CompletedTask;
            }

            // The exact tag we want to land. Using a stable id makes both
            // this service and the middleware idempotent against each
            // other and across plugin upgrades.
            string scriptTag = string.Format(
                "<script src=\"{0}/JellyFusion/bootstrap.js\" id=\"{1}\" defer></script>",
                basePath, InjectionMarker);

            // Already injected with the EXACT same tag? Nothing to do.
            if (indexContents.Contains(scriptTag, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "JellyFusion bootstrap tag already present in {Path}, skipping.",
                    indexFile);
                progress.Report(100);
                return Task.CompletedTask;
            }

            // An OLDER variant present (different attribute order / base
            // path / defer)? Strip it before we write the new one. This
            // keeps the file clean across plugin upgrades and prevents
            // the script from being loaded twice with different src
            // paths (which would race and double-fetch the API).
            string scrubbed = ExistingTagRegex.Replace(indexContents, "");

            int bodyClosing = scrubbed.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClosing == -1)
            {
                _logger.LogWarning(
                    "JellyFusion injection skipped: no </body> found in {Path}. " +
                    "Jellyfin's index.html shape may have changed; the runtime " +
                    "middleware will still try.", indexFile);
                return Task.CompletedTask;
            }

            string updated = scrubbed.Insert(bodyClosing, scriptTag);

            try
            {
                File.WriteAllText(indexFile, updated);
                _logger.LogInformation(
                    "JellyFusion bootstrap.js injected into {Path} (basePath='{BasePath}').",
                    indexFile, basePath);
            }
            catch (UnauthorizedAccessException unauthEx)
            {
                _logger.LogWarning(unauthEx,
                    "JellyFusion injection skipped: {Path} is not writable " +
                    "(read-only filesystem, Docker :ro mount, or insufficient " +
                    "permissions). The runtime middleware will handle injection " +
                    "instead.", indexFile);
            }
            catch (IOException ioEx)
            {
                _logger.LogWarning(ioEx,
                    "JellyFusion injection skipped: I/O error writing to {Path}. " +
                    "Falling back to runtime middleware.", indexFile);
            }

            progress.Report(100);
        }
        catch (Exception ex)
        {
            // Never throw from a startup task - that would block Jellyfin
            // boot. Log and continue.
            _logger.LogError(ex,
                "JellyFusion StartupInjectionService failed unexpectedly; " +
                "runtime middleware remains as fallback.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Re-run on every server start so we re-inject after Jellyfin
        // self-updates (Jellyfin's installer rewrites web/index.html and
        // wipes our tag), and after admins reinstall the plugin.
        yield return new TaskTriggerInfo
        {
            // v3.0.9 build fix: TaskTriggerInfoType (the enum) was
            // introduced in Jellyfin 10.11 and does NOT exist in our
            // target ABI 10.10.0.0. In 10.10, TaskTriggerInfo.Type is
            // a STRING and the valid values are exposed as static
            // string constants on TaskTriggerInfo itself
            // (TriggerStartup, TriggerInterval, TriggerDaily, ...).
            // Using the string constant keeps us compatible with 10.10
            // and forward-compatible with 10.11 (the enum's string
            // representation maps 1:1 to the same name).
            Type = TaskTriggerInfo.TriggerStartup
        };
    }

    /// <summary>
    /// Reads the server's configured BaseUrl (used by reverse-proxy
    /// installs like nginx subpath routing) and returns it as a prefix
    /// suitable for prepending to absolute paths. Empty string when
    /// no base path is configured (most installs).
    /// </summary>
    private string ResolveBasePath()
    {
        try
        {
            var network = _serverConfig.GetNetworkConfiguration();
            string raw = network?.BaseUrl ?? string.Empty;
            // Jellyfin stores BaseUrl with a leading slash (or empty).
            // Trim a trailing slash so we can append our own without
            // producing a double slash.
            return raw.TrimEnd('/');
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Could not resolve BaseUrl, defaulting to empty (no prefix).");
            return string.Empty;
        }
    }
}
