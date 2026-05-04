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

        // v3.0.29: edge-aware hover. A card scaling to 1.42x at the
        // very left or right of its scroller would clip against the
        // viewport edge, so we tag those cards with data-edge and let
        // CSS shift their transform-origin inward.
        // We use IntersectionObserver instead of polling scroll
        // events — fires only when a card crosses the threshold,
        // which is far cheaper for long rails. The threshold values
        // (1.0 and 0.4) flag cards whose right or left half just
        // peeked out of the scroller's clip rect.
        attachEdgeObserver(scroller);

        return row;
    };

    /**
     * Tags the LEFT-most and RIGHT-most fully-visible cards with
     * data-edge so :hover lands inside the scroller. Re-evaluates
     * automatically as the user scrolls.
     */
    function attachEdgeObserver(scroller) {
        if (!('IntersectionObserver' in window)) return;     // graceful no-op
        var EDGE_PAD = 24;                                    // px from edges

        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                var card = entry.target;
                var cr   = entry.boundingClientRect;
                var rr   = entry.rootBounds;
                if (!rr) { card.removeAttribute('data-edge'); return; }

                if (cr.left   <  rr.left   + EDGE_PAD)      card.setAttribute('data-edge', 'left');
                else if (cr.right >  rr.right - EDGE_PAD)   card.setAttribute('data-edge', 'right');
                else                                         card.removeAttribute('data-edge');
            });
        }, {
            root: scroller,
            // Threshold list lets us catch the moment a card is
            // partially clipped (just below 1.0 ratio) — that's when
            // we want the edge tag flipped.
            threshold: [0, 0.5, 0.9, 1]
        });

        Array.prototype.forEach.call(
            scroller.querySelectorAll('.jf-card'),
            function (c) { io.observe(c); }
        );
    }
})();
