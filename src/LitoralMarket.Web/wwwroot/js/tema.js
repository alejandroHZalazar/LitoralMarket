/* ================================================================
   LitoralMarket — Theme System
   Temas: light | dark | system | high-contrast
   Persistencia: cookie (leída por el servidor) + localStorage
   ================================================================ */
(function () {
    'use strict';

    var COOKIE_NAME = 'lm_theme';
    var LS_KEY      = 'lm_theme';
    var VALID       = ['light', 'dark', 'system', 'high-contrast'];

    var labels = {
        light:          'Claro',
        dark:           'Oscuro',
        system:         'Sistema',
        'high-contrast':'Alto contraste'
    };

    var icons = {
        light:          'bi-sun',
        dark:           'bi-moon-stars',
        system:         'bi-display',
        'high-contrast':'bi-eye'
    };

    // ── Aplicar tema al documento ──────────────────────────────────
    function apply(theme) {
        var root = document.documentElement;
        if (theme === 'system') {
            root.removeAttribute('data-theme');
        } else {
            root.setAttribute('data-theme', theme);
        }
    }

    // ── Leer preferencia guardada ──────────────────────────────────
    function getSaved() {
        try {
            var v = localStorage.getItem(LS_KEY);
            if (v && VALID.indexOf(v) !== -1) return v;
        } catch (e) {}
        var m = document.cookie.match('(?:^|;)\\s*' + COOKIE_NAME + '=([^;]+)');
        if (m && VALID.indexOf(m[1]) !== -1) return m[1];
        return 'light';
    }

    // ── Guardar preferencia ────────────────────────────────────────
    function save(theme) {
        try { localStorage.setItem(LS_KEY, theme); } catch (e) {}
        // Cookie leída por el servidor (previene FOUC en primer render)
        document.cookie = COOKIE_NAME + '=' + theme +
            '; path=/; max-age=31536000; SameSite=Lax';
    }

    // ── Cambiar tema ───────────────────────────────────────────────
    function set(theme) {
        if (VALID.indexOf(theme) === -1) return;
        apply(theme);
        save(theme);
        updateButtons(theme);
        document.dispatchEvent(new CustomEvent('lm:theme-changed', { detail: { theme: theme } }));
    }

    // ── Actualizar estado visual de los botones del switcher ───────
    function updateButtons(current) {
        document.querySelectorAll('[data-lm-theme-btn]').forEach(function (btn) {
            var t = btn.getAttribute('data-lm-theme-btn');
            btn.classList.toggle('active', t === current);
            btn.setAttribute('aria-pressed', t === current ? 'true' : 'false');
        });
        // Actualizar ícono del botón principal del switcher
        var icon = document.querySelector('#lm-theme-icon');
        if (icon) {
            var t = current === 'system'
                ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
                : current;
            icon.className = 'bi ' + (icons[t] || 'bi-circle-half');
        }
    }

    // ── Inicialización ─────────────────────────────────────────────
    function init() {
        var saved = getSaved();
        apply(saved);

        // Adjuntar handlers una vez que el DOM esté listo
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', onReady.bind(null, saved));
        } else {
            onReady(saved);
        }

        // Escuchar cambios del sistema cuando tema = "system"
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            if (getSaved() === 'system') updateButtons('system');
        });
    }

    function onReady(saved) {
        updateButtons(saved);

        document.querySelectorAll('[data-lm-theme-btn]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                set(btn.getAttribute('data-lm-theme-btn'));
            });
        });
    }

    // ── API pública ────────────────────────────────────────────────
    window.LitoralTheme = {
        set:    set,
        get:    getSaved,
        labels: labels,
        icons:  icons,
        themes: VALID
    };

    // Aplicar inmediatamente (antes de DOMContentLoaded) para evitar FOUC
    apply(getSaved());

    init();
}());
