/* -------------------------------------------------------------
 *  JellyFusion bootstrap.js
 *
 *  Injected by ClientScriptInjectionMiddleware into Jellyfin's
 *  /web/index.html. Runs in the browser on every page navigation
 *  inside Jellyfin's SPA, detects when the user lands on the Home
 *  screen and paints:
 *      1. The theme CSS (from /jellyfusion/theme/css)
 *      2. A Netflix-style banner with backdrop + logo + CTA
 *      3. One or more home rails (Top 10, etc.)
 *      4. A banner showing the plugin is ALIVE (console log +
 *         small corner badge in debug mode).
 *
 *  Design goals:
 *  - NEVER throw; a JS error here would break Jellyfin's UI.
 *  - Work on every Jellyfin client (Web, Android TV WebView, iOS).
 *  - Re-render when the user navigates between tabs (Jellyfin is a
 *    SPA so the home content is replaced without a page reload).
 * ------------------------------------------------------------- */
(function () {
    "use strict";

    if (window.__JELLYFUSION_LOADED__) { return; }
    window.__JELLYFUSION_LOADED__ = true;

    var VERSION = "3.0.20";
    var LOG_PREFIX = "[JellyFusion]";
    function log()  { try { console.log.apply(console, [LOG_PREFIX].concat([].slice.call(arguments))); } catch (e) {} }
    function warn() { try { console.warn.apply(console, [LOG_PREFIX].concat([].slice.call(arguments))); } catch (e) {} }

    log("bootstrap v" + VERSION + " loaded");

    // ---------------------------------------------------------
    //  1. Inject theme CSS as a <link> tag.
    //
    //  v3.0.7: the link is now RE-INJECTED on every SPA navigation
    //  (via refreshThemeCss()) instead of being cached forever. Users
    //  reported that after clicking Save in the plugin config the
    //  home page never reflected the new theme - because the <link>
    //  was already in <head> and the browser happily served its
    //  max-age=300 cached response. By bumping the ?v=... querystring
    //  on every navigation we force a re-fetch whenever Jellyfin
    //  dispatches viewshow, which is exactly when the user returns
    //  from the configuration page.
    // ---------------------------------------------------------
    function injectThemeCss() {
        if (document.getElementById("jellyfusion-theme-css")) { return; }
        var link = document.createElement("link");
        link.id   = "jellyfusion-theme-css";
        link.rel  = "stylesheet";
        link.type = "text/css";
        link.href = "/jellyfusion/theme/css?v=" + Date.now();
        document.head.appendChild(link);
        log("theme CSS injected");
    }

    function refreshThemeCss() {
        // Pull the existing link (if any) and bump the querystring so the
        // browser re-requests the CSS. This is what makes "Save in config
        // -> come back to home" actually repaint.
        var link = document.getElementById("jellyfusion-theme-css");
        if (!link) { injectThemeCss(); return; }
        var base = "/jellyfusion/theme/css";
        link.href = base + "?v=" + Date.now();
    }

    // ---------------------------------------------------------
    //  2. Base styles for banner + rails (inline so we don't
    //     need another round-trip).
    // ---------------------------------------------------------
    function injectBaseStyles() {
        if (document.getElementById("jellyfusion-base-css")) { return; }
        var css = [
            // ===== Default (netflix) banner =====
            ".jf-banner{position:relative;width:100%;margin:0 0 24px;overflow:hidden;",
            "border-radius:12px;background:#101018;color:#fff;",
            "box-shadow:0 10px 30px rgba(0,0,0,.45);}",
            // v3.0.17: BannerHeight presets — each style can override
            // its own combination via .jf-style-X.jf-height-Y, but the
            // base .jf-height-Y rules below give every style a sensible
            // baseline when the user changes the slider in config.
            ".jf-height-small{height:38vw;max-height:320px;min-height:220px;}",
            ".jf-height-medium{height:48vw;max-height:460px;min-height:280px;}",
            ".jf-height-large{height:56vw;max-height:560px;min-height:320px;}",
            ".jf-height-fullscreen{height:88vh;max-height:none;min-height:480px;}",
            // Disney/apple presets are taller by spec (matching the
            // platform-style screenshots the user shared); keep them
            // overriding only when both classes are present so changing
            // BannerHeight still affects the netflix style as expected.
            ".jf-style-disney.jf-height-small{height:48vw;max-height:380px;}",
            ".jf-style-disney.jf-height-medium{height:55vw;max-height:520px;}",
            ".jf-style-disney.jf-height-large{height:62vw;max-height:640px;}",
            ".jf-style-disney.jf-height-fullscreen{height:88vh;max-height:none;}",
            ".jf-style-apple.jf-height-small{height:48vw;max-height:380px;}",
            ".jf-style-apple.jf-height-medium{height:55vw;max-height:540px;}",
            ".jf-style-apple.jf-height-large{height:62vw;max-height:680px;}",
            ".jf-style-apple.jf-height-fullscreen{height:90vh;max-height:none;}",
            ".jf-banner .jf-bg{position:absolute;inset:0;background-size:cover;background-position:center;",
            "filter:brightness(.55);transform:scale(1.02);transition:opacity .6s ease;}",
            ".jf-banner .jf-grad{position:absolute;inset:0;background:linear-gradient(90deg,rgba(0,0,0,.85) 0%,rgba(0,0,0,.55) 35%,rgba(0,0,0,0) 70%);}",
            ".jf-banner .jf-content{position:absolute;left:6%;bottom:10%;max-width:46%;}",
            ".jf-banner .jf-logo{max-width:320px;max-height:130px;margin-bottom:14px;filter:drop-shadow(0 2px 6px rgba(0,0,0,.8));}",
            ".jf-banner .jf-title{font-size:clamp(22px,3.2vw,42px);font-weight:700;margin:0 0 10px;line-height:1.1;}",
            ".jf-banner .jf-meta{font-size:13px;opacity:.85;margin:0 0 10px;}",
            ".jf-banner .jf-desc{font-size:15px;line-height:1.45;max-height:4.3em;overflow:hidden;margin:0 0 18px;opacity:.92;}",
            ".jf-banner .jf-cta{display:inline-flex;gap:10px;}",
            ".jf-banner .jf-cta button{appearance:none;border:0;padding:10px 20px;border-radius:6px;cursor:pointer;",
            "font-weight:600;font-size:14px;display:inline-flex;align-items:center;gap:6px;}",
            ".jf-banner .jf-cta .jf-primary{background:#fff;color:#111;}",
            ".jf-banner .jf-cta .jf-ghost{background:rgba(109,109,110,.7);color:#fff;}",
            // Pagination dots — bumped contrast and z-index so they're
            // always visible on busy backdrops (user feedback v3.0.13:
            // "que se vean los puntitos para poder cambiar la imagen").
            ".jf-banner .jf-dots{position:absolute;right:20px;bottom:20px;display:flex;gap:8px;",
            "z-index:6;background:rgba(0,0,0,.45);backdrop-filter:blur(8px);",
            "padding:8px 12px;border-radius:18px;}",
            ".jf-banner .jf-dots span{width:9px;height:9px;border-radius:50%;background:rgba(255,255,255,.45);",
            "cursor:pointer;transition:background .2s,width .2s;border:1px solid rgba(255,255,255,.2);}",
            ".jf-banner .jf-dots span:hover{background:rgba(255,255,255,.75);}",
            ".jf-banner .jf-dots span.active{background:#fff;width:24px;border-radius:5px;}",
            ".jf-rail{margin:24px 0;}",
            ".jf-rail h2{font-size:18px;font-weight:600;margin:0 0 12px;padding:0 2%;color:var(--mdb-color-primary,#fff);}",
            ".jf-rail .jf-row{display:flex;gap:10px;overflow-x:auto;padding:4px 2% 12px;scrollbar-width:thin;}",
            ".jf-rail .jf-row::-webkit-scrollbar{height:8px;}",
            ".jf-rail .jf-card{flex:0 0 auto;width:180px;position:relative;border-radius:8px;overflow:hidden;",
            "background:#1a1a22;cursor:pointer;transition:transform .18s ease;}",
            ".jf-rail .jf-card:hover{transform:scale(1.04);}",
            ".jf-rail .jf-card img{width:100%;aspect-ratio:2/3;object-fit:cover;display:block;}",
            // v3.0.16: Studio and Category cards use a wider 16:9-ish
            // ratio and CONTAIN the logo (not crop it) so the user's
            // brand artwork stays intact.
            ".jf-rail .jf-card-studio,.jf-rail .jf-card-category{width:280px;aspect-ratio:16/9;",
            "display:flex;align-items:center;justify-content:center;padding:18px;",
            "background:linear-gradient(135deg,#1f2937 0%,#0b1220 100%);}",
            ".jf-rail .jf-card-studio img,.jf-rail .jf-card-category img{",
            "width:100%;height:100%;aspect-ratio:auto;object-fit:contain;}",
            ".jf-rail .jf-card-studio .jf-name,.jf-rail .jf-card-category .jf-name{display:none;}",
            ".jf-rail .jf-card .jf-rank{position:absolute;left:6px;top:4px;font-size:58px;font-weight:900;",
            "color:#fff;text-shadow:0 2px 8px rgba(0,0,0,.9),-2px 0 0 #000,2px 0 0 #000,0 -2px 0 #000,0 2px 0 #000;line-height:1;}",
            ".jf-rail .jf-card .jf-name{position:absolute;left:0;right:0;bottom:0;padding:6px 8px;background:linear-gradient(transparent,rgba(0,0,0,.9));",
            "font-size:12px;color:#fff;}",
            ".jf-debug{position:fixed;right:8px;bottom:8px;background:#5865F2;color:#fff;font:11px/1 system-ui,sans-serif;",
            "padding:4px 8px;border-radius:4px;z-index:99999;opacity:.85;pointer-events:none;}",

            // ===== Disney+ STYLE =====
            // Large rounded "tab card" look: pill nav at top center,
            // generous border radius, content tag in top-left, platform
            // logo bottom-right, and pagination dots on white pill.
            // v3.0.17: removed hardcoded height/max-height — handled
            // now by the .jf-height-X presets above so the BannerHeight
            // config slider in admin actually does something.
            ".jf-style-disney{border-radius:24px;",
            "background:linear-gradient(180deg,#0b1220 0%,#020617 100%);",
            "box-shadow:0 20px 60px rgba(0,0,0,.7);}",
            ".jf-style-disney .jf-bg{border-radius:24px;filter:brightness(.7);}",
            ".jf-style-disney .jf-grad{border-radius:24px;background:",
            "linear-gradient(90deg,rgba(0,0,0,.92) 0%,rgba(0,0,0,.6) 40%,rgba(0,0,0,.1) 75%,rgba(0,0,0,.5) 100%);}",
            ".jf-style-disney .jf-content{left:5%;bottom:14%;max-width:50%;}",
            ".jf-style-disney .jf-tag{display:inline-block!important;background:rgba(255,255,255,.18);",
            "backdrop-filter:blur(10px);color:#fff;font-size:12px;padding:4px 12px;border-radius:14px;",
            "margin-bottom:14px;letter-spacing:.5px;}",
            ".jf-style-disney .jf-title{font-size:clamp(28px,4vw,56px);font-weight:800;letter-spacing:-.02em;",
            "text-transform:uppercase;line-height:.95;text-shadow:0 4px 18px rgba(0,0,0,.7);}",
            ".jf-style-disney .jf-meta{font-weight:600;}",
            ".jf-style-disney .jf-cta .jf-primary{background:#0063e5;color:#fff;padding:12px 28px;",
            "border-radius:4px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;",
            "box-shadow:0 4px 12px rgba(0,99,229,.4);}",
            ".jf-style-disney .jf-cta .jf-primary:hover{background:#0079ff;}",
            ".jf-style-disney .jf-cta .jf-ghost{background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.4);",
            "padding:11px 22px;border-radius:4px;}",
            ".jf-style-disney .jf-dots{background:rgba(255,255,255,.92);left:50%;right:auto;bottom:18px;",
            "transform:translateX(-50%);padding:6px 14px;border-radius:14px;}",
            ".jf-style-disney .jf-dots span{background:#9ca3af;width:6px;height:6px;}",
            ".jf-style-disney .jf-dots span.active{background:#0063e5;width:18px;border-radius:3px;}",
            // Disney decorative tabs at top
            ".jf-disney-tabs{position:absolute;top:14px;left:50%;transform:translateX(-50%);",
            "display:flex;gap:10px;z-index:5;background:rgba(0,0,0,.55);backdrop-filter:blur(20px);",
            "padding:6px 8px;border-radius:30px;border:1px solid rgba(255,255,255,.08);}",
            ".jf-disney-tabs .jf-tab{padding:8px 18px;border-radius:20px;font-size:13px;font-weight:600;",
            "color:rgba(255,255,255,.7);cursor:pointer;transition:all .2s ease;letter-spacing:.02em;}",
            ".jf-disney-tabs .jf-tab:hover{color:#fff;background:rgba(255,255,255,.08);}",
            ".jf-disney-tabs .jf-tab.active{color:#fff;background:rgba(255,255,255,.15);}",

            // ===== Apple TV+ STYLE =====
            // Fullscreen-feel banner. Top/sides feather softly into the page
            // background; bottom carries a strong dark gradient so the title
            // and CTA stay readable and the page transitions into the rails
            // below without a hard edge - matches the Naked Gun screenshot
            // the user shared.
            // v3.0.17: same as disney — height handled by .jf-height-X.
            ".jf-style-apple{border-radius:0;background:transparent;",
            "box-shadow:none;margin:0 0 32px;padding-top:0;overflow:visible;}",
            ".jf-style-apple .jf-bg{filter:brightness(.92);",
            // Soft feather on top + sides; bottom stays solid so the dark
            // gradient below blends straight into the page.
            "-webkit-mask-image:linear-gradient(180deg,transparent 0%,#000 14%,#000 100%),",
            "linear-gradient(90deg,transparent 0%,#000 6%,#000 94%,transparent 100%);",
            "-webkit-mask-composite:source-in;mask-composite:intersect;",
            "mask-image:linear-gradient(180deg,transparent 0%,#000 14%,#000 100%),",
            "linear-gradient(90deg,transparent 0%,#000 6%,#000 94%,transparent 100%);}",
            // Bottom-heavy gradient (the user explicitly asked for "que pase
            // para abajo el degradado"). Goes from clear at top to nearly
            // opaque at the bottom so the title pops and the rails below
            // flow seamlessly.
            ".jf-style-apple .jf-grad{background:linear-gradient(180deg,",
            "rgba(0,0,0,0) 0%,rgba(0,0,0,0) 30%,rgba(0,0,0,.35) 55%,",
            "rgba(0,0,0,.7) 78%,rgba(0,0,0,.95) 100%);}",
            ".jf-style-apple .jf-content{left:6%;bottom:14%;max-width:55%;}",
            ".jf-style-apple .jf-title{font-family:'New York',Georgia,serif;font-style:italic;",
            "font-size:clamp(40px,5.5vw,90px);font-weight:700;letter-spacing:-.03em;line-height:1;",
            "margin-bottom:14px;text-shadow:0 6px 24px rgba(0,0,0,.85);color:#ff2e8a;}",
            ".jf-style-apple .jf-meta{display:inline-block;background:rgba(40,40,40,.75);backdrop-filter:blur(20px);",
            "padding:5px 12px;border-radius:6px;font-size:13px;color:#fff;margin-bottom:10px;}",
            ".jf-style-apple .jf-desc{color:#e5e5e5;font-size:16px;max-width:560px;}",
            ".jf-style-apple .jf-cta .jf-primary{background:rgba(255,255,255,.95);color:#000;padding:10px 32px;",
            "border-radius:30px;border:0;font-weight:700;font-size:15px;}",
            ".jf-style-apple .jf-cta .jf-primary:hover{background:#fff;}",
            ".jf-style-apple .jf-cta .jf-ghost{background:rgba(40,40,40,.7);border:1px solid rgba(255,255,255,.18);",
            "padding:10px 18px;border-radius:30px;backdrop-filter:blur(20px);color:#fff;}",
            // Dots — VISIBLE (user feedback: "que se vean los puntitos").
            // Pinned to the BANNER bottom (not the page) so they never get
            // hidden by overlapping rails.
            ".jf-style-apple .jf-dots{left:50%;right:auto;transform:translateX(-50%);bottom:24px;",
            "z-index:6;background:rgba(0,0,0,.45);backdrop-filter:blur(12px);",
            "padding:8px 14px;border-radius:20px;}",
            ".jf-style-apple .jf-dots span{background:rgba(255,255,255,.45);width:7px;height:7px;}",
            ".jf-style-apple .jf-dots span.active{background:#fff;width:22px;border-radius:4px;}",
            // Apple platform pill (top-left)
            ".jf-apple-pill{position:absolute;top:18px;left:24px;z-index:5;display:inline-flex;",
            "align-items:center;gap:8px;background:rgba(20,20,22,.85);backdrop-filter:blur(20px);",
            "border:1px solid rgba(255,255,255,.08);padding:6px 14px 6px 6px;border-radius:24px;",
            "font-size:13px;color:#fff;letter-spacing:.02em;box-shadow:0 4px 12px rgba(0,0,0,.4);}",
            ".jf-apple-icon{display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;",
            "background:linear-gradient(135deg,#1c1c1e 0%,#3a3a3c 100%);color:#fff;border-radius:50%;",
            "font-size:9px;font-weight:800;letter-spacing:0;}",
            ".jf-apple-name{font-weight:600;}"
        ].join("");
        var s = document.createElement("style");
        s.id = "jellyfusion-base-css";
        s.textContent = css;
        document.head.appendChild(s);
    }

    // ---------------------------------------------------------
    //  Small debug badge so the user can SEE at a glance that
    //  the plugin is running (we drop it as soon as the banner
    //  renders successfully).
    // ---------------------------------------------------------
    function showDebugBadge(text) {
        var existing = document.getElementById("jf-debug-badge");
        if (existing) existing.remove();
        var b = document.createElement("div");
        b.id = "jf-debug-badge";
        b.className = "jf-debug";
        b.textContent = "JellyFusion v" + VERSION + (text ? " · " + text : "");
        document.body.appendChild(b);
    }

    // ---------------------------------------------------------
    //  3. Detect the Home page container. Jellyfin's SPA renders
    //     each view inside <div class="view"> with data-type or
    //     a homeTab wrapper. We grab whatever is currently visible.
    // ---------------------------------------------------------
    function findHomeContainer() {
        // The home tab content lives inside .homeTabContent (or the section
        // with homeSectionsContainer). We pick the FIRST visible one.
        var candidates = [
            document.querySelector(".homeTabContent:not(.hide) .sections"),
            document.querySelector(".homeTabContent:not(.hide)"),
            document.querySelector(".homeSectionsContainer"),
            document.querySelector(".page:not(.hide) .sections"),
            document.querySelector(".page:not(.hide)")
        ];
        for (var i = 0; i < candidates.length; i++) {
            if (candidates[i]) { return candidates[i]; }
        }
        return null;
    }

    function isHomePage() {
        var h = location.hash || "";
        return h.indexOf("home") !== -1 || h === "" || h === "#" || h === "#/" ||
               h.indexOf("!/home") !== -1 || h === "#/home.html";
    }

    // ---------------------------------------------------------
    //  4. Fetch helpers
    //
    //  v3.0.9: prefer Jellyfin's ApiClient when available. The raw
    //  fetch() with credentials:same-origin in v3.0.8 worked on most
    //  installs but NOT when Jellyfin sits behind a reverse proxy that
    //  rewrites cookies, or when the user authenticated via the mobile
    //  apps (which carry the X-Emby-Authorization header instead of a
    //  cookie). Editor's Choice plugin uses the same pattern (their
    //  client.js calls ApiClient.fetch({ url: ApiClient.getUrl(...) }))
    //  and that's exactly why their banner works on more setups than
    //  ours did. We fall back to raw fetch when ApiClient is missing
    //  (very early SPA boot, or when /web/index.html is loaded outside
    //  the app shell).
    // ---------------------------------------------------------
    function getApiClient() {
        try {
            // Jellyfin exposes ApiClient on window via the connection manager.
            if (window.ApiClient && typeof window.ApiClient.fetch === "function") {
                return window.ApiClient;
            }
        } catch (e) {}
        return null;
    }

    function fetchJson(url) {
        var ac = getApiClient();
        if (ac) {
            try {
                // ApiClient.getUrl prepends the server base path AND attaches
                // the auth headers Jellyfin expects. ApiClient.fetch returns a
                // promise that already calls .json() when type is GET and the
                // dataType is "json", but we keep it explicit to be portable
                // across web/Android/iOS clients.
                return ac.fetch({
                    url:      ac.getUrl(url),
                    type:     "GET",
                    dataType: "json"
                }).then(function (data) {
                    // Some ApiClient builds resolve to the parsed JSON
                    // directly; others resolve to a Response. Handle both.
                    if (data && typeof data.json === "function") {
                        return data.json();
                    }
                    return data;
                });
            } catch (e) {
                warn("ApiClient.fetch failed, falling back to raw fetch:", e && e.message);
            }
        }
        return fetch(url, { credentials: "same-origin", cache: "no-cache" })
            .then(function (r) {
                if (!r.ok) throw new Error(url + " -> HTTP " + r.status);
                return r.json();
            });
    }

    // ---------------------------------------------------------
    //  4b. Item playback helper (v3.0.16)
    //
    //  Both v3.0.14 (require playbackManager) and v3.0.15 (navigate
    //  to #/video?id=X) failed in the wild - the SPA either ignored
    //  the route or couldn't resolve the AMD module from our injected
    //  context. v3.0.16 uses the most reliable trick: navigate to the
    //  details page (which Jellyfin always handles correctly - the
    //  "Más info" button proves this), then POLL for the rendered
    //  primary play button and SYNTHETICALLY CLICK it. We're using
    //  Jellyfin's own play path, so whatever code starts playback for
    //  a click on the details page also starts it for our click.
    //
    //  Selectors used (covers Jellyfin web 10.10 → 10.11.8):
    //    .detailPagePrimaryContainer .btnPlay
    //    .mainDetailButtons .btnPlay
    //    button[data-action="play"]
    //    button.btnPlay
    //  Poll fires every 200ms for up to 5 seconds; if the button
    //  never appears we leave the user on the details page (they can
    //  click play manually - same as Más info, no broken state).
    // ---------------------------------------------------------
    function playItem(itemId) {
        if (!itemId) return;
        try {
            // Step 1: navigate to details. This works on every version.
            location.hash = "/details?id=" + encodeURIComponent(itemId);

            // Step 2: poll for Jellyfin's primary play button and click it.
            var attempts = 0;
            var clicked  = false;
            var poll = setInterval(function () {
                attempts++;
                if (clicked || attempts > 25) {  // 5s max
                    clearInterval(poll);
                    return;
                }
                var btn =
                    document.querySelector('.detailPagePrimaryContainer .btnPlay') ||
                    document.querySelector('.mainDetailButtons .btnPlay') ||
                    document.querySelector('button[data-action="play"]') ||
                    document.querySelector('button.btnPlay') ||
                    // Fallback: the "Reproducir" labelled button on the
                    // hero panel of the details page in newer 10.11.
                    document.querySelector('.detailButtons button[is="emby-button"]');

                // Make sure the button is actually visible (Jellyfin
                // sometimes pre-renders hidden buttons before content loads).
                if (btn && btn.offsetParent !== null) {
                    clicked = true;
                    clearInterval(poll);
                    log("playItem -> clicking native Play for", itemId);
                    try { btn.click(); } catch (e) {
                        warn("native Play click failed:", e && e.message);
                    }
                }
            }, 200);
        } catch (e) {
            warn("playItem failed, leaving user on details:", e && e.message);
            location.hash = "/details?id=" + encodeURIComponent(itemId);
        }
    }

    // ---------------------------------------------------------
    //  4c. Top-nav replacement (v3.0.14)
    //
    //  Hides Jellyfin's stock "Inicio / Favoritos" tabs in the
    //  header and injects our own row resolved from
    //  /jellyfusion/navigation. Each item is { id, label, url }.
    //  Re-runs on viewshow because Jellyfin re-renders the header
    //  when the SPA navigates between sections.
    // ---------------------------------------------------------
    function renderTopNav() {
        // Find Jellyfin's header tab strip. The DOM hook varies slightly
        // between Jellyfin versions; we try several selectors.
        var header = document.querySelector(".skinHeader");
        if (!header) return;

        var existingHost = document.getElementById("jf-topnav");
        if (existingHost) return; // already injected this load

        fetchJson("/jellyfusion/navigation").then(function (response) {
            // v3.0.15: response is now { hideStockMyMedia, items } -
            // normalise the older raw-array shape (v3.0.14) for safety.
            var items, hideStockMyMedia = false;
            if (Array.isArray(response)) {
                items = response;
            } else {
                items            = (response && response.items) || [];
                hideStockMyMedia = !!(response && response.hideStockMyMedia);
            }
            if (!items.length) return;

            // Build the host. Inject CSS once.
            if (!document.getElementById("jellyfusion-topnav-css")) {
                var style = document.createElement("style");
                style.id = "jellyfusion-topnav-css";
                style.textContent = [
                    ".jf-topnav-host{display:flex;align-items:center;justify-content:center;",
                    "padding:6px 16px;background:rgba(0,0,0,.55);backdrop-filter:blur(20px);",
                    "border-bottom:1px solid rgba(255,255,255,.06);}",
                    ".jf-topnav-pills{display:inline-flex;gap:6px;background:rgba(0,0,0,.55);",
                    "border:1px solid rgba(255,255,255,.08);padding:6px 8px;border-radius:30px;}",
                    ".jf-topnav-pill{padding:8px 18px;border-radius:20px;font-size:13px;font-weight:600;",
                    "color:rgba(255,255,255,.7);cursor:pointer;transition:all .15s ease;",
                    "user-select:none;letter-spacing:.02em;background:transparent;border:0;}",
                    ".jf-topnav-pill:hover{color:#fff;background:rgba(255,255,255,.08);}",
                    ".jf-topnav-pill.active{color:#fff;background:rgba(255,255,255,.18);}",
                    // Hide Jellyfin's stock home/favorites buttons in the
                    // header tabs strip - they're now duplicated by our nav.
                    ".jf-topnav-active .headerTabs{display:none!important;}",
                    // v3.0.15: HideStockMyMedia toggle - hides the stock
                    // Jellyfin "Mis medios" library cards row that sits
                    // right under the banner. Multiple selectors because
                    // Jellyfin re-renders this section with different
                    // class names depending on view mode.
                    ".jf-hide-mymedia .verticalSection.section.MyMedia,",
                    ".jf-hide-mymedia .verticalSection[data-section-type=\"mymedialatest\"],",
                    ".jf-hide-mymedia .verticalSection[data-section-type=\"mymedia\"],",
                    ".jf-hide-mymedia .verticalSection.section[is=\"emby-scroller\"]:has(.libraryButton),",
                    ".jf-hide-mymedia .homeLibraryView,",
                    ".jf-hide-mymedia .libraryButtons{display:none!important;}"
                ].join("");
                document.head.appendChild(style);
            }
            document.body.classList.add("jf-topnav-active");
            // v3.0.15: toggle the hide-mymedia class according to admin pref.
            document.body.classList.toggle("jf-hide-mymedia", hideStockMyMedia);

            // Build pills.
            var host = document.createElement("div");
            host.id = "jf-topnav";
            host.className = "jf-topnav-host";
            var pills = document.createElement("div");
            pills.className = "jf-topnav-pills";

            var currentHash = (location.hash || "").toLowerCase();
            items.forEach(function (it) {
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "jf-topnav-pill";
                btn.textContent = it.label || it.id;
                btn.setAttribute("data-jf-nav-id", it.id);
                if (it.url && currentHash.indexOf(it.url.toLowerCase().replace(/^#/, "")) === 0) {
                    btn.classList.add("active");
                }
                btn.onclick = function () {
                    if (it.url) {
                        // Both forms work: full hash or hash-relative.
                        if (it.url.indexOf("#") === 0) location.hash = it.url.substring(1);
                        else location.hash = it.url.replace(/^#?/, "");
                        // Mark active on click for instant feedback.
                        var siblings = pills.querySelectorAll(".jf-topnav-pill");
                        for (var i = 0; i < siblings.length; i++) siblings[i].classList.remove("active");
                        btn.classList.add("active");
                    }
                };
                pills.appendChild(btn);
            });
            host.appendChild(pills);

            // Insert ABOVE the header tabs row but inside the .skinHeader so
            // it stays sticky with the rest of the chrome.
            header.insertBefore(host, header.firstChild);
            log("top nav rendered with", items.length, "items");
        }).catch(function (e) {
            warn("renderTopNav failed:", e && e.message);
        });
    }

    function refreshTopNavActive() {
        var pills = document.querySelectorAll("#jf-topnav .jf-topnav-pill");
        if (!pills.length) return;
        var currentHash = (location.hash || "").toLowerCase();
        pills.forEach(function (p) {
            // The pill's url is held on its onclick closure; we re-read
            // by running through navigation again would be wasteful.
            // Simpler: check if currentHash starts with /home for "home" id.
            var id = p.getAttribute("data-jf-nav-id");
            var isActive =
                (id === "home"      && (currentHash === "" || currentHash === "#" || currentHash.indexOf("home") !== -1)) ||
                (id === "favorites" && currentHash.indexOf("favorit") !== -1) ||
                (id === "movies"    && currentHash.indexOf("parentid") !== -1);
            p.classList.toggle("active", !!isActive);
        });
    }

    // ---------------------------------------------------------
    //  5. Render banner
    // ---------------------------------------------------------
    var bannerState = { items: [], idx: 0, timer: null, el: null, style: "netflix" };

    function renderBanner(container) {
        fetchJson("/jellyfusion/slider/items").then(function (response) {
            // v3.0.13: response is now { style, autoplay, items: [...] }
            // v3.0.17: also reads `height` for the BannerHeight preset.
            // - normalise the older raw-array shape (v3.0.12-) for safety.
            var items, style, autoplay = true, autoplayInterval = 7, height = "Large";
            if (Array.isArray(response)) {
                items = response;
                style = "netflix";
            } else {
                items            = (response && response.items) || [];
                style            = (response && response.style)    || "netflix";
                autoplay         = (response && typeof response.autoplay === "boolean") ? response.autoplay : true;
                autoplayInterval = (response && response.autoplayInterval) || 7;
                height           = (response && response.height) || "Large";
            }
            if (!items || !items.length) {
                warn("slider returned no items");
                showDebugBadge("no banner items (check Banner tab)");
                return;
            }
            bannerState.items            = items;
            bannerState.idx              = 0;
            bannerState.style            = style;
            bannerState.autoplay         = autoplay;
            bannerState.autoplayInterval = autoplayInterval;

            var banner = document.createElement("div");
            // v3.0.17: stack style class + height class so the CSS combo
            // selector .jf-style-X.jf-height-Y can scope size overrides
            // per platform style.
            banner.className =
                "jf-banner jf-style-" + style +
                " jf-height-" + (height || "Large").toLowerCase();
            banner.setAttribute("data-jf-style",  style);
            banner.setAttribute("data-jf-height", (height || "Large").toLowerCase());
            banner.id        = "jf-banner";

            // v3.0.15: REMOVED the decorative Disney "Para ti/Películas/
            // Series/Estudios" tabs that floated inside the banner —
            // they were duplicated by the real top nav we now inject
            // (and the user marked them in red as "remove"). Apple's
            // small "Apple TV+" branding pill stays since it's just a
            // logo, not a duplicated nav.
            var topRow = "";
            if (style === "apple") {
                topRow =
                    "<div class='jf-apple-pill'>" +
                    "  <span class='jf-apple-icon'>tv+</span>" +
                    "  <span class='jf-apple-name'>Apple TV+</span>" +
                    "</div>";
            }

            banner.innerHTML =
                topRow +
                "<div class='jf-bg'></div>" +
                "<div class='jf-grad'></div>" +
                "<div class='jf-content'>" +
                "  <img class='jf-logo' alt='' style='display:none'/>" +
                "  <span class='jf-tag' style='display:none'></span>" +
                "  <h1 class='jf-title'></h1>" +
                "  <p class='jf-meta'></p>" +
                "  <p class='jf-desc'></p>" +
                "  <div class='jf-cta'>" +
                "    <button class='jf-primary' type='button'>▶ Reproducir</button>" +
                "    <button class='jf-ghost'   type='button'>ℹ Más info</button>" +
                "  </div>" +
                "</div>" +
                "<div class='jf-dots'></div>";

            container.insertBefore(banner, container.firstChild);
            bannerState.el = banner;

            // Dots
            var dots = banner.querySelector(".jf-dots");
            // v3.0.17: render ONE dot per banner item (the user explicitly
            // asked: "si dices 10 elementos, 10 puntitos"). Removed the
            // hard cap of 8 so the dots match Slider.MaxItems exactly.
            for (var i = 0; i < items.length; i++) {
                var d = document.createElement("span");
                (function (idx) { d.onclick = function () { showSlide(idx); }; })(i);
                dots.appendChild(d);
            }

            // CTA
            // v3.0.14: Play button now ACTUALLY plays the item via
            // Jellyfin's playbackManager, with progressive fallbacks so
            // it works on Web, Android TV WebView and iOS clients:
            //   1. require(['playbackManager'], pm => pm.play({ ids, serverId }))
            //      - the canonical web-app API. Works on the standard
            //        Jellyfin web client.
            //   2. window.playbackManager?.play(...) - some forks expose it.
            //   3. Fallback: navigate to #/video?serverId=...&id=... which
            //      the SPA treats as "open player and start playing".
            //   4. Last resort: details page (the v3.0.13 behaviour).
            // More-info button stays as plain navigation to details.
            banner.querySelector(".jf-primary").onclick = function () {
                var it = bannerState.items[bannerState.idx];
                if (!it) return;
                playItem(it.id);
            };
            banner.querySelector(".jf-ghost").onclick = function () {
                var it = bannerState.items[bannerState.idx];
                if (it) location.hash = "#/details?id=" + it.id;
            };

            showSlide(0);
            startAutoplay();
            showDebugBadge("banner " + items.length + " items");
        }).catch(function (e) {
            warn("banner render failed:", e && e.message);
            showDebugBadge("banner ERR: " + (e && e.message || "fetch failed"));
        });
    }

    function showSlide(i) {
        var items = bannerState.items;
        if (!items.length || !bannerState.el) return;
        bannerState.idx = ((i % items.length) + items.length) % items.length;
        var it = items[bannerState.idx];
        var el = bannerState.el;

        // v3.0.20: tear down any previous trailer iframe before painting
        // the new slide image - prevents leaking iframes when the user
        // scrubs through dots fast.
        cancelTrailerOverlay();

        el.querySelector(".jf-bg").style.backgroundImage =
            "url('" + it.imageUrl + "?fillHeight=720&quality=85')";
        var logo = el.querySelector(".jf-logo");
        logo.onerror = function () { logo.style.display = "none"; };
        logo.onload  = function () { logo.style.display = "";     };
        logo.src     = it.logoUrl + "?quality=85";
        el.querySelector(".jf-title").textContent = it.name || "";
        var meta = [];
        if (it.year)   meta.push(it.year);
        if (it.rating) meta.push("★ " + Math.round(it.rating * 10) / 10);
        el.querySelector(".jf-meta").textContent = meta.join("  ·  ");
        el.querySelector(".jf-desc").textContent = (it.overview || "").slice(0, 260);

        // v3.0.13: content tag (used by Disney+ style mainly).
        // Show "Nueva serie" / "Nueva película" for Series/Movie types,
        // else hide.
        var tag = el.querySelector(".jf-tag");
        if (tag) {
            var tagText = "";
            if (it.type === "Series")     tagText = "Nueva serie";
            else if (it.type === "Movie") tagText = "Nueva película";
            else if (it.type === "BoxSet") tagText = "Colección";
            if (tagText) {
                tag.textContent  = tagText;
                tag.style.display = "";
            } else {
                tag.style.display = "none";
            }
        }

        var dots = el.querySelectorAll(".jf-dots span");
        for (var k = 0; k < dots.length; k++) {
            dots[k].classList.toggle("active", k === bannerState.idx);
        }

        // v3.0.20: trailer schedule. The user spec was:
        //   "que al momento de 2 segundos muestre la imagen del banner
        //    y en el segundo 3 empieze el trailer"
        // Implementation:
        //   1. Slide paints the still IMMEDIATELY (above).
        //   2. After ~2.4s of the still being on screen, fetch the
        //      trailer key from /jellyfusion/slider/trailer/{id}.
        //   3. At t≈3s overlay a YouTube IFrame (autoplay+muted+
        //      no-controls+loop+playlist=key for indefinite loop) on
        //      top of .jf-bg with an opacity fade-in.
        //   4. cancelTrailerOverlay() runs on next slide / autoplay tick
        //      to tear down the iframe so we never leak DOM.
        scheduleTrailerOverlay(it.id);
    }

    // ---------------------------------------------------------
    //  v3.0.20 — banner trailer overlay
    // ---------------------------------------------------------
    var trailerState = { timer: null, fetchAbort: null, iframe: null };

    function cancelTrailerOverlay() {
        if (trailerState.timer) { clearTimeout(trailerState.timer); trailerState.timer = null; }
        if (trailerState.iframe) {
            try { trailerState.iframe.parentNode && trailerState.iframe.parentNode.removeChild(trailerState.iframe); }
            catch (e) {}
            trailerState.iframe = null;
        }
    }

    function scheduleTrailerOverlay(itemId) {
        if (!itemId || !bannerState.el) return;
        cancelTrailerOverlay();
        // Fetch the trailer URL after 2s of the still being visible.
        // If autoplay is OFF or interval too short to show the trailer,
        // skip - the overlay would tear down before the iframe loads.
        var interval = (bannerState.autoplayInterval || 7);
        if (bannerState.autoplay !== false && interval < 4) return;

        trailerState.timer = setTimeout(function () {
            fetchJson("/jellyfusion/slider/trailer/" + encodeURIComponent(itemId))
                .then(function (resp) {
                    if (!resp || !resp.embedUrl) return; // no trailer for this item
                    if (!bannerState.el) return;          // banner gone
                    var bg = bannerState.el.querySelector(".jf-bg");
                    if (!bg) return;

                    var iframe = document.createElement("iframe");
                    iframe.className = "jf-trailer-iframe";
                    iframe.src       = resp.embedUrl;
                    iframe.allow     = "autoplay; encrypted-media; picture-in-picture";
                    iframe.frameBorder = "0";
                    iframe.style.cssText =
                        "position:absolute;inset:0;width:100%;height:100%;" +
                        "border:0;opacity:0;transition:opacity .6s ease;pointer-events:none;";
                    bannerState.el.appendChild(iframe);
                    trailerState.iframe = iframe;
                    // Fade in next tick so the transition runs.
                    setTimeout(function () { iframe.style.opacity = "1"; }, 30);
                })
                .catch(function (e) { /* no trailer, no problem */ });
        }, 2400);  // 2.4s still → ~3s when iframe is visible
    }

    function startAutoplay() {
        if (bannerState.timer) clearInterval(bannerState.timer);
        if (bannerState.autoplay === false) return;  // respect admin toggle
        var ms = (bannerState.autoplayInterval || 7) * 1000;
        bannerState.timer = setInterval(function () {
            if (!document.getElementById("jf-banner")) {
                clearInterval(bannerState.timer);
                bannerState.timer = null;
                return;
            }
            showSlide(bannerState.idx + 1);
        }, ms);
    }

    // ---------------------------------------------------------
    //  6. Render home rails
    // ---------------------------------------------------------
    function renderRails(container) {
        fetchJson("/jellyfusion/home/rails").then(function (rails) {
            if (!rails || !rails.length) {
                warn("no rails returned (check Home tab data source)");
                // Append a note to the existing badge so the user knows
                // banner rendered but rails didn't - this was the top
                // "other tabs don't appear" feedback from v3.0.3.
                var existing = document.getElementById("jf-debug-badge");
                if (existing) existing.textContent += " | rails 0";
                return;
            }
            rails.forEach(function (rail) {
                if (!rail || !rail.items || !rail.items.length) return;
                var row = document.createElement("div");
                row.className = "jf-rail jf-rail-" + (rail.id || "default");
                row.setAttribute("data-jf-rail", rail.id || rail.title || "rail");

                var cards = rail.items.map(function (it, idx) {
                    var rank = rail.showRank ? "<div class='jf-rank'>" + (idx + 1) + "</div>" : "";

                    // v3.0.16: Studio cards use the configured logoUrl
                    // (the user uploaded brand artwork like a Netflix
                    // logo) instead of /Items/<id>/Images/Primary - the
                    // id is a studio NAME, not a Jellyfin GUID, so the
                    // /Items/... path was 404'ing and showing a broken
                    // image placeholder. Studio cards also carry their
                    // own clickUrl (search by studio name) so the click
                    // handler routes to "all items by this studio".
                    if (it.kind === "Studio") {
                        var sUrl = it.logoUrl || "";
                        var sBg  = it.gradient
                            ? "background:" + it.gradient + ";"
                            : "";
                        var inv  = it.invert ? "filter:invert(1) brightness(2);" : "";
                        return "<div class='jf-card jf-card-studio' " +
                                    "data-id='" + (it.id || "") + "' " +
                                    "data-click-url='" + (it.clickUrl || "") + "' " +
                                    "style='" + sBg + "'>" +
                                    (sUrl ? "<img src='" + sUrl + "' alt='" + (it.name || "") + "' " +
                                            "style='" + inv + "'/>" : "") +
                                    "<div class='jf-name'>" + (it.name || "") + "</div>" +
                                "</div>";
                    }
                    if (it.kind === "Category") {
                        // Category seed card from "Explora por género" -
                        // navigate to a search by genre name.
                        return "<div class='jf-card jf-card-category' " +
                                    "data-id='" + (it.id || "") + "' " +
                                    "data-click-url='#/search.html?query=" + encodeURIComponent(it.name || "") + "'>" +
                                    (it.imageUrl ? "<img src='" + it.imageUrl + "?fillHeight=270&quality=80' alt=''/>" : "") +
                                    "<div class='jf-name'>" + (it.name || "") + "</div>" +
                                "</div>";
                    }

                    var img = "/Items/" + it.id + "/Images/Primary?fillHeight=270&quality=80";
                    return "<div class='jf-card' data-id='" + it.id + "'>" +
                               rank +
                               "<img src='" + img + "' alt=''/>" +
                               "<div class='jf-name'>" + (it.name || "") + "</div>" +
                           "</div>";
                }).join("");
                row.innerHTML =
                    "<h2>" + (rail.title || "") + "</h2>" +
                    "<div class='jf-row'>" + cards + "</div>";

                // v3.0.16: click routing is now per-card-type:
                //   - Studio / Category cards use their data-click-url
                //   - Regular media items go to details page
                row.addEventListener("click", function (ev) {
                    var card = ev.target.closest ? ev.target.closest(".jf-card") : null;
                    if (!card) return;
                    var clickUrl = card.getAttribute("data-click-url");
                    if (clickUrl) {
                        location.hash = clickUrl.indexOf("#") === 0
                            ? clickUrl.substring(1)
                            : clickUrl.replace(/^#?/, "");
                        return;
                    }
                    var id = card.getAttribute("data-id");
                    if (id) location.hash = "/details?id=" + id;
                });
                container.appendChild(row);
            });
            log("rendered", rails.length, "rails");
            var existing = document.getElementById("jf-debug-badge");
            if (existing) existing.textContent += " | " + rails.length + " rails";
        }).catch(function (e) {
            warn("rails fetch failed:", e && e.message);
            var existing = document.getElementById("jf-debug-badge");
            if (existing) existing.textContent += " | rails ERR";
        });
    }

    // ---------------------------------------------------------
    //  7. Orchestrator — runs on home page detection
    // ---------------------------------------------------------
    function tryRenderHome() {
        if (!isHomePage()) return;
        var container = findHomeContainer();
        if (!container) return;
        // Already painted in this container.
        if (container.querySelector("#jf-banner")) return;
        // Already in-flight for this container. This flag is set SYNCHRONOUSLY
        // before the async renderBanner/renderRails fetches resolve, so the
        // next poll tick (1.5s later) won't re-fire duplicate fetches. The
        // flag lives on the container itself, so when Jellyfin's SPA replaces
        // the home container (e.g. after Save → back to home), the new
        // container has no flag and will render cleanly.
        if (container.getAttribute("data-jf-rendering") === "1") return;
        container.setAttribute("data-jf-rendering", "1");
        // Safety release: if the fetches fail and the banner never inserts,
        // clear the marker after 10s so the poll can retry. If the banner
        // renders successfully, the top-of-function short-circuit on
        // #jf-banner makes this a no-op.
        setTimeout(function () {
            if (!container.querySelector("#jf-banner")) {
                container.removeAttribute("data-jf-rendering");
            }
        }, 10000);
        try {
            injectBaseStyles();
            renderBanner(container);
            renderRails(container);
            showDebugBadge("home OK");
            log("rendered home into", container);
        } catch (e) {
            warn("render failed:", e && e.message);
            // On sync error, release the marker so a later poll can retry.
            container.removeAttribute("data-jf-rendering");
        }
    }

    // ---------------------------------------------------------
    //  8. Lifecycle
    //
    //  v3.0.5 CRITICAL FIX: removed MutationObserver that was added
    //  in v3.0.4. The observer watched document.body with subtree:true
    //  and called tryRenderHome() on every mutation. But tryRenderHome
    //  itself appends DOM nodes (banner, rails, debug badge), and each
    //  append fires the observer again → runaway loop + hundreds of
    //  concurrent fetches → browser freezes, page stays black.
    //
    //  Replaced with a restartable poll: when Jellyfin dispatches
    //  viewshow / hashchange / popstate (all of which fire when the
    //  user returns from the configurationpage), we restart the same
    //  1.5s × 40-attempt poll that originally only ran at boot. This
    //  covers the "banner disappears after Save" case without touching
    //  the DOM globally.
    // ---------------------------------------------------------
    var pollTimer    = null;
    var pollAttempts = 0;
    function startPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
        pollAttempts = 0;
        pollTimer = setInterval(function () {
            pollAttempts++;
            tryRenderHome();
            if (pollAttempts > 40) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }, 1500);
    }

    function onSpaNavigate() {
        // v3.0.7: refresh theme CSS so a Save in the config page actually
        // repaints the skin when the user comes back to /web/#/home.html.
        // Cheap - just bumps the <link>'s href query string.
        refreshThemeCss();
        // v3.0.14: re-attempt the top nav injection - Jellyfin may have
        // rebuilt the header on navigation. Also keep the active pill in
        // sync with the current route.
        renderTopNav();
        refreshTopNavActive();
        // Reset the "already rendered" guard so a fresh home view
        // (after Jellyfin rebuilt the DOM) gets painted again.
        setTimeout(tryRenderHome, 400);
        // Also restart the poll in case the new home container is
        // inserted slightly later than our single setTimeout.
        startPolling();
    }

    function boot() {
        injectThemeCss();
        injectBaseStyles();
        showDebugBadge("loaded");
        renderTopNav();      // v3.0.14
        tryRenderHome();

        // Re-render on SPA navigation. Each handler restarts the poll
        // so a slow home-container insert is still caught.
        window.addEventListener("hashchange", onSpaNavigate);
        window.addEventListener("popstate",   onSpaNavigate);
        // Jellyfin dispatches 'viewshow' when a page is displayed.
        document.addEventListener("viewshow", onSpaNavigate);

        // Initial poll for first login / cold start.
        startPolling();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();

