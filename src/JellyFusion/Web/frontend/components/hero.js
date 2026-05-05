/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/hero.js   (v2)
 *
 *  Banner / hero slider with one slide visible at a time.
 *
 *  v2 changes:
 *    - Pagination dots at the bottom-right (one per item, clickable).
 *    - Prev / Next arrows on the sides (only visible on hover).
 *    - Exposes hero.paint(i) and hero.next() / hero.prev() so the
 *      caller can wire autoplay via setInterval if desired.
 *    - Background painted via inline style (no extra layer needed).
 *
 *  Usage from app.js:
 *    var hero = JF.Hero({ items: [...], onPlay, onMoreInfo });
 *    layout.heroSlot.appendChild(hero);
 *    setInterval(hero.next, 7000);   // autoplay
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

        // ── Background + content ─────────────────────────────
        var bg = document.createElement('div');
        bg.className = 'jf-hero__bg';

        var content = document.createElement('div');
        content.className = 'jf-hero__content';

        var titleEl = document.createElement('h1');
        titleEl.className = 'jf-hero__title';

        var metaEl  = document.createElement('div');
        metaEl.className = 'jf-hero__meta';

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
        content.appendChild(metaEl);
        content.appendChild(descEl);
        content.appendChild(ctaEl);

        // ── Arrows ───────────────────────────────────────────
        var prevBtn = document.createElement('button');
        prevBtn.type      = 'button';
        prevBtn.className = 'jf-hero__arrow jf-hero__arrow--prev';
        prevBtn.setAttribute('aria-label', 'Anterior');
        prevBtn.textContent = '‹';
        prevBtn.addEventListener('click', function () { paint(state.idx - 1); });

        var nextBtn = document.createElement('button');
        nextBtn.type      = 'button';
        nextBtn.className = 'jf-hero__arrow jf-hero__arrow--next';
        nextBtn.setAttribute('aria-label', 'Siguiente');
        nextBtn.textContent = '›';
        nextBtn.addEventListener('click', function () { paint(state.idx + 1); });

        // ── Dots ─────────────────────────────────────────────
        var dots = document.createElement('div');
        dots.className = 'jf-hero__dots';
        items.forEach(function (_, i) {
            var d = document.createElement('button');
            d.type      = 'button';
            d.className = 'jf-hero__dot';
            d.setAttribute('aria-label', 'Diapositiva ' + (i + 1));
            d.addEventListener('click', function () { paint(i); });
            dots.appendChild(d);
        });

        hero.appendChild(bg);
        hero.appendChild(content);
        hero.appendChild(prevBtn);
        hero.appendChild(nextBtn);
        hero.appendChild(dots);

        // ── Painter ──────────────────────────────────────────
        function paint(i) {
            state.idx = ((i % items.length) + items.length) % items.length;
            var it = items[state.idx] || {};

            bg.style.backgroundImage = it.imageUrl
                ? "url('" + it.imageUrl + (it.imageUrl.indexOf('?') < 0 ? '?fillHeight=720&quality=85' : '') + "')"
                : '';
            titleEl.textContent = it.name || '';

            var metaParts = [];
            if (it.year)   metaParts.push(String(it.year));
            if (it.rating) metaParts.push('★ ' + (Math.round(it.rating * 10) / 10));
            metaEl.textContent = metaParts.join('  ·  ');

            descEl.textContent = (it.overview || '').slice(0, 240);

            // Refresh active dot.
            var allDots = dots.querySelectorAll('.jf-hero__dot');
            for (var k = 0; k < allDots.length; k++) {
                allDots[k].classList.toggle('jf-hero__dot--active', k === state.idx);
            }
        }
        paint(0);

        // Public surface so app.js can drive autoplay.
        hero.paint = paint;
        hero.next  = function () { paint(state.idx + 1); };
        hero.prev  = function () { paint(state.idx - 1); };

        return hero;
    };
})();
