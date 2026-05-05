/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/api.js
 *
 *  Direct integration with the Jellyfin REST API via the
 *  globally-exposed window.ApiClient. We use ApiClient.fetch()
 *  because it transparently adds:
 *    - X-Emby-Authorization header (server, client, device, version)
 *    - X-Emby-Token (the user's access token)
 *    - Server base URL (handles reverse-proxy installs)
 *  Replicating that ourselves would require pulling the token from
 *  localStorage / sessionStorage, which Jellyfin reshuffles between
 *  versions — ApiClient is the contract.
 *
 *  Public API:
 *    JF.api.jellyfin.isAvailable()
 *    JF.api.jellyfin.userId()
 *    JF.api.jellyfin.getLatest({ types, limit })
 *    JF.api.jellyfin.getResume({ limit })
 *    JF.api.jellyfin.getMovies({ limit })
 *    JF.api.jellyfin.getSeries({ limit })
 *    JF.api.normalize(jellyfinItem)            → our internal shape
 *    JF.api.buildHome()                        → { slider, rails }
 *
 *  All functions return Promises. None throw — failure paths
 *  resolve to empty arrays so the UI never crashes on a flaky
 *  endpoint (a single rail just shows nothing).
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};
    JF.api = JF.api || {};

    // ── Jellyfin client wrapper ──────────────────────────────
    JF.api.jellyfin = {
        /** True when window.ApiClient is exposed and authenticated. */
        isAvailable: function () {
            return !!(window.ApiClient
                   && typeof window.ApiClient.fetch === 'function'
                   && typeof window.ApiClient.getCurrentUserId === 'function'
                   && window.ApiClient.getCurrentUserId());
        },

        /** Returns the active user's GUID (or null when logged out). */
        userId: function () {
            try { return window.ApiClient.getCurrentUserId() || null; }
            catch (e) { return null; }
        },

        /**
         * Builds a fully-qualified URL via ApiClient.getUrl (handles
         * reverse-proxy bases) and runs the GET through ApiClient.fetch
         * so auth headers are injected. Catches network failures and
         * returns [] so callers don't need their own try/catch.
         */
        request: function (path, params) {
            try {
                var url = window.ApiClient.getUrl(path, params || {});
                return window.ApiClient.fetch({
                    url:      url,
                    type:     'GET',
                    dataType: 'json'
                }).catch(function () { return null; });
            } catch (e) {
                return Promise.resolve(null);
            }
        },

        // ── Recently added ───────────────────────────────────
        // Jellyfin's "Latest" endpoint already orders by DateCreated
        // desc and returns the SHAPE we want for "Recently Added"
        // sections (no nested Items/Result envelope).
        getLatest: function (opts) {
            opts = opts || {};
            var uid = this.userId();
            if (!uid) return Promise.resolve([]);
            return this.request('/Users/' + uid + '/Items/Latest', {
                IncludeItemTypes: opts.types  || 'Movie,Series',
                Limit:            opts.limit  || 20,
                EnableImageTypes: 'Primary,Backdrop,Thumb',
                Fields:           'Overview,ProductionYear,CommunityRating'
            }).then(function (r) { return Array.isArray(r) ? r : []; });
        },

        // ── Continue watching ────────────────────────────────
        getResume: function (opts) {
            opts = opts || {};
            var uid = this.userId();
            if (!uid) return Promise.resolve([]);
            return this.request('/Users/' + uid + '/Items/Resume', {
                Limit:            opts.limit || 12,
                MediaTypes:       'Video',
                EnableImageTypes: 'Primary,Backdrop,Thumb',
                Fields:           'Overview,ProductionYear,CommunityRating'
            }).then(function (r) {
                return (r && Array.isArray(r.Items)) ? r.Items : [];
            });
        },

        // ── Movies ───────────────────────────────────────────
        getMovies: function (opts) {
            opts = opts || {};
            var uid = this.userId();
            if (!uid) return Promise.resolve([]);
            return this.request('/Users/' + uid + '/Items', {
                IncludeItemTypes: 'Movie',
                Recursive:        true,
                SortBy:           opts.sortBy    || 'DateCreated',
                SortOrder:        opts.sortOrder || 'Descending',
                Limit:            opts.limit     || 20,
                EnableImageTypes: 'Primary,Backdrop,Thumb',
                Fields:           'Overview,ProductionYear,CommunityRating'
            }).then(function (r) {
                return (r && Array.isArray(r.Items)) ? r.Items : [];
            });
        },

        // ── Series ───────────────────────────────────────────
        getSeries: function (opts) {
            opts = opts || {};
            var uid = this.userId();
            if (!uid) return Promise.resolve([]);
            return this.request('/Users/' + uid + '/Items', {
                IncludeItemTypes: 'Series',
                Recursive:        true,
                SortBy:           opts.sortBy    || 'DateCreated',
                SortOrder:        opts.sortOrder || 'Descending',
                Limit:            opts.limit     || 20,
                EnableImageTypes: 'Primary,Backdrop,Thumb',
                Fields:           'Overview,ProductionYear,CommunityRating'
            }).then(function (r) {
                return (r && Array.isArray(r.Items)) ? r.Items : [];
            });
        }
    };

    // ── Normalizer ───────────────────────────────────────────
    /**
     * Maps a raw Jellyfin Item DTO to the internal shape the new
     * frontend components consume (id, name, overview, year, rating,
     * type, imageUrl). Imager URL prefers Backdrop for landscape rails
     * but is left undefined when no asset exists so JF.Card.imageUrl
     * derives the canonical /Items/{id}/Images/* URL on demand.
     */
    JF.api.normalize = function (it) {
        if (!it || !it.Id) return null;
        var hasBackdrop = Array.isArray(it.BackdropImageTags) && it.BackdropImageTags.length > 0;
        return {
            id:       it.Id,
            name:     it.Name || it.SeriesName || '',
            overview: it.Overview || '',
            year:     it.ProductionYear || (it.PremiereDate ? new Date(it.PremiereDate).getFullYear() : null),
            rating:   typeof it.CommunityRating === 'number' ? it.CommunityRating : null,
            type:     it.Type,
            // Hero needs a wide image; cards build their own URLs
            // unless this one wins by being more specific.
            imageUrl: hasBackdrop
                ? '/Items/' + it.Id + '/Images/Backdrop?fillHeight=720&quality=85'
                : undefined
        };
    };

    // ── Aggregator ───────────────────────────────────────────
    /**
     * Builds the full home payload from parallel Jellyfin requests.
     * Resolves to { slider, rails } in the SAME shape app.js's
     * renderInitial() already consumes — no changes to render path.
     *
     * Failure tolerance: each fetch's .catch maps to [], so a single
     * dead endpoint (e.g. Resume returns 500 on a fresh install with
     * zero playback history) just yields an empty rail. The rest of
     * the home renders normally.
     */
    JF.api.buildHome = function () {
        var jf  = JF.api.jellyfin;
        var nrm = JF.api.normalize;

        if (!jf.isAvailable()) {
            console.warn('[JF] Jellyfin ApiClient not available — buildHome returning empty.');
            return Promise.resolve({
                slider: { style: 'default', items: [] },
                rails:  []
            });
        }

        return Promise.all([
            jf.getResume({ limit: 12 }),
            jf.getLatest({ types: 'Movie',  limit: 20 }),
            jf.getLatest({ types: 'Series', limit: 20 })
        ]).then(function (results) {
            var resume = results[0].map(nrm).filter(Boolean);
            var movies = results[1].map(nrm).filter(Boolean);
            var series = results[2].map(nrm).filter(Boolean);

            // Hero takes the first 5 newest movies (skipped if empty).
            var heroSource = movies.length ? movies : series;
            var heroItems  = heroSource.slice(0, 5);

            var rails = [];
            if (resume.length) rails.push({
                id:     'continueWatching',
                title:  'Continuar viendo',
                format: 'landscape',
                items:  resume
            });
            if (movies.length) rails.push({
                id:     'latestMovies',
                title:  'Películas recién añadidas',
                format: 'portrait',
                items:  movies
            });
            if (series.length) rails.push({
                id:     'latestSeries',
                title:  'Series recién añadidas',
                format: 'portrait',
                items:  series
            });

            return {
                slider: { style: 'default', items: heroItems, autoplay: true, autoplayInterval: 8 },
                rails:  rails
            };
        });
    };
})();
