-- ============================================================
-- MOSTRAR EL STOCK EN LAS TARJETAS DEL CATÁLOGO
-- Ejecutar UNA VEZ en cada base (ale, pañalera, SerNatural, Core...).
-- Es seguro volver a correrlo: no duplica el parámetro.
--
-- ecommerce/MostrarStockEnCatalogo:
--   '1'                           → las tarjetas muestran el stock disponible (catálogo, búsqueda e inicio)
--   '0', NULL, inexistente u otro → no se muestra (comportamiento actual)
-- ============================================================
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'MostrarStockEnCatalogo', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'MostrarStockEnCatalogo');

-- Para activar:
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'MostrarStockEnCatalogo';
