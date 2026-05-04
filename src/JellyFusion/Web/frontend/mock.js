/* ────────────────────────────────────────────────────────────
 *  JellyFusion · frontend/mock.js
 *
 *  Hardcoded sample data so the components can be previewed
 *  WITHOUT a live Jellyfin backend. Image URLs hit TMDB's public
 *  CDN — no auth, no rate limits worth worrying about for a few
 *  tabs of preview.
 *
 *  Usage:
 *    JF.bootMock();   // boots app with mock data instead of fetching
 *
 *  The shape mirrors what /jellyfusion/{navigation, slider/items,
 *  home/rails} return so the app.js render path is identical
 *  regardless of source.
 * ──────────────────────────────────────────────────────────── */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};

    var TMDB = 'https://image.tmdb.org/t/p';
    var POSTER  = function (p) { return TMDB + '/w500'  + p; };
    var STILL   = function (p) { return TMDB + '/w1280' + p; };

    JF.mock = {
        nav: {
            items: [
                { id: 'home',      label: 'Inicio',       url: '/home.html' },
                { id: 'movies',    label: 'Películas',    url: '/movies.html' },
                { id: 'series',    label: 'Series',       url: '/tv.html' },
                { id: 'live',      label: 'TV en vivo',   url: '/livetv.html' },
                { id: 'favorites', label: 'Favoritos',    url: '/home.html?tab=1' }
            ]
        },

        slider: {
            style:            'default',
            autoplay:         true,
            autoplayInterval: 7,
            items: [
                {
                    id:       'mock-hero-1',
                    name:     'Project Hail Mary',
                    overview: 'Un astronauta solitario despierta en una nave espacial con la misión de salvar a la humanidad de una catástrofe que amenaza el sol.',
                    year:     2026,
                    rating:   8.4,
                    imageUrl: STILL('/jBJWaqoSCiARWtfV0GlqHrcdidd.jpg')
                },
                {
                    id:       'mock-hero-2',
                    name:     'Dune: Part Three',
                    overview: 'Paul Atreides asciende como emperador del universo conocido y debe enfrentarse a las consecuencias de su jihad galáctica.',
                    year:     2026,
                    rating:   8.1,
                    imageUrl: STILL('/euYIwmwkmz95mnXvufEmbL6ovhZ.jpg')
                },
                {
                    id:       'mock-hero-3',
                    name:     'Avatar: Fire and Ash',
                    overview: 'Jake Sully y Neytiri llevan a su familia a las regiones más remotas de Pandora para protegerlos de una nueva amenaza.',
                    year:     2025,
                    rating:   7.9,
                    imageUrl: STILL('/8b8R8l88Qje9dn9OE8PY05Nxl1X.jpg')
                }
            ]
        },

        rails: [
            {
                id:       'continueWatching',
                title:    'Continuar viendo',
                format:   'landscape',
                items: [
                    { id: 'm1', name: 'The Last of Us', year: 2025, rating: 8.7,
                      imageUrl: STILL('/uKvVjHNqB5VmOrdxqAt2F7J78ED.jpg') },
                    { id: 'm2', name: 'Severance',     year: 2025, rating: 8.4,
                      imageUrl: STILL('/lTsLhsGdxX6RfeolMeLWDovB22a.jpg') },
                    { id: 'm3', name: 'Andor',         year: 2025, rating: 8.5,
                      imageUrl: STILL('/3tvBqYsBhxWeHlu62SIJ1el93M7.jpg') },
                    { id: 'm4', name: 'Slow Horses',   year: 2024, rating: 8.2,
                      imageUrl: STILL('/sukhUO0i5JAg8OWqQtwUfEihdX5.jpg') }
                ]
            },
            {
                id:        'top10Movies',
                title:     'Top 10 Películas hoy',
                format:    'portrait',
                showRank:  true,
                items: [
                    { id: 'p1', name: 'Oppenheimer',          year: 2023, rating: 8.3,
                      imageUrl: POSTER('/8Gxv8gSFCU0XGDykEGv7zR1n2ua.jpg') },
                    { id: 'p2', name: 'Dune: Part Two',        year: 2024, rating: 8.4,
                      imageUrl: POSTER('/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg') },
                    { id: 'p3', name: 'Inception',             year: 2010, rating: 8.8,
                      imageUrl: POSTER('/9gk7adHYeDvHkCSEqAvQNLV5Uge.jpg') },
                    { id: 'p4', name: 'Interstellar',          year: 2014, rating: 8.6,
                      imageUrl: POSTER('/gEU2QniE6E77NI6lCU6MxlNBvIx.jpg') },
                    { id: 'p5', name: 'The Dark Knight',       year: 2008, rating: 9.0,
                      imageUrl: POSTER('/qJ2tW6WMUDux911r6m7haRef0WH.jpg') },
                    { id: 'p6', name: 'Parasite',              year: 2019, rating: 8.5,
                      imageUrl: POSTER('/7IiTTgloJzvGI1TAYymCfbfl3vT.jpg') },
                    { id: 'p7', name: 'Everything Everywhere', year: 2022, rating: 7.9,
                      imageUrl: POSTER('/w3LxiVYdWWRvEVdn5RYq6jIqkb1.jpg') }
                ]
            },
            {
                id:       'newReleases',
                title:    'Recién añadido',
                format:   'portrait',
                items: [
                    { id: 'n1', name: 'Furiosa',          year: 2024, rating: 7.6,
                      imageUrl: POSTER('/iADOJ8Zymht2JPMoy3R7xceZprc.jpg') },
                    { id: 'n2', name: 'Civil War',        year: 2024, rating: 7.0,
                      imageUrl: POSTER('/sh7Rg8Er3tFcN9BpKIPOMvALgZd.jpg') },
                    { id: 'n3', name: 'Challengers',      year: 2024, rating: 7.1,
                      imageUrl: POSTER('/H6j5smdpRqP9a8UnhWp6zfl0SC.jpg') },
                    { id: 'n4', name: 'The Substance',    year: 2024, rating: 7.4,
                      imageUrl: POSTER('/lqoMzCcZYEFK729d6qzt349fB4o.jpg') },
                    { id: 'n5', name: 'Anora',            year: 2024, rating: 7.7,
                      imageUrl: POSTER('/lyzM9pZ7KfHj9vDCLXlElG3VRkD.jpg') }
                ]
            },
            {
                id:       'recommended',
                title:    'Recomendado para ti',
                format:   'small',
                items: [
                    { id: 'r1', name: 'Whiplash',     year: 2014, rating: 8.5,
                      imageUrl: POSTER('/7fn624j5lj3xTme2SgiLCeuedmO.jpg') },
                    { id: 'r2', name: 'La La Land',   year: 2016, rating: 8.0,
                      imageUrl: POSTER('/uDO8zWDhfWwoFdKS4fzkUJt0Rf0.jpg') },
                    { id: 'r3', name: 'Coco',         year: 2017, rating: 8.4,
                      imageUrl: POSTER('/gGEsBPAijhVUFoiNpgZXqRVWJt2.jpg') },
                    { id: 'r4', name: 'Spirited Away', year: 2001, rating: 8.6,
                      imageUrl: POSTER('/39wmItIWsg5sZMyRUHLkWBcuVCM.jpg') },
                    { id: 'r5', name: 'Soul',         year: 2020, rating: 8.0,
                      imageUrl: POSTER('/hm58Jw4Lw8OIeECIq5qyPYhAeRJ.jpg') },
                    { id: 'r6', name: 'Coraline',     year: 2009, rating: 7.7,
                      imageUrl: POSTER('/4jeFXQYytChdZYE9JniS8MtZVlD.jpg') }
                ]
            }
        ]
    };

    /**
     * Boots the app with mock data (skips the /jellyfusion/* fetches).
     * Idempotent: bails out if JF.boot already ran.
     */
    JF.bootMock = function () {
        if (JF.state && JF.state.booted) return;

        // Patch fetchJson to return mock data so JF.boot() takes the
        // normal render path. We restore it after the initial render
        // so any later fetches (trailers, refresh) hit the real API.
        var realFetch = JF.api.fetchJson;
        JF.api.fetchJson = function (url) {
            if (url.indexOf('/navigation')   >= 0) return Promise.resolve(JF.mock.nav);
            if (url.indexOf('/slider/items') >= 0) return Promise.resolve(JF.mock.slider);
            if (url.indexOf('/home/rails')   >= 0) return Promise.resolve(JF.mock.rails);
            return realFetch(url);
        };

        JF.boot();
        // Restore the live fetcher on the next tick.
        setTimeout(function () { JF.api.fetchJson = realFetch; }, 0);
    };
})();
