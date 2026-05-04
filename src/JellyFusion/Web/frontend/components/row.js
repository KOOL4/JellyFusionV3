/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/row.js   (v2)
 *
 *  Horizontal scroll of Cards, format-aware.
 *
 *  Layout:
 *    <section class="jf-row jf-row--{format}">
 *      <header class="jf-row__header">
 *        <h2>{rail.title}</h2>
 *      </header>
 *      <div class="jf-row__scroller" role="list">
 *        [ jf-card ]× N
 *      </div>
 *    </section>
 *
 *  v2 changes:
 *    - gap: 16px (was 12) so the hover-lift on a card doesn't kiss
 *      the next card.
 *    - scroll-snap-type: x mandatory + scroll-padding so the
 *      scroller naturally aligns the first card to the left edge.
 *    - role="list" + each card role="listitem" for screen readers.
 *    - Lazy onSelect proxying so all card events go through the
 *      row's handler — keeps the API surface tidy.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Row = function (opts) {
        opts = opts || {};
        var rail     = opts.rail     || {};
        var onSelect = opts.onSelect || function () {};
        var onPlay   = opts.onPlay   || onSelect;
        var format   = (rail.format || 'portrait').toLowerCase();

        var row = document.createElement('section');
        row.className = 'jf-row jf-row--' + format;
        if (rail.id) row.dataset.railId = rail.id;

        // ── Header ───────────────────────────────────────────
        var header = document.createElement('header');
        header.className = 'jf-row__header';
        var title = document.createElement('h2');
        title.className   = 'jf-row__title';
        title.textContent = rail.title || '';
        header.appendChild(title);

        // ── Scroller ─────────────────────────────────────────
        var scroller = document.createElement('div');
        scroller.className = 'jf-row__scroller';
        scroller.setAttribute('role', 'list');

        (rail.items || []).forEach(function (item) {
            var card = JF.Card({
                item:     item,
                format:   format,
                onSelect: onSelect,
                onPlay:   onPlay
            });
            card.setAttribute('role', 'listitem');
            scroller.appendChild(card);
        });

        row.appendChild(header);
        row.appendChild(scroller);
        return row;
    };
})();
