/* =============================================
   CONTROL DE CANTIDAD (+/-)
   ============================================= */
document.addEventListener('click', function (e) {
    const btn = e.target.closest('.btn-menos, .btn-mas');
    if (!btn) return;

    const control = btn.closest('.cantidad-control');
    const input   = control.querySelector('.cantidad-input');
    const step    = parseFloat(input.step) || 1;
    const min     = parseFloat(input.min)  || step;
    const max     = input.max ? parseFloat(input.max) : Infinity;
    let   val     = parseFloat(input.value) || min;

    if (btn.classList.contains('btn-menos')) {
        val = Math.max(min, parseFloat((val - step).toFixed(4)));
    } else {
        val = Math.min(max, parseFloat((val + step).toFixed(4)));
    }

    input.value = step < 1 ? val.toFixed(2) : val.toString();
});

// Validar ingreso manual
document.addEventListener('change', function (e) {
    const input = e.target.closest('.cantidad-input');
    if (!input) return;

    const step = parseFloat(input.step) || 1;
    const min  = parseFloat(input.min)  || step;
    const max  = input.max ? parseFloat(input.max) : Infinity;
    let val    = parseFloat(input.value);

    if (isNaN(val) || val < min) val = min;
    if (val > max) val = max;

    input.value = step < 1 ? val.toFixed(2) : Math.round(val / step) * step;
});


/* =============================================
   AGREGAR AL CARRITO — AJAX con UI optimista
   El feedback táctil del botón es inmediato (0 ms); el toast y el
   badge se reconcilian con la respuesta real del servidor. Así la
   interacción se siente instantánea sin cambiar la lógica del backend.
   ============================================= */
document.addEventListener('submit', function (e) {
    const form = e.target.closest('[data-agregar-form]');
    if (!form) return;

    e.preventDefault();

    // Guarda contra doble envío mientras hay una petición en curso
    if (form.dataset.enviando === '1') return;
    form.dataset.enviando = '1';

    const productoId = form.dataset.productoId;
    const nombre     = form.dataset.nombre;
    const cantidad   = form.querySelector('.cantidad-input')?.value ?? '1';
    const token      = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // ── Feedback táctil INMEDIATO en el botón (no espera al servidor) ──
    const btnAgregar = form.querySelector('.btn-agregar');
    const textoOrig  = btnAgregar.innerHTML;
    btnAgregar.classList.add('btn-agregar--ok');
    btnAgregar.innerHTML = '<i class="bi bi-check-lg me-1"></i>Agregado';

    const restaurarBoton = function () {
        form.dataset.enviando = '0';
        btnAgregar.classList.remove('btn-agregar--ok');
        btnAgregar.innerHTML = textoOrig;
    };

    const body = new URLSearchParams({
        productoId,
        cantidad,
        __RequestVerificationToken: token
    });

    fetch('/Carrito?handler=AgregarAjax', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString()
    })
        .then(function (res) { return res.json(); })
        .then(function (data) {
            if (data.ok) {
                mostrarToast(`✓ "${nombre}" agregado al carrito`, 'success');
                actualizarBadgeCarrito(data.cantidadCarrito);
                // Deja ver el "Agregado" un instante antes de restaurar
                setTimeout(restaurarBoton, 850);
            } else {
                mostrarToast(data.mensaje || 'Error al agregar el producto', 'error');
                restaurarBoton();
            }
        })
        .catch(function () {
            mostrarToast('Error de conexión. Intentá de nuevo.', 'error');
            restaurarBoton();
        });
});


/* =============================================
   TOAST NOTIFICACIÓN
   ============================================= */
function mostrarToast(mensaje, tipo = 'success') {
    const toastEl  = document.getElementById('toastCarrito');
    const msgEl    = document.getElementById('toastMensaje');
    const iconEl   = toastEl.querySelector('.bi');

    if (!toastEl || !msgEl) return;

    msgEl.textContent = mensaje;

    // Ícono y color según tipo
    if (tipo === 'success') {
        iconEl.className = 'bi bi-cart-check-fill fs-5';
        iconEl.style.color = 'var(--verde)';
        toastEl.style.background = 'linear-gradient(135deg, var(--verde-profundo), #0c4727)';
        toastEl.style.color = '#fff';
    } else {
        iconEl.className = 'bi bi-exclamation-circle-fill fs-5';
        iconEl.style.color = '#f0c040';
        toastEl.style.background = 'linear-gradient(135deg, #7b1111, #a01515)';
        toastEl.style.color = '#fff';
    }

    const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { delay: 3000 });
    toast.show();
}


/* =============================================
   ACTUALIZAR BADGE DEL CARRITO EN NAVBAR
   ============================================= */
function actualizarBadgeCarrito(cantidad) {
    let badge = document.querySelector('.badge-carrito');

    if (cantidad > 0) {
        if (!badge) {
            badge = document.createElement('span');
            badge.className = 'badge-carrito';
            // Selector explícito: solo el link del carrito, nunca otro botón
            // que pudiera compartir la clase .btn-carrito por estilo.
            document.querySelector('a.btn-carrito')?.appendChild(badge);
        }
        badge.textContent = cantidad;
        // Reinicia la animación de "pop" para que se dispare en cada alta
        badge.classList.remove('badge-carrito--pop');
        void badge.offsetWidth;
        badge.classList.add('badge-carrito--pop');
    } else if (badge) {
        badge.remove();
    }
}
