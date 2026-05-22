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
   AGREGAR AL CARRITO — AJAX
   ============================================= */
document.addEventListener('submit', async function (e) {
    const form = e.target.closest('[data-agregar-form]');
    if (!form) return;

    e.preventDefault();

    const productoId = form.dataset.productoId;
    const nombre     = form.dataset.nombre;
    const cantidad   = form.querySelector('.cantidad-input')?.value ?? '1';
    const token      = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Estado de carga en el botón
    const btnAgregar = form.querySelector('.btn-agregar');
    const textoOrig  = btnAgregar.innerHTML;
    btnAgregar.disabled = true;
    btnAgregar.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Agregando…';

    try {
        const body = new URLSearchParams({
            productoId,
            cantidad,
            __RequestVerificationToken: token
        });

        const res = await fetch('/Carrito?handler=AgregarAjax', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        });

        const data = await res.json();

        if (data.ok) {
            mostrarToast(`✓ "${nombre}" agregado al carrito`, 'success');
            actualizarBadgeCarrito(data.cantidadCarrito);
        } else {
            mostrarToast(data.mensaje || 'Error al agregar el producto', 'error');
        }
    } catch (err) {
        mostrarToast('Error de conexión. Intentá de nuevo.', 'error');
    } finally {
        btnAgregar.disabled = false;
        btnAgregar.innerHTML = textoOrig;
    }
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
            document.querySelector('.btn-carrito')?.appendChild(badge);
        }
        badge.textContent = cantidad;
    } else if (badge) {
        badge.remove();
    }
}
