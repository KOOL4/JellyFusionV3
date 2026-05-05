/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/themes.js
 *
 *  Dynamic theme loader. A theme is a folder under /themes/<id>/
 *  containing:
 *    - theme.css   (mandatory) — redefines the --jf-* variables
 *    - config.js   (optional)  — registers JS hooks via JF.themes.register
 *
 *  Usage:
 *    JF.themes.use('netflix');    // applies Netflix colours + behaviour
 *    JF.themes.use('disney');     // swap in Disney+
 *    JF.themes.use(null);         // unload theme, fall back to defaults
 *
 *  Design rules honoured:
 *    1. NO global conditionals like body.netflix .jf-card. The selectors
 *       in core styles.css read var(--jf-*); the active theme.css just
 *       redefines those vars. Removing the theme link cleanly reverts.
 *    2. Themes are isolated. Each lives in its own folder, never imports
 *       another, and registers its config under its own id namespace.
 *    3. Core UI doesn't know which theme is active. JS hooks are opt-in:
 *       components call into JF.themes.invoke(hook, args) and ignore
 *       the return when no theme registered the hook.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.themes = {
        active:   null,
        registry: {},

        /** Theme folders register themselves here from their config.js */
        register: function (id, config) {
            this.registry[id] = config || {};
        },

        /**
         * Loads <id>/theme.css and <id>/config.js. Removes any previously
         * loaded theme. Pass null/falsy to unload and fall back to the
         * default theme baked into styles.css.
         */
        use: function (id) {
            var self = this;

            // Unload previous (CSS link + onRemove hook).
            if (self.active && self.active !== id) {
                var prev = self.registry[self.active];
                if (prev && typeof prev.onRemove === 'function') {
                    try { prev.onRemove(); } catch (e) {}
                }
            }
            removeNode('jf-theme-css');
            removeNode('jf-theme-config');

            if (!id) {
                self.active = null;
                return Promise.resolve(null);
            }

            // Load <id>/theme.css.
            var cssLink = document.createElement('link');
            cssLink.id   = 'jf-theme-css';
            cssLink.rel  = 'stylesheet';
            cssLink.href = '/jellyfusion/frontend/themes/' + id + '/theme.css?v=' + Date.now();
            document.head.appendChild(cssLink);

            // Load <id>/config.js, then call its onApply if defined.
            return new Promise(function (resolve) {
                var script  = document.createElement('script');
                script.id   = 'jf-theme-config';
                script.src  = '/jellyfusion/frontend/themes/' + id + '/config.js?v=' + Date.now();
                script.onload = function () {
                    self.active = id;
                    var cfg = self.registry[id];
                    if (cfg && typeof cfg.onApply === 'function') {
                        try { cfg.onApply(); } catch (e) {}
                    }
                    resolve(cfg || null);
                };
                script.onerror = function () {
                    // CSS-only theme is valid (config.js is optional).
                    self.active = id;
                    resolve(null);
                };
                document.head.appendChild(script);
            });
        },

        /**
         * Components call this to give the active theme an opportunity
         * to override behaviour (e.g. custom overlay HTML, extra
         * badges). Returns whatever the hook returned, or undefined.
         */
        invoke: function (hookName, args) {
            if (!this.active) return undefined;
            var cfg = this.registry[this.active];
            if (!cfg || typeof cfg[hookName] !== 'function') return undefined;
            try { return cfg[hookName].apply(cfg, args || []); }
            catch (e) { return undefined; }
        }
    };

    function removeNode(id) {
        var n = document.getElementById(id);
        if (n && n.parentNode) n.parentNode.removeChild(n);
    }
})();
