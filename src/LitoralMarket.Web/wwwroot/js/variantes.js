/*
 * Detalle de producto con variantes (medida y/o color).
 * Todas las variantes viajan en la página (#variantesData): al elegir opciones se actualizan precio, stock y
 * tope de cantidad SIN consultar al servidor. "Agregar" envía el id de la variante al mismo endpoint de siempre.
 * Solo se carga si el producto es una ficha (ecommerce/HabilitarVariantesProducto = 1).
 */
(function () {
    'use strict';
    const dataEl = document.getElementById('variantesData');
    if (!dataEl) return;

    let data;
    try { data = JSON.parse(dataEl.textContent); } catch { return; }

    const $ = id => document.getElementById(id);
    const raiz = $('varDetalle');
    const verStock = raiz?.dataset.verStock === '1';
    const verCod = raiz?.dataset.verCod === '1';
    const vs = data.variantes || [];
    const fmt = n => n.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const fmtCant = n => Number.isInteger(n) ? String(n) : n.toFixed(2).replace(/\.?0+$/, '');
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    const sel = { m: 0, c: 0 };           // 0 = sin elegir
    const usaM = !!data.usaMedidas, usaC = !!data.usaColores;

    // Opciones distintas, en el orden en que llegan (el servidor ya las ordena por medida y color).
    const medidas = [], colores = [];
    vs.forEach(v => {
        if (usaM && v.medidaId && !medidas.some(x => x.id === v.medidaId)) medidas.push({ id: v.medidaId, nombre: v.medida });
        if (usaC && v.colorId && !colores.some(x => x.id === v.colorId)) colores.push({ id: v.colorId, nombre: v.color, hex: v.colorHex });
    });

    const coincide = v => (!usaM || !sel.m || v.medidaId === sel.m) && (!usaC || !sel.c || v.colorId === sel.c);
    const actual = () => {
        if ((usaM && !sel.m) || (usaC && !sel.c)) return null;
        return vs.find(v => (!usaM || v.medidaId === sel.m) && (!usaC || v.colorId === sel.c)) ?? null;
    };

    // Una opción se puede elegir si existe alguna variante con stock que la tenga y sea compatible con la otra dimensión elegida.
    function disponible(dim, id) {
        return vs.some(v => v.stock > 0
            && (dim === 'm' ? v.medidaId === id : (!usaM || !sel.m || v.medidaId === sel.m))
            && (dim === 'c' ? v.colorId === id : (!usaC || !sel.c || v.colorId === sel.c)));
    }
    function existe(dim, id) {
        return vs.some(v => (dim === 'm' ? v.medidaId === id : (!usaM || !sel.m || v.medidaId === sel.m))
                         && (dim === 'c' ? v.colorId === id : (!usaC || !sel.c || v.colorId === sel.c)));
    }

    function renderOpciones() {
        if (usaM) {
            $('varOpcionesMedida').innerHTML = medidas.map(o => {
                const ok = disponible('m', o.id), ex = existe('m', o.id);
                return `<button type="button" class="btn btn-sm ${sel.m === o.id ? 'btn-primary' : 'btn-outline-secondary'} var-op ${ok ? '' : 'var-op-off'}"
                    data-dim="m" data-id="${o.id}" aria-pressed="${sel.m === o.id}" ${ok || sel.m === o.id ? '' : 'disabled'}
                    title="${ok ? '' : (ex ? 'Sin stock' : 'No disponible en este color')}">${esc(o.nombre)}</button>`;
            }).join('');
            $('varMedidaSel').textContent = sel.m ? (medidas.find(x => x.id === sel.m)?.nombre ?? '') : 'elegí una';
        }
        if (usaC) {
            $('varOpcionesColor').innerHTML = colores.map(o => {
                const ok = disponible('c', o.id), ex = existe('c', o.id);
                return `<button type="button" class="btn btn-sm ${sel.c === o.id ? 'btn-primary' : 'btn-outline-secondary'} var-op ${ok ? '' : 'var-op-off'}"
                    data-dim="c" data-id="${o.id}" aria-pressed="${sel.c === o.id}" ${ok || sel.c === o.id ? '' : 'disabled'}
                    title="${ok ? '' : (ex ? 'Sin stock' : 'No disponible en esta medida')}">${o.hex ? `<span class="var-dot" style="background:${esc(o.hex)}"></span>` : ''}${esc(o.nombre)}</button>`;
            }).join('');
            $('varColorSel').textContent = sel.c ? (colores.find(x => x.id === sel.c)?.nombre ?? '') : 'elegí uno';
        }
    }

    function renderEstado() {
        const v = actual();
        const precio = $('varPrecio');
        const btn = $('varBtnAgregar'), txt = $('varBtnTexto'), hid = $('varProductoId'), info = $('varInfo');
        const inp = document.querySelector('#varForm .cantidad-input');

        if (!v) {
            if (precio) precio.innerHTML = `<small class="fs-6 fw-normal text-secondary">Desde</small> $${precio.dataset.desde}`;
            const promoOff = $('varPromo'); if (promoOff) promoOff.classList.add('d-none');
            if (hid) hid.value = '';
            if (btn) { btn.disabled = true; if (txt) txt.textContent = 'Elegí una opción'; }
            if (info) info.textContent = '';
            return;
        }

        // Promociones por cantidad / porcentaje (promociones/descuentos = 1): precio tachado + promocional y texto NxM.
        if (precio) {
            if (v.precioPromo != null) precio.innerHTML = `<span class="producto-precio-tachado fs-6">$${fmt(v.precio)}</span> <span class="badge badge-promo-estatico fs-6 align-middle">-${v.promoPorcentaje}%</span> $${fmt(v.precioPromo)}`;
            else precio.textContent = '$' + fmt(v.precio);
        }
        const promo = $('varPromo');
        if (promo) {
            if (v.promoLleva) { promo.textContent = `Promo ${v.promoLleva}x${v.promoPaga}: llevando ${v.promoLleva} pagás ${v.promoPaga}` + (v.promoAgrupa ? ' (se suman las variantes de este producto)' : ''); promo.classList.remove('d-none'); }
            else promo.classList.add('d-none');
        }
        const partes = [];
        if (verStock) partes.push(v.stock > 0 ? `Stock: ${fmtCant(v.stock)}` : 'Agotado');
        if (verCod && v.codProveedor) partes.push(`Cód.: ${esc(v.codProveedor)}`);
        if (info) info.innerHTML = partes.join(' · ');

        const insuficiente = v.maximo != null && v.maximo < data.unidadMinima;
        const puede = v.stock > 0 && !insuficiente;
        if (hid) hid.value = puede ? String(v.id) : '';
        if (btn) {
            btn.disabled = !puede;
            if (txt) txt.textContent = v.stock <= 0 ? 'Agotado' : (insuficiente ? 'Stock insuficiente' : 'Agregar al carrito');
        }

        // Tope por stock (solo con ecommerce/LimitarCantidadPorStock = 1; sin maximo no se emite max).
        if (inp) {
            if (v.maximo != null && v.maximo > 0) inp.max = String(v.maximo); else inp.removeAttribute('max');
            const max = inp.max ? parseFloat(inp.max) : Infinity;
            if (parseFloat(inp.value) > max) { inp.value = String(max); }
        }
    }

    function elegir(dim, id) {
        const k = dim === 'm' ? 'm' : 'c';
        sel[k] = sel[k] === id ? 0 : id;     // tocar de nuevo desmarca
        // Si la combinación quedó imposible (sin stock o inexistente), se suelta la otra dimensión.
        const otra = k === 'm' ? 'c' : 'm';
        if (sel.m && sel.c && !vs.some(v => v.medidaId === sel.m && v.colorId === sel.c && v.stock > 0)) sel[otra] = 0;
        // Una sola dimensión: queda elegida la variante; con ambas, falta la otra.
        renderOpciones();
        renderEstado();
    }

    raiz.addEventListener('click', e => {
        const b = e.target.closest('.var-op');
        if (!b || b.disabled) return;
        elegir(b.dataset.dim, parseInt(b.dataset.id, 10));
    });

    // Enlace directo a una variante (?v=): se preselecciona. Si es la única opción con stock, también.
    const ini = vs.find(v => v.id === data.inicial);
    if (ini) { sel.m = usaM ? ini.medidaId : 0; sel.c = usaC ? ini.colorId : 0; }
    else {
        const conStock = vs.filter(v => v.stock > 0);
        if (conStock.length === 1) { sel.m = usaM ? conStock[0].medidaId : 0; sel.c = usaC ? conStock[0].colorId : 0; }
    }
    renderOpciones();
    renderEstado();
})();
