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

    var VERSION = "3.0.4";
    var LOG_PREFIX = "[JellyFusion]";
    function log()  { try { console.log.apply(console, [LOG_PREFIX].concat([].slice.call(arguments))); } catch (e) {} }
    function warn() { try { console.warn.apply(console, [LOG_PREFIX].concat([].slice.call(arguments))); } catch (e) {} }

    log("bootstrap v" + VERSION + " loaded");

    // ---------------------------------------------------------
    //  1. Inject theme CSS as a <link> tag
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

    // ---------------------------------------------------------
    //  2. Base styles for banner + rails (inline so we don't
    //     need another round-trip).
    // ---------------------------------------------------------
    function injectBaseStyles() {
        if (document.getElementById("jellyfusion-base-css")) { return; }
        var css = [
            ".jf-banner{position:relative;width:100%;height:56vw;max-height:560px;min-height:320px;",
            "margin:0 0 24px;overflow:hidden;border-radius:12px;background:#101018;color:#fff;",
            "box-shadow:0 10px 30px rgba(0,0,0,.45);}",
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
            ".jf-banner .jf-dots{position:absolute;right:20px;bottom:20px;display:flex;gap:6px;}",
            ".jf-banner .jf-dots span{width:8px;height:8px;border-radius:50%;background:rgba(255,255,255,.35);cursor:pointer;transition:background .2s;}",
            ".jf-banner .jf-dots span.active{background:#fff;}",
            ".jf-rail{margin:24px 0;}",
            ".jf-rail h2{font-size:18px;font-weight:600;margin:0 0 12px;padding:0 2%;color:var(--mdb-color-primary,#fff);}",
            ".jf-rail .jf-row{display:flex;gap:10px;overflow-x:auto;padding:4px 2% 12px;scrollbar-width:thin;}",
            ".jf-rail .jf-row::-webkit-scrollbar{height:8px;}",
            ".jf-rail .jf-card{flex:0 0 auto;width:180px;position:relative;border-radius:8px;overflow:hidden;",
            "background:#1a1a22;cursor:pointer;transition:transform .18s ease;}",
            ".jf-rail .jf-card:hover{transform:scale(1.04);}",
            ".jf-rail .jf-card img{width:100%;aspect-ratio:2/3;object-fit:cover;display:block;}",
            ".jf-rail .jf-card .jf-rank{position:absolute;left:6px;top:4px;font-size:58px;font-weight:900;",
            "color:#fff;text-shadow:0 2px 8px rgba(0,0,0,.9),-2px 0 0 #000,2px 0 0 #000,0 -2px 0 #000,0 2px 0 #000;line-height:1;}",
            ".jf-rail .jf-card .jf-name{position:absolute;left:0;right:0;bottom:0;padding:6px 8px;background:linear-gradient(transparent,rgba(0,0,0,.9));",
            "font-size:12px;color:#fff;}",
            ".jf-debug{position:fixed;right:8px;bottom:8px;background:#5865F2;color:#fff;font:11px/1 system-ui,sans-serif;",
            "padding:4px 8px;border-radius:4px;z-index:99999;opacity:.85;pointer-events:none;}"
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
    // ---------------------------------------------------------
    function fetchJson(url) {
        return fetch(url, { credentials: "same-origin", cache: "no-cache" })
            .then(function (r) {
                if (!r.ok) throw new Error(url + " -> HTTP " + r.status);
                return r.json();
            });
    }

    // ---------------------------------------------------------
    //  5. Render banner
    // ---------------------------------------------------------
    var bannerState = { items: [], idx: 0, timer: null, el: null };

    function renderBanner(container) {
        fetchJson("/jellyfusion/slider/items").then(function (items) {
            if (!items || !items.length) {
                warn("slider returned no items");
                showDebugBadge("no banner items (check Banner tab)");
                return;
            }
            bannerState.items = items;
            bannerState.idx   = 0;

            var banner = document.createElement("div");
            banner.className = "jf-banner";
            banner.id        = "jf-banner";
            banner.innerHTML =
                "<div class='jf-bg'></div>" +
                "<div class='jf-grad'></div>" +
                "<div class='jf-content'>" +
                "  <img class='jf-logo' alt='' style='display:none'/>" +
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
            for (var i = 0; i < Math.min(items.length, 8); i++) {
                var d = document.createElement("span");
                (function (idx) { d.onclick = function () { showSlide(idx); }; })(i);
                dots.appendChild(d);
            }

            // CTA
            banner.querySelector(".jf-primary").onclick = function () {
                var it = bannerState.items[bannerState.idx];
                if (it) location.hash = "#/details?id=" + it.id;
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
        var dots = el.querySelectorAll(".jf-dots span");
        for (var k = 0; k < dots.length; k++) {
            dots[k].classList.toggle("active", k === bannerState.idx);
        }
    }

    function startAutoplay() {
        if (bannerState.timer) clearInterval(bannerState.timer);
        bannerState.timer = setInterval(function () {
            if (!document.getElementById("jf-banner")) {
                clearInterval(bannerState.timer);
                bannerState.timer = null;
                return;
            }
            showSlide(bannerState.idx + 1);
        }, 7000);
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
                row.className = "jf-rail";
                row.setAttribute("data-jf-rail", rail.id || rail.title || "rail");
                var cards = rail.items.map(function (it, idx) {
                    var rank = rail.showRank ? "<div class='jf-rank'>" + (idx + 1) + "</div>" : "";
                    var img  = "/Items/" + it.id + "/Images/Primary?fillHeight=270&quality=80";
                    return "<div class='jf-card' data-id='" + it.id + "'>" +
                               rank +
                               "<img src='" + img + "' alt=''/>" +
                               "<div class='jf-name'>" + (it.name || "") + "</div>" +
                           "</div>";
                }).join("");
                row.innerHTML =
                    "<h2>" + (rail.title || "") + "</h2>" +
                    "<div class='jf-row'>" + cards + "</div>";
                // Click → details
                row.addEventListener("click", function (ev) {
                    var card = ev.target.closest ? ev.target.closest(".jf-card") : null;
                    if (card && card.getAttribute("data-id")) {
                        location.hash = "#/details?id=" + card.getAttribute("data-id");
                    }
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
    var rendering = false;
    function tryRenderHome() {
        if (rendering) return;
        if (!isHomePage()) return;
        var container = findHomeContainer();
        if (!container) return;
        if (container.querySelector("#jf-banner")) return; // already rendered
        rendering = true;
        try {
            injectBaseStyles();
            renderBanner(container);
            renderRails(container);
            showDebugBadge("home OK");
            log("rendered home into", container);
        } catch (e) {
            warn("render failed:", e && e.message);
        } finally {
            rendering = false;
        }
    }

    // ---------------------------------------------------------
    //  8. Lifecycle
    // ---------------------------------------------------------
    function boot() {
        injectThemeCss();
        injectBaseStyles();
        showDebugBadge("loaded");
        tryRenderHome();

        // Re-render on SPA navigation.
        window.addEventListener("hashchange",  function () { setTimeout(tryRenderHome, 400); });
        window.addEventListener("popstate",    function () { setTimeout(tryRenderHome, 400); });
        // Jellyfin dispatches 'viewshow' when a page is displayed.
        document.addEventListener("viewshow",  function () { setTimeout(tryRenderHome, 400); });

        // Poll every 1.5s for 60s to survive late-initialising home pages on
        // first login when hashchange has already fired.
        var attempts = 0;
        var poll = setInterval(function () {
            attempts++;
            tryRenderHome();
            if (attempts > 40) { clearInterval(poll); }
        }, 1500);

        // v3.0.4: MutationObserver on <body> so that when Jellyfin replaces
        // the home view (e.g. after returning from /web/configurationpage),
        // we re-detect and re-render the banner. This is what was missing
        // after the user clicked Save on the admin UI: the poll had already
        // stopped after 60s, and the viewshow event fires BEFORE the home
        // container is inserted, so tryRenderHome saw no container and
        // bailed. The observer catches the container append as it happens.
        try {
            var mo = new MutationObserver(function () {
                if (isHomePage()) { tryRenderHome(); }
            });
            mo.observe(document.body, { childList: true, subtree: true });
        } catch (e) {
            warn("MutationObserver unavailable:", e && e.message);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();

