/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/components/row.js
 *
 *  Horizontal-scroll row of Cards.
 *
 *  Builds:
 *    <section class="jf-row jf-row--{format}">
 *      <header class="jf-row__header">
 *        <h2>{rail.title}</h2>
 *      </header>
 *      <div class="jf-row__scroller">
 *        [ jf-card ]× N
 *      </div>
 *    </section>
 *
 *  In this foundation pass the row is just a flex strip that
 *  scrolls overflow-x. Polish (chevron arrows, snap-to-card,
 *  Top-10 big numbers, hover-expand cards) lands in a later
 *  iteration once the architecture is approved.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    JF.Row = function (opts) {
        opts = opts || {};
        var rail     = opts.rail || {};
        var onSelect = opts.onSelect || function () {};
        var format   = (rail.format || 'portrait').toLowerCase();

        var row = document.createElement('section');
        row.className = 'jf-row jf-row--' + format;
        if (rail.id) row.dataset.railId = rail.id;

        // Header
        var header = document.createElement('header');
        header.className = 'jf-row__header';
        var title = document.createElement('h2');
        title.className   = 'jf-row__title';
        title.textContent = rail.title || '';
        header.appendChild(title);

        // Scroller
        var scroller = document.createElement('div');
        scroller.className = 'jf-row__scroller';

        (rail.items || []).forEach(function (item) {
            scroller.appendChild(JF.Card({
                item:     item,
                format:   format,
                onSelect: onSelect
            }));
        });

        row.appendChild(header);
        row.appendChild(scroller);
        return row;
    };
})();
