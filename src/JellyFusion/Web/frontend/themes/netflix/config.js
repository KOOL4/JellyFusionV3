/* ────────────────────────────────────────────────────────────
 *  Netflix theme — config.js
 *
 *  Optional JS hooks. Themes can implement any subset; the core
 *  invokes them via JF.themes.invoke(hookName, args) and ignores
 *  the result when the hook isn't defined.
 *
 *  Hooks supported by the current core:
 *    - onApply()
 *    - onRemove()
 *    - cardOverlayExtras(item) → string of HTML to prepend inside
 *      the card overlay (we use this to inject the "98% Match"
 *      badge that's iconic to Netflix).
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};
    JF.themes = JF.themes || { registry: {} };

    JF.themes.register('netflix', {
        id:   'netflix',
        name: 'Netflix',

        /**
         * Called once after the theme link is in the DOM.
         * Use it to set up any DOM observers or document-level
         * listeners the theme needs. Always pair with onRemove().
         */
        onApply: function () {
            // Tag the host so any user-space tooling can detect the
            // active theme. This is NOT used by the core selectors —
            // it's just a read-only flag for downstream code.
            var host = document.getElementById('jf-app');
            if (host) host.setAttribute('data-jf-theme', 'netflix');
        },

        onRemove: function () {
            var host = document.getElementById('jf-app');
            if (host) host.removeAttribute('data-jf-theme');
        },

        /**
         * Called by card.js when building each card's overlay.
         * Returns a string of HTML that will be prepended inside
         * the .jf-card__overlay before the title. The score is
         * synthesised from item.rating to mimic Netflix's "Match %"
         * (which is a personalised relevance score).
         */
        cardOverlayExtras: function (item) {
            if (!item) return '';
            var rating = typeof item.rating === 'number' ? item.rating : null;
            if (rating === null) return '';
            var match = Math.min(99, Math.round(70 + rating * 3));
            return '<div class="jf-card__match">' + match + '% Match</div>';
        }
    });
})();
