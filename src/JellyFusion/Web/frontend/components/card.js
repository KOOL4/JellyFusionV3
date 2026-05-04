/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/card.js   (v2)
 *
 *  A media tile with three layers:
 *
 *    ┌──────────────────────┐
 *    │                      │   ← .jf-card__img       (always visible)
 *    │      [poster]        │
 *    │                      │
 *    │ ─────────────────── │   ← .jf-card__title     (gradient strip)
 *    │   Título            │
 *    └──────────────────────┘
 *
 *  On hover/focus the inset overlay fades in and shows extra info
 *  (year · rating · overview · play button). The card itself lifts
 *  slightly so it feels tactile.
 *
 *  Format-aware:
 *    portrait  → 2:3 vertical poster (default)
 *    landscape → 16:9 wide thumbnail
 *    square    → 1:1
 *    small     → compact 2:3
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Card = function (opts) {
        opts = opts || {};
        var item     = opts.item     || {};
        var format   = (opts.format || 'portrait').toLowerCase();
        var onSelect = opts.onSelect || function () {};
        var onPlay   = opts.onPlay   || onSelect;          // default: same as click

        var card = document.createElement('article');
        card.className = 'jf-card jf-card--' + format;
        card.tabIndex  = 0;                                 // focusable for keyboard hover
        if (item.id) card.dataset.id = item.id;

        // ── Media ────────────────────────────────────────────
        var img = document.createElement('img');
        img.className = 'jf-card__img';
        img.alt       = item.name || '';
        img.loading   = 'lazy';
        img.src       = JF.Card.imageUrl(item, format);
        img.addEventListener('error', function () { img.style.opacity = '0'; });

        // ── Title strip (always visible, bottom of card) ─────
        var title = document.createElement('div');
        title.className   = 'jf-card__title';
        title.textContent = item.name || '';

        // ── Hidden overlay (revealed on hover/focus) ─────────
        var overlay = document.createElement('div');
        overlay.className = 'jf-card__overlay';

        var overlayTitle = document.createElement('div');
        overlayTitle.className   = 'jf-card__overlay-title';
        overlayTitle.textContent = item.name || '';

        var meta = document.createElement('div');
        meta.className = 'jf-card__overlay-meta';
        var metaParts = [];
        if (item.year)   metaParts.push(String(item.year));
        if (item.rating) metaParts.push('★ ' + (Math.round(item.rating * 10) / 10));
        meta.textContent = metaParts.join('  ·  ');

        var desc = document.createElement('p');
        desc.className   = 'jf-card__overlay-desc';
        desc.textContent = (item.overview || '').slice(0, 140);

        var actions = document.createElement('div');
        actions.className = 'jf-card__overlay-actions';

        var playBtn = document.createElement('button');
        playBtn.type        = 'button';
        playBtn.className   = 'jf-card__btn jf-card__btn--primary';
        playBtn.textContent = '▶';
        playBtn.setAttribute('aria-label', 'Reproducir');
        playBtn.addEventListener('click', function (ev) {
            ev.stopPropagation();
            onPlay(item);
        });

        var infoBtn = document.createElement('button');
        infoBtn.type        = 'button';
        infoBtn.className   = 'jf-card__btn jf-card__btn--ghost';
        infoBtn.textContent = 'i';
        infoBtn.setAttribute('aria-label', 'Más info');
        infoBtn.addEventListener('click', function (ev) {
            ev.stopPropagation();
            onSelect(item);
        });

        actions.appendChild(playBtn);
        actions.appendChild(infoBtn);

        overlay.appendChild(overlayTitle);
        if (metaParts.length) overlay.appendChild(meta);
        if (desc.textContent) overlay.appendChild(desc);
        overlay.appendChild(actions);

        card.appendChild(img);
        card.appendChild(title);
        card.appendChild(overlay);

        // Click anywhere outside the overlay buttons selects the item.
        card.addEventListener('click', function () { onSelect(item); });

        return card;
    };

    /**
     * Returns the URL to load into the card's <img>.
     *   - Pre-supplied item.imageUrl wins (lets the API serve
     *     CDN-hosted artwork like studio logos / mock TMDB stills).
     *   - Otherwise build the canonical Jellyfin Items API URL.
     */
    JF.Card.imageUrl = function (item, format) {
        if (item.imageUrl) return item.imageUrl;
        if (!item.id)      return '';
        var imgType = (format === 'landscape') ? 'Backdrop' : 'Primary';
        return '/Items/' + item.id + '/Images/' + imgType + '?fillHeight=270&quality=80';
    };
})();
