/* ================================================================
   PREFETCH DE NAVEGACIÓN — percepción de velocidad
   Precarga páginas internas cuando el usuario muestra intención
   (hover en desktop / touchstart en mobile). El clic posterior
   usa la copia en caché del navegador y se siente instantáneo.

   Seguro: solo enlaces GET del mismo origen. No toca formularios
   (agregar al carrito, login, etc.) ni la lógica de la app.
   ================================================================ */
(function () {
    'use strict';

    // Respetar ahorro de datos y conexiones lentas
    var conn = navigator.connection;
    if (conn && (conn.saveData || /2g/.test(conn.effectiveType || ''))) return;
    if (!document.createElement('link').relList.supports('prefetch')) return;

    var yaPrecargado = {};   // dedupe por URL
    var timer = null;

    function esNavegable(a) {
        if (!a || !a.href) return false;
        if (a.origin !== location.origin) return false;                 // mismo origen
        if (a.hasAttribute('download')) return false;
        if (a.target && a.target !== '' && a.target !== '_self') return false;
        var href = a.getAttribute('href') || '';
        if (href.charAt(0) === '#') return false;                       // ancla interna
        if (/^(mailto:|tel:|javascript:)/i.test(href)) return false;
        if (a.dataset.noPrefetch !== undefined) return false;           // opt-out explícito
        var url = a.href.split('#')[0];
        if (url === location.href.split('#')[0]) return false;          // página actual
        return !yaPrecargado[url];
    }

    function precargar(url) {
        yaPrecargado[url] = true;
        var link = document.createElement('link');
        link.rel = 'prefetch';
        link.href = url;
        link.as = 'document';
        document.head.appendChild(link);
    }

    function alApuntar(e) {
        var a = e.target.closest && e.target.closest('a');
        if (!esNavegable(a)) return;
        var url = a.href.split('#')[0];
        clearTimeout(timer);
        // Pequeño retardo de intención: evita precargar todo lo que roza el cursor
        timer = setTimeout(function () { precargar(url); }, 60);
    }

    function cancelar() { clearTimeout(timer); }

    document.addEventListener('mouseover', alApuntar, { passive: true });
    document.addEventListener('mouseout', cancelar, { passive: true });
    // En mobile, touchstart precarga apenas antes del click
    document.addEventListener('touchstart', function (e) {
        var a = e.target.closest && e.target.closest('a');
        if (esNavegable(a)) precargar(a.href.split('#')[0]);
    }, { passive: true });

    // Precarga proactiva de la página siguiente (paginación del catálogo).
    // Se hace en tiempo idle para no competir con la carga de la página actual.
    function precargarSiguiente() {
        var next = document.querySelector('a[rel="next"]');
        if (esNavegable(next)) precargar(next.href.split('#')[0]);
    }
    if ('requestIdleCallback' in window) {
        requestIdleCallback(precargarSiguiente, { timeout: 2000 });
    } else {
        setTimeout(precargarSiguiente, 800);
    }
}());
