/* Disney+ theme — minimal config (colors only, no JS hooks). */
(function () {
    'use strict';
    var JF = window.JF = window.JF || {};
    JF.themes = JF.themes || { registry: {} };
    JF.themes.register('disney', {
        id: 'disney',
        name: 'Disney+',
        onApply: function () {
            var host = document.getElementById('jf-app');
            if (host) host.setAttribute('data-jf-theme', 'disney');
        },
        onRemove: function () {
            var host = document.getElementById('jf-app');
            if (host) host.removeAttribute('data-jf-theme');
        }
    });
})();
