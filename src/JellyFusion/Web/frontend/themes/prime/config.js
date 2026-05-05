/* Prime Video theme — minimal config (colors only). */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};
    JF.themes = JF.themes || { registry: {} };
    JF.themes.register('prime', {
        id: 'prime',
        name: 'Prime Video',
        onApply: function () {
            var host = document.getElementById('jf-app');
            if (host) host.setAttribute('data-jf-theme', 'prime');
        },
        onRemove: function () {
            var host = document.getElementById('jf-app');
            if (host) host.removeAttribute('data-jf-theme');
        }
    });
})();
