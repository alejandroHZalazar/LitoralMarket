-- ============================================================
-- LIMITAR LA CANTIDAD PEDIDA AL STOCK DISPONIBLE
-- Ejecutar UNA VEZ en cada base (ale, pañalera, SerNatural, Core...).
-- Es seguro volver a correrlo: no duplica el parámetro.
--
-- ecommerce/LimitarCantidadPorStock:
--   '1'                           → no se puede pedir más cantidad que el stock disponible
--                                   (catálogo, detalle, carrito y confirmación del pedido)
--   '0', NULL, inexistente u otro → sin límite (comportamiento actual: con stock > 0 se puede pedir cualquier cantidad)
-- ============================================================
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'LimitarCantidadPorStock', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'LimitarCantidadPorStock');

-- Para activar:
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'LimitarCantidadPorStock';
