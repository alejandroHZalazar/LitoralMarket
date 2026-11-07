/* Carrusel de promociones: desplazamiento manual y automático.
   - Manual: scroll nativo (rueda, dedo, teclado) con scroll-snap + flechas anterior/siguiente.
   - Automático: avanza una tarjeta cada data-intervalo segundos (parámetro IntervaloCarruselPromociones).
     Ante cualquier interacción se pausa y se reanuda tras IDLE_MS de inactividad. También se pausa
     con el mouse encima, con la pestaña oculta y cuando el carrusel no está en pantalla.
   - Al llegar a la última promoción vuelve al inicio con desplazamiento suave (sin salto brusco).
   - Con "reducir movimiento" activado en el sistema no hay avance automático. */
(function () {
    'use strict';

    var IDLE_MS = 10000;        // inactividad necesaria para reanudar tras una interacción manual
    var INTERVALO_DEFECTO = 5;  // segundos, si el valor recibido no es válido

    function iniciar(root) {
        var track = root.querySelector('[data-carrusel-track]');
        var btnPrev = root.querySelector('[data-carrusel-prev]');
        var btnNext = root.querySelector('[data-carrusel-next]');
        var slides = track ? track.querySelectorAll('.carrusel-slide') : [];
        if (!track || slides.length === 0) return;

        var seg = parseInt(root.getAttribute('data-intervalo'), 10);
        var intervaloMs = (isFinite(seg) && seg >= 2 && seg <= 60 ? seg : INTERVALO_DEFECTO) * 1000;
        var reducir = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        var ultimaInteraccion = 0;
        var sobre = false;       // mouse encima
        var visible = true;      // carrusel dentro de la pantalla

        function paso() {
            if (slides.length < 2) return track.clientWidth;
            return slides[1].offsetLeft - slides[0].offsetLeft;     // ancho de tarjeta + separación
        }
        function maxScroll() { return track.scrollWidth - track.clientWidth; }
        function hayDesborde() { return maxScroll() > 2; }

        function actualizarFlechas() {
            var mostrar = hayDesborde();
            btnPrev.hidden = btnNext.hidden = !mostrar;
        }

        // Siguiente / anterior con vuelta al otro extremo en los bordes. behavior 'auto' = usa el CSS scroll-behavior
        // de la pista (suave; instantáneo si el sistema pide reducir movimiento).
        function mover(dir) {
            if (!hayDesborde()) return;
            var x = track.scrollLeft, fin = maxScroll();
            // La primera tarjeta queda a "padding-left" del borde: ese es el inicio real de la pista.
            var inicio = parseFloat(getComputedStyle(track).paddingLeft) || 0;
            if (dir > 0) {
                if (x >= fin - 2) track.scrollTo({ left: 0, behavior: 'auto' });
                else track.scrollBy({ left: paso(), behavior: 'auto' });
            } else {
                if (x <= inicio + 2) track.scrollTo({ left: fin, behavior: 'auto' });
                else track.scrollBy({ left: -paso(), behavior: 'auto' });
            }
        }

        function interaccion() { ultimaInteraccion = Date.now(); }

        btnNext.addEventListener('click', function () { interaccion(); mover(1); });
        btnPrev.addEventListener('click', function () { interaccion(); mover(-1); });

        // Interacción manual: pausa el avance automático durante IDLE_MS.
        ['pointerdown', 'touchstart', 'wheel', 'keydown'].forEach(function (ev) {
            track.addEventListener(ev, interaccion, { passive: true });
        });
        track.addEventListener('focusin', interaccion);
        root.addEventListener('mouseenter', function () { sobre = true; });
        root.addEventListener('mouseleave', function () { sobre = false; });

        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (entradas) {
                visible = entradas[0].isIntersecting;
            }, { threshold: 0.25 }).observe(root);
        }

        if ('ResizeObserver' in window) new ResizeObserver(actualizarFlechas).observe(track);
        window.addEventListener('resize', actualizarFlechas);
        actualizarFlechas();

        // Avance automático
        if (!reducir) {
            setInterval(function () {
                if (document.hidden || sobre || !visible || !hayDesborde()) return;
                if (Date.now() - ultimaInteraccion < IDLE_MS) return;
                mover(1);
            }, intervaloMs);
        }
    }

    function arrancar() {
        document.querySelectorAll('[data-carrusel-promos]').forEach(iniciar);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', arrancar);
    else arrancar();
})();
