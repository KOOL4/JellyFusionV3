/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/layout.js
 *
 *  Builds the page shell:
 *    [ topbar ]
 *    [ heroSlot   ]   ← components/hero.js mounts here
 *    [ rowsSlot   ]   ← components/row.js × N mount here
 *
 *  Returns a small object with:
 *    .element       — the root <div> ready to be appended.
 *    .topbar        — <header>, exposed for theming hooks.
 *    .navContainer  — <nav>, where setNav() injects pills.
 *    .heroSlot      — <section> that will host the Hero.
 *    .rowsSlot      — <main>, where rows are appended in order.
 *    .setNav(items) — replaces the nav pill content.
 *
 *  Why a factory and not a class:
 *    Plain functions composed by `app.js` are easier to follow
 *    than a class hierarchy when there's only one root layout.
 *    If we ever need a second layout (mobile, kids profile …) we
 *    just add a sibling factory next to this one.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Layout = function (opts) {
        opts = opts || {};
        var onNavigate = opts.onNavigate || function () {};

        // ── Root ──────────────────────────────────────────────
        var root = document.createElement('div');
        root.className = 'jf-layout';

        // ── Top bar (brand + nav pills) ───────────────────────
        var topbar = document.createElement('header');
        topbar.className = 'jf-topbar';

        var brand = document.createElement('div');
        brand.className   = 'jf-brand';
        brand.textContent = 'JellyFusion';

        var navContainer = document.createElement('nav');
        navContainer.className = 'jf-topnav';

        topbar.appendChild(brand);
        topbar.appendChild(navContainer);

        // ── Hero slot ─────────────────────────────────────────
        var heroSlot = document.createElement('section');
        heroSlot.className = 'jf-hero-slot';

        // ── Rows slot ─────────────────────────────────────────
        var rowsSlot = document.createElement('main');
        rowsSlot.className = 'jf-rows-slot';

        root.appendChild(topbar);
        root.appendChild(heroSlot);
        root.appendChild(rowsSlot);

        return {
            element:       root,
            topbar:        topbar,
            navContainer:  navContainer,
            heroSlot:      heroSlot,
            rowsSlot:      rowsSlot,

            /**
             * Replaces the contents of the top-nav with the given items.
             * Each item is { id, label, url }.
             */
            setNav: function (items) {
                navContainer.innerHTML = '';
                (items || []).forEach(function (it) {
                    var pill = document.createElement('a');
                    pill.className = 'jf-navlink';
                    pill.href      = '#';
                    pill.textContent = it.label || it.id || '';
                    if (it.id) pill.dataset.navId = it.id;
                    pill.addEventListener('click', function (ev) {
                        ev.preventDefault();
                        var url = (it.url || '').replace(/^#/, '');
                        if (url) onNavigate(url);
                    });
                    navContainer.appendChild(pill);
                });
            },

            /**
             * Removes every row mounted so far. Useful for refreshes
             * after the user changes the active library / filter.
             */
            clearRows: function () {
                rowsSlot.innerHTML = '';
            }
        };
    };
})();
