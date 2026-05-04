/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/hero.js
 *
 *  Banner / hero slider. Renders one slide at a time from the
 *  /jellyfusion/slider/items items array.
 *
 *  Foundation pass:
 *    - Single still image, no autoplay, no trailer overlay.
 *    - Title + overview + Play / More-info buttons.
 *    - paint(i) repaints by index — caller can wire dots, arrows
 *      or autoplay later by iterating the index from outside.
 *
 *  Returned element exposes a .paint(i) method on its dataset so
 *  parent code can drive the slide (e.g. JF.app could bind
 *  hero.paint to a setInterval for autoplay). Kept off the DOM
 *  surface to avoid leaking implementation into selectors.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Hero = function (opts) {
        opts = opts || {};
        var items      = opts.items      || [];
        var style      = opts.style      || 'default';
        var onPlay     = opts.onPlay     || function () {};
        var onMoreInfo = opts.onMoreInfo || function () {};

        var hero = document.createElement('section');
        hero.className = 'jf-hero jf-hero--' + style;

        if (!items.length) {
            hero.classList.add('jf-hero--empty');
            return hero;
        }

        var state = { idx: 0 };

        // ── Subtree ──────────────────────────────────────────
        var bg      = document.createElement('div');
        bg.className = 'jf-hero__bg';

        var content = document.createElement('div');
        content.className = 'jf-hero__content';

        var titleEl = document.createElement('h1');
        titleEl.className = 'jf-hero__title';

        var descEl  = document.createElement('p');
        descEl.className = 'jf-hero__desc';

        var ctaEl   = document.createElement('div');
        ctaEl.className = 'jf-hero__cta';

        var btnPlay = document.createElement('button');
        btnPlay.type        = 'button';
        btnPlay.className   = 'jf-hero__btn jf-hero__btn--primary';
        btnPlay.textContent = '▶  Reproducir';
        btnPlay.addEventListener('click', function () {
            var it = items[state.idx];
            if (it && it.id) onPlay(it.id);
        });

        var btnInfo = document.createElement('button');
        btnInfo.type        = 'button';
        btnInfo.className   = 'jf-hero__btn jf-hero__btn--ghost';
        btnInfo.textContent = 'ℹ  Más info';
        btnInfo.addEventListener('click', function () {
            var it = items[state.idx];
            if (it && it.id) onMoreInfo(it.id);
        });

        ctaEl.appendChild(btnPlay);
        ctaEl.appendChild(btnInfo);
        content.appendChild(titleEl);
        content.appendChild(descEl);
        content.appendChild(ctaEl);
        hero.appendChild(bg);
        hero.appendChild(content);

        // ── Painter ──────────────────────────────────────────
        function paint(i) {
            state.idx = ((i % items.length) + items.length) % items.length;
            var it = items[state.idx] || {};

            bg.style.backgroundImage = it.imageUrl
                ? "url('" + it.imageUrl + "?fillHeight=720&quality=85')"
                : '';
            titleEl.textContent = it.name || '';
            descEl.textContent  = (it.overview || '').slice(0, 240);
        }
        paint(0);

        // Expose so the parent layout / app can drive autoplay later.
        hero.paint = paint;
        return hero;
    };
})();
