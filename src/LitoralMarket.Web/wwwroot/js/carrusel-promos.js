/* Carrusel de promociones: una imagen por vez, a todo el ancho.
   - Movimiento: la pista se desplaza con transform (transición de 400 ms). Con 2 o más imágenes se agregan una copia
     de la última al inicio y una de la primera al final: al llegar a una copia se salta, sin animación, a la
     imagen real equivalente, así el loop no se nota.
   - Manual: flechas, puntos, teclado (← →) y deslizar con el dedo / mouse.
   - Automático: avanza cada data-intervalo segundos (parámetro IntervaloCarruselPromociones). Ante cualquier
     interacción se pausa y se reanuda tras IDLE_MS de inactividad. También se pausa con el mouse encima, con la
     pestaña oculta y cuando el carrusel no está en pantalla.
   - Con "reducir movimiento" activado en el sistema no hay avance automático ni animación.
   - Con una sola imagen no hay flechas, puntos ni avance. */
(function () {
    'use strict';

    var IDLE_MS = 10000;        // inactividad necesaria para reanudar tras una interacción manual
    var INTERVALO_DEFECTO = 5;  // segundos, si el valor recibido no es válido
    var DURACION_MS = 400;      // igual a la transición del CSS
    var UMBRAL_SWIPE = 40;      // px de arrastre para cambiar de imagen

    function iniciar(root) {
        var track = root.querySelector('[data-carrusel-track]');
        var btnPrev = root.querySelector('[data-carrusel-prev]');
        var btnNext = root.querySelector('[data-carrusel-next]');
        var dotsBox = root.querySelector('[data-carrusel-dots]');
        var reales = track ? track.querySelectorAll('.carrusel-slide') : [];
        var n = reales.length;
        if (!track || n === 0) return;
        if (n < 2) return;   // una sola imagen: sin controles ni movimiento

        var seg = parseInt(root.getAttribute('data-intervalo'), 10);
        var intervaloMs = (isFinite(seg) && seg >= 2 && seg <= 60 ? seg : INTERVALO_DEFECTO) * 1000;
        var reducir = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        // Copias de los extremos para el loop infinito (ocultas a lectores de pantalla y al teclado).
        function copia(el) {
            var c = el.cloneNode(true);
            c.setAttribute('aria-hidden', 'true');
            c.removeAttribute('role');
            c.removeAttribute('aria-label');
            var im = c.querySelector('img');
            if (im) { im.setAttribute('alt', ''); im.loading = 'eager'; im.removeAttribute('fetchpriority'); }
            return c;
        }
        track.insertBefore(copia(reales[n - 1]), track.firstChild);
        track.appendChild(copia(reales[0]));

        // La pista mide (n+2) veces el visible y cada imagen ocupa 1/(n+2) de ella = el ancho completo del carrusel.
        track.style.width = ((n + 2) * 100) + '%';
        Array.prototype.forEach.call(track.children, function (s) { s.style.flex = '0 0 ' + (100 / (n + 2)) + '%'; });

        // Posición en la pista: 1..n = imágenes reales; 0 y n+1 = copias.
        var pos = 1;
        var animando = false;
        var ultimaInteraccion = 0;
        var sobre = false;
        var visible = true;
        var timerFin = null;

        function aplicar(animar) {
            track.style.transition = (animar && !reducir) ? 'transform ' + DURACION_MS + 'ms ease' : 'none';
            track.style.transform = 'translate3d(' + (-pos * 100 / (n + 2)) + '%,0,0)';
        }

        function actual() { return ((pos - 1 + n) % n); }   // índice real 0..n-1

        function marcarPuntos() {
            var a = actual();
            Array.prototype.forEach.call(dotsBox.children, function (d, i) {
                var on = i === a;
                d.classList.toggle('activo', on);
                d.setAttribute('aria-selected', on ? 'true' : 'false');
                d.tabIndex = on ? 0 : -1;
            });
        }

        // Al terminar la animación: si quedó sobre una copia, salta sin animar a la imagen real equivalente.
        function terminar() {
            clearTimeout(timerFin);
            if (pos === 0) pos = n;
            else if (pos === n + 1) pos = 1;
            aplicar(false);
            animando = false;
        }

        function ir(destino) {
            if (animando) return;
            animando = true;
            pos = destino;
            aplicar(true);
            marcarPuntos();
            // transitionend no se emite si no hay animación (reducir movimiento): se asegura con un temporizador.
            clearTimeout(timerFin);
            timerFin = setTimeout(terminar, reducir ? 0 : DURACION_MS + 60);
        }

        function siguiente() { ir(pos + 1); }
        function anterior()  { ir(pos - 1); }
        function irAReal(i) { ir(i + 1); }

        track.addEventListener('transitionend', function (e) {
            if (e.target === track && e.propertyName === 'transform') terminar();
        });

        // Puntos
        for (var i = 0; i < n; i++) {
            (function (idx) {
                var d = document.createElement('button');
                d.type = 'button';
                d.className = 'carrusel-dot';
                d.setAttribute('role', 'tab');
                d.setAttribute('aria-label', 'Ir a la promoción ' + (idx + 1));
                d.addEventListener('click', function () { interaccion(); irAReal(idx); });
                dotsBox.appendChild(d);
            })(i);
        }
        btnPrev.hidden = btnNext.hidden = false;

        function interaccion() { ultimaInteraccion = Date.now(); }

        btnNext.addEventListener('click', function () { interaccion(); siguiente(); });
        btnPrev.addEventListener('click', function () { interaccion(); anterior(); });

        root.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowRight') { interaccion(); siguiente(); e.preventDefault(); }
            else if (e.key === 'ArrowLeft') { interaccion(); anterior(); e.preventDefault(); }
        });

        // Deslizar con el dedo o el mouse
        var x0 = null, y0 = null;
        var arrastro = false;     // un arrastre no debe disparar el enlace del banner (promoción asociada)
        track.addEventListener('pointerdown', function (e) {
            if (e.pointerType === 'mouse' && e.button !== 0) return;
            x0 = e.clientX; y0 = e.clientY;
            interaccion();
        });
        track.addEventListener('pointerup', function (e) {
            if (x0 === null) return;
            var dx = e.clientX - x0, dy = e.clientY - y0;
            x0 = y0 = null;
            if (Math.abs(dx) >= UMBRAL_SWIPE && Math.abs(dx) > Math.abs(dy)) { arrastro = true; interaccion(); dx < 0 ? siguiente() : anterior(); }
        });
        // Fase de captura: si hubo arrastre se anula el clic que sigue al soltar; un clic normal navega.
        track.addEventListener('click', function (e) {
            if (arrastro) { e.preventDefault(); e.stopPropagation(); }
            arrastro = false;
        }, true);
        track.addEventListener('pointercancel', function () { x0 = y0 = null; });
        track.addEventListener('dragstart', function (e) { e.preventDefault(); });

        root.addEventListener('mouseenter', function () { sobre = true; });
        root.addEventListener('mouseleave', function () { sobre = false; });
        root.addEventListener('focusin', interaccion);

        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (entradas) {
                visible = entradas[0].isIntersecting;
            }, { threshold: 0.25 }).observe(root);
        }

        aplicar(false);
        marcarPuntos();

        // Avance automático
        if (!reducir) {
            setInterval(function () {
                if (document.hidden || sobre || !visible) return;
                if (Date.now() - ultimaInteraccion < IDLE_MS) return;
                siguiente();
            }, intervaloMs);
        }
    }

    function arrancar() {
        document.querySelectorAll('[data-carrusel-promos]').forEach(iniciar);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', arrancar);
    else arrancar();
})();
