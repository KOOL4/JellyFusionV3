/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/app.js
 *
 *  Entry point of the new JellyFusion home shell. Boots a single
 *  full-viewport host that overlays Jellyfin's existing UI, fetches
 *  data from /jellyfusion/* endpoints, and delegates rendering to
 *  the modular components in /frontend/components.
 *
 *  Architecture decisions for this foundation pass:
 *    - Pure DOM, no virtual DOM, no React/Vue/Svelte runtime.
 *    - One global namespace `window.JF` (matches bootstrap.js style;
 *      avoids ES modules so we don't need import-map setup or CORS
 *      headers on the embedded-resource endpoint).
 *    - Each file is a self-contained IIFE that registers its export
 *      on `JF`. Order of <script> loads matters: components first,
 *      layout second, app.js last.
 *    - All API calls funnelled through JF.api so retries / caching /
 *      auth headers can be added in one place later.
 *    - JF.boot is exposed and idempotent. Callers can re-trigger from
 *      console for hot-reload during development.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';

    var JF = window.JF = window.JF || {};
    JF.VERSION = '0.1.0';

    // ── API ───────────────────────────────────────────────────
    JF.api = {
        fetchJson: function (url) {
            return fetch(url, { credentials: 'same-origin', cache: 'no-cache' })
                .then(function (r) {
                    if (!r.ok) throw new Error(url + ' -> HTTP ' + r.status);
                    return r.json();
                });
        }
    };

    // ── Mutable runtime state ─────────────────────────────────
    JF.state = {
        booted: false,
        host:   null,
        layout: null
    };

    // ── Public boot ───────────────────────────────────────────
    JF.boot = function () {
        if (JF.state.booted) return;
        JF.state.booted = true;

        injectStylesheet();
        var host = createHost();
        JF.state.host = host;

        var layout = JF.Layout({
            onNavigate: function (href) { navigate(href); }
        });
        JF.state.layout = layout;
        host.appendChild(layout.element);

        loadInitialData();
    };

    // ── Private helpers ───────────────────────────────────────
    function injectStylesheet() {
        if (document.getElementById('jf-app-styles')) return;
        var link  = document.createElement('link');
        link.id   = 'jf-app-styles';
        link.rel  = 'stylesheet';
        link.href = '/jellyfusion/frontend/styles.css?v=' + Date.now();
        document.head.appendChild(link);
    }

    function createHost() {
        // The .jf-app-active class on <html> is what hides Jellyfin's
        // existing markup (see the `:not(#jf-app)` rule in styles.css).
        document.documentElement.classList.add('jf-app-active');

        var existing = document.getElementById('jf-app');
        if (existing) existing.parentNode.removeChild(existing);

        var host = document.createElement('div');
        host.id        = 'jf-app';
        host.className = 'jf-app';
        document.body.appendChild(host);
        return host;
    }

    function loadInitialData() {
        Promise.all([
            JF.api.fetchJson('/jellyfusion/navigation').catch(emptyOk),
            JF.api.fetchJson('/jellyfusion/slider/items').catch(emptyOk),
            JF.api.fetchJson('/jellyfusion/home/rails').catch(emptyArr)
        ]).then(function (results) {
            renderInitial({
                nav:    results[0],
                slider: results[1],
                rails:  results[2]
            });
        });
    }
    function emptyOk()  { return { items: [] }; }
    function emptyArr() { return []; }

    function renderInitial(data) {
        var l = JF.state.layout;
        if (!l) return;

        // Top nav (the response shape can be raw array OR { items, ... })
        var navItems = Array.isArray(data.nav) ? data.nav
            : (data.nav && data.nav.items) || [];
        l.setNav(navItems);

        // Hero / banner — accepts both response shapes for compat with
        // the existing /slider/items endpoint.
        var sliderItems = Array.isArray(data.slider) ? data.slider
            : (data.slider && data.slider.items) || [];
        if (sliderItems.length) {
            l.heroSlot.appendChild(JF.Hero({
                items:      sliderItems,
                style:      (data.slider && data.slider.style) || 'default',
                onPlay:     function (id) { navigate('/details?id=' + id); },
                onMoreInfo: function (id) { navigate('/details?id=' + id); }
            }));
        }

        // Rows
        var rails = Array.isArray(data.rails) ? data.rails : [];
        rails.forEach(function (rail) {
            if (!rail || !rail.items || !rail.items.length) return;
            l.rowsSlot.appendChild(JF.Row({
                rail: rail,
                onSelect: function (item) {
                    if (item && item.clickUrl) {
                        navigate(stripHash(item.clickUrl));
                    } else if (item && item.id) {
                        navigate('/details?id=' + item.id);
                    }
                }
            }));
        });
    }

    function navigate(href) {
        if (!href) return;
        location.hash = stripHash(href);
    }

    function stripHash(href) {
        return href.indexOf('#') === 0 ? href.substring(1) : href;
    }

    // ── Auto-boot ─────────────────────────────────────────────
    // Disabled by default during the foundation pass. Boot manually
    // from the console with JF.boot() to preview, OR add a config
    // flag (Home.UseNewFrontend) and call JF.boot() conditionally
    // from bootstrap.js once the architecture is approved.
    //
    // if (document.readyState === 'loading') {
    //     document.addEventListener('DOMContentLoaded', JF.boot);
    // } else {
    //     JF.boot();
    // }
})();
