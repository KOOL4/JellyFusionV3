/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/card.js
 *
 *  A single media tile. Format-aware:
 *    portrait  → 2:3 vertical poster (default)
 *    landscape → 16:9 wide thumbnail
 *    square    → 1:1
 *    small     → compact 2:3
 *
 *  Image source resolution is centralised on JF.Card.imageUrl so
 *  that future formats (or per-style overrides) live in one place.
 *  An item with a pre-baked imageUrl (e.g. studio cards from the
 *  rails endpoint) wins over the auto-derived /Items/{id}/Images
 *  URL — so studio logos / category artwork work transparently.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Card = function (opts) {
        opts = opts || {};
        var item     = opts.item   || {};
        var format   = (opts.format || 'portrait').toLowerCase();
        var onSelect = opts.onSelect || function () {};

        var card = document.createElement('article');
        card.className = 'jf-card jf-card--' + format;
        if (item.id) card.dataset.id = item.id;

        // Media
        var img = document.createElement('img');
        img.className = 'jf-card__img';
        img.alt       = item.name || '';
        img.loading   = 'lazy';
        img.src       = JF.Card.imageUrl(item, format);
        img.addEventListener('error', function () {
            // Hide instead of showing a broken-image icon. Title still
            // renders so the user knows what the card represents.
            img.style.opacity = '0';
        });

        // Title (overlaid on bottom; CSS handles the gradient)
        var title = document.createElement('div');
        title.className   = 'jf-card__title';
        title.textContent = item.name || '';

        card.appendChild(img);
        card.appendChild(title);

        card.addEventListener('click', function () { onSelect(item); });

        return card;
    };

    /**
     * Returns the URL to load into the card's <img>.
     *   - Pre-supplied item.imageUrl wins (lets the API serve
     *     CDN-hosted artwork like studio logos).
     *   - Otherwise build the canonical Jellyfin Items API URL.
     */
    JF.Card.imageUrl = function (item, format) {
        if (item.imageUrl) return item.imageUrl;
        if (!item.id) return '';
        var imgType = (format === 'landscape') ? 'Backdrop' : 'Primary';
        return '/Items/' + item.id + '/Images/' + imgType + '?fillHeight=270&quality=80';
    };
})();
