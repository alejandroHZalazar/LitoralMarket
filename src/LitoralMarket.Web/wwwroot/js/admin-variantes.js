/*
 * Administración de productos con variantes (medidas y/o colores).
 * Solo se carga con ecommerce/HabilitarVariantesProducto = 1 (y las tablas creadas).
 * Sin dependencias (usa Bootstrap solo para el modal de confirmación de precios).
 *
 * Contrato con la página (Admin/Productos/Index):
 *   AdminVariantes.init({ token })   — una vez
 *   AdminVariantes.reset()           — al abrir "Nuevo producto"
 *   await AdminVariantes.load(id)    — al abrir "Editar" (después de cargar los campos del producto)
 *   await AdminVariantes.preparar(fd)— antes de enviar: valida, pide confirmar precios y agrega variantesJson
 *   AdminVariantes.activo()          — true si el producto se guarda como ficha con variantes
 */
(function () {
    'use strict';

    const $ = id => document.getElementById(id);
    const esc = s => String(s ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const num = v => { const n = parseFloat(String(v ?? '').replace(',', '.')); return isFinite(n) ? n : null; };
    const money = n => (n ?? 0).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const S = {
        token: '',
        activo: false,        // el interruptor "tiene variantes" está encendido
        tiene: false,         // la ficha ya tiene variantes guardadas (no se pueden cambiar las dimensiones)
        usaMedidas: true,
        usaColores: true,
        iguales: true,
        filas: [],            // combinaciones (ver nuevaFila)
        medidas: [],          // maestros activos
        colores: [],
        selM: [],             // ids elegidos
        selC: [],
        comunInicial: null,   // { costo, precio } al abrir una ficha existente
        disponible: false,
        codigoAutomatico: false   // las combinaciones nuevas sin códigos los reciben al guardar
    };

    // ── utilidades de modelo ────────────────────────────────────────────
    function nombreM(id) {
        if (!id) return '';
        return S.medidas.find(m => m.id === id)?.nombre ?? S.filas.find(f => f.medidaId === id)?.medidaNombre ?? ('#' + id);
    }
    function nombreC(id) {
        if (!id) return '';
        return S.colores.find(c => c.id === id)?.nombre ?? S.filas.find(f => f.colorId === id)?.colorNombre ?? ('#' + id);
    }
    function hexC(id) { return S.colores.find(c => c.id === id)?.codigoHex ?? S.filas.find(f => f.colorId === id)?.colorHex ?? null; }
    function ordenM(id) { const i = S.medidas.findIndex(m => m.id === id); return i < 0 ? 9999 : i; }
    function ordenC(id) { const i = S.colores.findIndex(c => c.id === id); return i < 0 ? 9999 : i; }

    function nuevaFila(m, c) {
        return { productoId: null, medidaId: m, colorId: c, habilitada: true, codBarras: '', codProveedor: '',
                 costo: null, precio: null, stock: 0, stockInicial: 0, override: !S.iguales, descripcion: null, sel: false };
    }
    function buscarFila(m, c) { return S.filas.find(f => f.medidaId === m && f.colorId === c); }
    function ordenarFilas() {
        S.filas.sort((a, b) => (ordenM(a.medidaId) - ordenM(b.medidaId)) || (ordenC(a.colorId) - ordenC(b.colorId)));
    }
    function comunes() {
        return { costo: num($('inpPrecioCosto')?.value) ?? 0, precio: num($('inpPrecioLista')?.value) ?? 0 };
    }
    function base() { return (document.querySelector('[name="Producto.Descripcion"]')?.value ?? '').trim(); }
    function descripcionDe(f) {
        const partes = [base()];
        if (S.usaMedidas && f.medidaId) partes.push(nombreM(f.medidaId));
        if (S.usaColores && f.colorId) partes.push(nombreC(f.colorId));
        return partes.join(' - ');
    }

    // ── diseño del formulario según el modo ─────────────────────────────
    function aplicarLayout() {
        const on = S.activo;
        document.querySelectorAll('.solo-simple').forEach(e => e.classList.toggle('d-none', on));
        // Con variantes, el bloque de precios solo se ve si son iguales para todas (costo/precio comunes).
        const sp = $('seccionPrecios');
        if (sp) sp.classList.toggle('d-none', on && !S.iguales);
        const pan = $('variantesPanel');
        if (pan) pan.classList.toggle('d-none', !on);
        const lblComun = $('lblPrecioComun');
        if (lblComun) lblComun.classList.toggle('d-none', !(on && S.iguales));
    }

    // ── render ──────────────────────────────────────────────────────────
    function render() {
        aplicarLayout();
        if (!S.activo) return;
        renderPasos();
        renderChips();
        renderMatriz();
        renderGrilla();
    }

    function renderPasos() {
        const dim = (S.usaMedidas && S.usaColores) ? 'mc' : (S.usaMedidas ? 'm' : 'c');
        document.querySelectorAll('input[name="varDim"]').forEach(r => { r.checked = r.value === dim; r.disabled = S.tiene; });
        $('btnPreciosIguales')?.classList.toggle('active', S.iguales);
        $('btnPreciosDistintos')?.classList.toggle('active', !S.iguales);
        $('varAvisoDim')?.classList.toggle('d-none', !S.tiene);
        $('varCfgM')?.classList.toggle('d-none', !S.usaMedidas);
        $('varCfgC')?.classList.toggle('d-none', !S.usaColores);
        $('varPreciosComunHint')?.classList.toggle('d-none', !S.iguales);
        $('varPreciosGrillaHint')?.classList.toggle('d-none', S.iguales);
    }

    function renderChips() {
        const chipM = S.selM.map(id => `<span class="var-chip">${esc(nombreM(id))}
            <button type="button" class="btn-close btn-close-sm" aria-label="Quitar" data-quitar-m="${id}"></button></span>`).join('');
        const chipC = S.selC.map(id => {
            const hx = hexC(id);
            return `<span class="var-chip">${hx ? `<span class="var-dot" style="background:${esc(hx)}"></span>` : ''}${esc(nombreC(id))}
            <button type="button" class="btn-close btn-close-sm" aria-label="Quitar" data-quitar-c="${id}"></button></span>`;
        }).join('');
        $('varChipsM').innerHTML = chipM || '<span class="text-muted small">Ninguna elegida</span>';
        $('varChipsC').innerHTML = chipC || '<span class="text-muted small">Ninguno elegido</span>';

        const opt = (lista, sel) => '<option value="">+ Agregar…</option>' +
            lista.filter(x => !sel.includes(x.id)).map(x => `<option value="${x.id}">${esc(x.nombre)}</option>`).join('');
        $('varSelM').innerHTML = opt(S.medidas, S.selM);
        $('varSelC').innerHTML = opt(S.colores, S.selC);
    }

    function renderMatriz() {
        const ms = S.usaMedidas ? S.selM : [0];
        const cs = S.usaColores ? S.selC : [0];
        const cont = $('varMatriz');
        if (!ms.length || !cs.length) { cont.innerHTML = '<p class="text-muted small mb-0">Elegí al menos una opción para armar la matriz.</p>'; return; }
        let h = '<div class="table-responsive"><table class="table table-sm table-bordered text-center align-middle mb-0 var-matriz"><thead><tr><th></th>';
        for (const c of cs) h += `<th class="small">${c ? esc(nombreC(c)) : '—'}</th>`;
        h += '</tr></thead><tbody>';
        for (const m of ms) {
            h += `<tr><th class="small text-start">${m ? esc(nombreM(m)) : '—'}</th>`;
            for (const c of cs) {
                const f = buscarFila(m, c);
                h += `<td><input type="checkbox" class="form-check-input" data-celda="${m}|${c}" ${f && f.habilitada ? 'checked' : ''}></td>`;
            }
            h += '</tr>';
        }
        cont.innerHTML = h + '</tbody></table></div>';
    }

    // "extra" (botón y marca de precio propio) va dentro del mismo grupo para que la celda no tenga dos renglones.
    function celdaPrecio(f, i, campo, extra = '') {
        // campo: 'costo' | 'precio'
        const cm = comunes();
        const comun = campo === 'costo' ? cm.costo : cm.precio;
        const propio = f.override || !S.iguales;
        const val = propio ? (f[campo] ?? comun) : comun;
        return `<div class="input-group input-group-sm flex-nowrap">
            <input type="number" step="0.01" min="0" class="form-control var-in-${campo}" data-i="${i}" data-campo="${campo}"
                   value="${val ?? ''}" ${propio ? '' : 'readonly tabindex="-1"'}>${extra}
        </div>`;
    }

    function renderGrilla() {
        ordenarFilas();
        const cont = $('varGrilla');
        if (!S.filas.length) {
            cont.innerHTML = '<p class="text-muted small mb-0">Todavía no hay combinaciones. Elegí medidas y/o colores y tocá “Generar combinaciones”.</p>';
            $('varResumen').textContent = '';
            return;
        }
        const colEstado = S.tiene ? 'Stock' : 'Stock inicial';
        let h = `<table class="table table-sm align-middle mb-0 var-grilla"><thead class="table-light"><tr>
            <th style="width:28px"><input type="checkbox" class="form-check-input" id="varSelTodas" title="Seleccionar todas"></th>
            <th>Habil.</th>${S.usaMedidas ? '<th>Medida</th>' : ''}${S.usaColores ? '<th>Color</th>' : ''}
            <th>Cód. barras</th><th>Cód. proveedor</th><th>Costo</th><th>Precio</th><th>${colEstado}</th></tr></thead><tbody>`;
        S.filas.forEach((f, i) => {
            const auto = S.codigoAutomatico && !f.productoId ? ' placeholder="Vacío = automático"' : '';
            const propioBadge = (S.iguales && f.override)
                ? '<span class="input-group-text text-bg-warning" title="Tiene precio propio">propio</span>' : '';
            const btnPropio = S.iguales
                ? `<button type="button" class="btn btn-outline-secondary" data-propio="${i}" title="${f.override ? 'Volver al precio común' : 'Poner un precio propio'}"><i class="bi ${f.override ? 'bi-arrow-counterclockwise' : 'bi-pencil'}"></i></button>`
                : '';
            h += `<tr class="${f.habilitada ? '' : 'var-off'}">
              <td data-label=""><input type="checkbox" class="form-check-input" data-sel="${i}" ${f.sel ? 'checked' : ''}></td>
              <td data-label="Habilitada"><input type="checkbox" class="form-check-input" data-hab="${i}" ${f.habilitada ? 'checked' : ''}></td>
              ${S.usaMedidas ? `<td data-label="Medida">${esc(nombreM(f.medidaId))}</td>` : ''}
              ${S.usaColores ? `<td data-label="Color">${hexC(f.colorId) ? `<span class="var-dot" style="background:${esc(hexC(f.colorId))}"></span>` : ''}${esc(nombreC(f.colorId))}</td>` : ''}
              <td data-label="Cód. barras"><input class="form-control form-control-sm" maxlength="45" data-i="${i}" data-campo="codBarras" value="${esc(f.codBarras)}" inputmode="numeric"${auto}></td>
              <td data-label="Cód. proveedor"><input class="form-control form-control-sm" maxlength="45" data-i="${i}" data-campo="codProveedor" value="${esc(f.codProveedor)}"${auto}></td>
              <td data-label="Costo">${celdaPrecio(f, i, 'costo')}</td>
              <td data-label="Precio">${celdaPrecio(f, i, 'precio', btnPropio + propioBadge)}</td>
              <td data-label="${colEstado}">${f.productoId
                  ? `<span class="small">${f.stock}</span>`
                  : `<input type="number" step="1" min="0" class="form-control form-control-sm var-in-stock" data-i="${i}" data-campo="stockInicial" value="${f.stockInicial || 0}">`}</td>
            </tr>`;
        });
        cont.innerHTML = h + '</tbody></table>';
        const hab = S.filas.filter(f => f.habilitada).length;
        $('varResumen').textContent = `${S.filas.length} combinación(es), ${hab} habilitada(s).`;
    }

    // ── acciones de estado ──────────────────────────────────────────────
    function setDimension(v) {
        if (S.tiene) return;
        S.usaMedidas = v === 'mc' || v === 'm';
        S.usaColores = v === 'mc' || v === 'c';
        // Cambiar las dimensiones invalida las combinaciones armadas (todas son nuevas acá).
        S.filas = [];
        if (!S.usaMedidas) S.selM = [];
        if (!S.usaColores) S.selC = [];
        render();
    }

    function setIguales(v) {
        if (v === S.iguales) return;
        if (v && S.filas.length && !confirm('Todas las variantes pasarán a usar el costo y el precio comunes (los precios propios se descartan al guardar según lo que confirmes). ¿Seguir?')) return;
        S.iguales = v;
        const cm = comunes();
        S.filas.forEach(f => {
            if (v) { f.override = false; }
            else { f.override = true; if (f.costo == null) f.costo = cm.costo; if (f.precio == null) f.precio = cm.precio; }
        });
        render();
    }

    function agregarAtributo(tipo, id) {
        if (!id) return;
        const lista = tipo === 'm' ? S.selM : S.selC;
        if (!lista.includes(id)) lista.push(id);
        render();
    }

    function quitarAtributo(tipo, id) {
        const lista = tipo === 'm' ? S.selM : S.selC;
        const k = lista.indexOf(id);
        if (k >= 0) lista.splice(k, 1);
        // Existentes: se deshabilitan (no se borran); nuevas: se descartan.
        S.filas = S.filas.filter(f => {
            const coincide = tipo === 'm' ? f.medidaId === id : f.colorId === id;
            if (!coincide) return true;
            if (f.productoId) { f.habilitada = false; return true; }
            return false;
        });
        render();
    }

    function generar() {
        const ms = S.usaMedidas ? S.selM : [0];
        const cs = S.usaColores ? S.selC : [0];
        if (!ms.length || !cs.length) { alert('Elegí al menos una opción en cada dimensión.'); return; }
        let nuevas = 0;
        for (const m of ms) for (const c of cs) {
            const f = buscarFila(m, c);
            if (!f) { S.filas.push(nuevaFila(m, c)); nuevas++; }
        }
        render();
        $('varResumen').textContent += nuevas ? ` Se agregaron ${nuevas} nueva(s).` : ' No faltaba ninguna.';
    }

    async function crearAtributo(tipo) {
        const inp = $(tipo === 'm' ? 'varNuevaM' : 'varNuevaC');
        const nombre = inp.value.trim();
        if (!nombre) return;
        const body = { tipo: tipo === 'c' ? 'color' : 'medida', nombre, hex: tipo === 'c' ? ($('varNuevoHex').value || null) : null };
        const res = await fetch('?handler=VarianteAtributo', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': S.token },
            body: JSON.stringify(body)
        });
        let data = null; try { data = await res.json(); } catch { /* no JSON */ }
        if (!res.ok || !data?.ok) { alert(data?.error ?? 'No se pudo crear.'); return; }
        const item = { id: data.id, nombre, codigoHex: body.hex, activa: true };
        (tipo === 'm' ? S.medidas : S.colores).push(item);
        inp.value = '';
        if (tipo === 'c') $('varNuevoHex').value = '';
        agregarAtributo(tipo, data.id);
    }

    function slug(s) { return String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function generarCodigos() {
        const b = slug(base()).slice(0, 10) || 'PROD';
        let n = 0;
        S.filas.forEach(f => {
            if (f.codProveedor || !f.habilitada) return;
            const partes = [b];
            if (S.usaMedidas && f.medidaId) partes.push(slug(nombreM(f.medidaId)).slice(0, 8));
            if (S.usaColores && f.colorId) partes.push(slug(nombreC(f.colorId)).slice(0, 8));
            f.codProveedor = partes.join('-').slice(0, 45);
            n++;
        });
        renderGrilla();
        $('varResumen').textContent += ` Códigos generados: ${n}.`;
    }

    function objetivo() {
        const marcadas = S.filas.filter(f => f.sel);
        return marcadas.length ? marcadas : S.filas;
    }
    function aplicarMasivo() {
        const c = num($('varBulkCosto').value), p = num($('varBulkPrecio').value), pct = num($('varBulkPct').value),
              st = num($('varBulkStock').value);
        if (c == null && p == null && pct == null && st == null) { alert('Escribí un valor para aplicar.'); return; }
        const cm = comunes();
        objetivo().forEach(f => {
            if (c != null || p != null || pct != null) {
                if (S.iguales && !f.override) { f.override = true; f.costo = cm.costo; f.precio = cm.precio; }
                if (c != null) f.costo = c;
                if (p != null) f.precio = p;
                if (pct != null) f.precio = Math.round(((f.precio ?? cm.precio) * (1 + pct / 100)) * 100) / 100;
            }
            if (st != null && !f.productoId) f.stockInicial = Math.max(0, Math.floor(st));
        });
        ['varBulkCosto', 'varBulkPrecio', 'varBulkPct', 'varBulkStock'].forEach(id => $(id).value = '');
        renderGrilla();
    }

    // ── eventos (delegados) ─────────────────────────────────────────────
    function enlazar() {
        const pan = $('variantesPanel');
        if (!pan || pan.dataset.listo) return;
        pan.dataset.listo = '1';

        document.querySelectorAll('input[name="varDim"]').forEach(r => r.addEventListener('change', () => setDimension(r.value)));
        $('btnPreciosIguales').addEventListener('click', () => setIguales(true));
        $('btnPreciosDistintos').addEventListener('click', () => setIguales(false));
        $('varSelM').addEventListener('change', e => { agregarAtributo('m', parseInt(e.target.value, 10)); });
        $('varSelC').addEventListener('change', e => { agregarAtributo('c', parseInt(e.target.value, 10)); });
        $('varBtnNuevaM').addEventListener('click', () => crearAtributo('m'));
        $('varBtnNuevaC').addEventListener('click', () => crearAtributo('c'));
        $('varBtnGenerar').addEventListener('click', generar);
        $('varBtnCodigos')?.addEventListener('click', generarCodigos);   // no está con códigos automáticos
        $('varBtnAplicar').addEventListener('click', aplicarMasivo);

        pan.addEventListener('click', e => {
            const t = e.target;
            const qm = t.closest('[data-quitar-m]'); if (qm) return quitarAtributo('m', parseInt(qm.dataset.quitarM, 10));
            const qc = t.closest('[data-quitar-c]'); if (qc) return quitarAtributo('c', parseInt(qc.dataset.quitarC, 10));
            const pr = t.closest('[data-propio]');
            if (pr) {
                const f = S.filas[parseInt(pr.dataset.propio, 10)];
                const cm = comunes();
                if (f.override) { f.override = false; f.costo = null; f.precio = null; }
                else { f.override = true; f.costo = cm.costo; f.precio = cm.precio; }
                renderGrilla();
            }
        });
        pan.addEventListener('change', e => {
            const t = e.target;
            if (t.dataset.celda) {
                const [m, c] = t.dataset.celda.split('|').map(Number);
                const f = buscarFila(m, c);
                if (!f) { S.filas.push(nuevaFila(m, c)); }
                else if (!f.productoId) { S.filas.splice(S.filas.indexOf(f), 1); }
                else { f.habilitada = t.checked; }
                renderGrilla(); renderMatriz(); return;
            }
            if (t.dataset.hab != null && t.dataset.hab !== '') {
                S.filas[parseInt(t.dataset.hab, 10)].habilitada = t.checked; renderGrilla(); renderMatriz(); return;
            }
            if (t.dataset.sel != null && t.dataset.sel !== '') { S.filas[parseInt(t.dataset.sel, 10)].sel = t.checked; return; }
            if (t.id === 'varSelTodas') { S.filas.forEach(f => f.sel = t.checked); renderGrilla(); return; }
        });
        pan.addEventListener('input', e => {
            const t = e.target;
            if (t.dataset.i == null || !t.dataset.campo) return;
            const f = S.filas[parseInt(t.dataset.i, 10)];
            const campo = t.dataset.campo;
            if (campo === 'codBarras' || campo === 'codProveedor') f[campo] = t.value;
            else if (campo === 'stockInicial') f.stockInicial = Math.max(0, num(t.value) ?? 0);
            else f[campo] = num(t.value);
        });

        // Si cambia el costo/precio común, las filas que lo siguen muestran el nuevo valor.
        ['inpPrecioCosto', 'inpPrecioLista'].forEach(id => $(id)?.addEventListener('input', () => {
            if (S.activo && S.iguales) renderGrilla();
        }));
        document.querySelector('[name="Producto.Descripcion"]')?.addEventListener('input', () => {
            if (S.activo) renderGrilla();
        });
    }

    // ── interruptor "tiene variantes" ───────────────────────────────────
    function onToggle(chk) {
        S.activo = chk.checked;
        if (S.activo && !S.medidas.length && !S.colores.length) {
            // Sin maestros todavía: se pueden crear en línea desde el panel.
        }
        render();
    }

    // ── confirmación al cambiar el precio común (vista previa) ──────────
    function confirmarPropagacion(vp) {
        return new Promise(resolve => {
            const el = $('modalPropagacion');
            $('propSiguen').textContent = vp.siguen;
            $('propPropias').textContent = vp.conPrecioPropio;
            $('propListaPropias').innerHTML = (vp.propias ?? []).map(d => `<li>${esc(d)}</li>`).join('');
            $('propListaWrap').classList.toggle('d-none', !(vp.propias ?? []).length);
            document.querySelector('input[name="propModo"][value="1"]').checked = true;
            const modal = bootstrap.Modal.getOrCreateInstance(el);
            let resuelto = false;
            const fin = valor => { if (resuelto) return; resuelto = true; resolve(valor); };
            const ok = () => { fin(parseInt(document.querySelector('input[name="propModo"]:checked').value, 10)); modal.hide(); };
            const no = () => fin(null);
            $('btnPropOk').onclick = ok;
            el.addEventListener('hidden.bs.modal', no, { once: true });
            modal.show();
        });
    }

    // ── API pública ─────────────────────────────────────────────────────
    async function preparar(fd) {
        if (!S.activo) return true;
        const errores = [];
        if (!base()) errores.push('Escribí la descripción del producto.');
        if (S.usaMedidas && !S.selM.length && !S.tiene) errores.push('Elegí al menos una medida.');
        if (S.usaColores && !S.selC.length && !S.tiene) errores.push('Elegí al menos un color.');
        if (!S.filas.length) errores.push('Generá al menos una combinación (botón “Generar combinaciones”).');
        if (S.filas.length && !S.filas.some(f => f.habilitada)) errores.push('Habilitá al menos una combinación.');
        if (S.iguales) {
            const cm = comunes();
            if (!(cm.precio > 0)) errores.push('Cargá el precio de venta común.');
        } else {
            S.filas.forEach(f => {
                if (f.habilitada && !(f.precio > 0)) errores.push(`Falta el precio de “${descripcionDe(f)}”.`);
            });
        }
        if (errores.length) { window.mostrarErroresFormulario?.(errores); return false; }

        let modo = 0;
        const cm = comunes();
        const cambioComun = S.tiene && S.iguales && S.comunInicial &&
            (Math.abs(cm.costo - S.comunInicial.costo) > 0.001 || Math.abs(cm.precio - S.comunInicial.precio) > 0.001);
        if (cambioComun) {
            const id = parseInt($('prodId').value, 10) || 0;
            let vp = null;
            try {
                const r = await fetch(`?handler=VariantesVistaPrevia&id=${id}&costo=${cm.costo}&precio=${cm.precio}`);
                const d = await r.json();
                if (d?.ok) vp = d.vp;
            } catch { /* sin vista previa: se guarda sin propagar */ }
            if (vp && (vp.siguen > 0 || vp.conPrecioPropio > 0)) {
                const eleccion = await confirmarPropagacion(vp);
                if (eleccion === null) return false;
                modo = eleccion;
            }
        }

        const req = {
            usaMedidas: S.usaMedidas, usaColores: S.usaColores, preciosIguales: S.iguales, modoPropagacion: modo,
            variantes: S.filas.map((f, i) => ({
                productoId: f.productoId, medidaId: f.medidaId, colorId: f.colorId, habilitada: f.habilitada,
                codBarras: (f.codBarras ?? '').trim() || null, codProveedor: (f.codProveedor ?? '').trim() || null,
                costo: (S.iguales && !f.override) ? null : f.costo,
                precio: (S.iguales && !f.override) ? null : f.precio,
                stockInicial: f.productoId ? 0 : (f.stockInicial || 0),
                orden: i
            }))
        };
        fd.set('variantesJson', JSON.stringify(req));
        return true;
    }

    async function load(id) {
        reset();
        if (!S.disponible || !id) return;
        let d = null;
        try { const r = await fetch(`?handler=VariantesFicha&id=${id}`); d = await r.json(); } catch { return; }
        if (!d?.ok) return;
        S.medidas = d.medidas ?? []; S.colores = d.colores ?? [];
        const f = d.ficha;
        if (f?.tieneVariantes) {
            S.activo = true; S.tiene = true;
            S.usaMedidas = f.usaMedidas; S.usaColores = f.usaColores; S.iguales = f.preciosIguales;
            S.filas = (f.variantes ?? []).map(v => ({
                productoId: v.productoId, medidaId: v.medidaId, colorId: v.colorId, habilitada: v.habilitada,
                medidaNombre: v.medidaNombre, colorNombre: v.colorNombre, colorHex: v.colorHex,
                codBarras: v.codBarras ?? '', codProveedor: v.codProveedor ?? '',
                costo: v.costo, precio: v.precio, stock: v.stock, stockInicial: 0,
                override: v.precioPropio || !f.preciosIguales, descripcion: v.descripcion, sel: false
            }));
            S.selM = [...new Set(S.filas.filter(x => x.medidaId).map(x => x.medidaId))];
            S.selC = [...new Set(S.filas.filter(x => x.colorId).map(x => x.colorId))];
            S.comunInicial = comunes();
            const chk = $('chkTieneVariantes'); chk.checked = true; chk.disabled = true;
        }
        render();
    }

    function reset() {
        S.activo = false; S.tiene = false; S.usaMedidas = true; S.usaColores = true; S.iguales = true;
        S.filas = []; S.selM = []; S.selC = []; S.comunInicial = null;
        const chk = $('chkTieneVariantes');
        if (chk) { chk.checked = false; chk.disabled = false; }
        // Maestros para "Nuevo": se piden una sola vez al abrir el interruptor.
        render();
    }

    async function asegurarMaestros() {
        if (S.medidas.length || S.colores.length) return;
        try {
            const r = await fetch('?handler=VariantesFicha&id=0');
            const d = await r.json();
            if (d?.ok) { S.medidas = d.medidas ?? []; S.colores = d.colores ?? []; }
        } catch { /* se pueden crear en línea */ }
    }

    function init(opts) {
        S.token = opts?.token ?? '';
        S.codigoAutomatico = !!opts?.codigoAutomatico;
        S.disponible = !!$('variantesPanel');
        if (!S.disponible) return;
        enlazar();
        $('chkTieneVariantes').addEventListener('change', async e => {
            if (e.target.checked) await asegurarMaestros();
            onToggle(e.target);
        });
        reset();
    }

    window.AdminVariantes = { init, reset, load, preparar, activo: () => S.activo, _estado: S };
})();
