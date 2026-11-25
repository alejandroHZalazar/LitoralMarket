-- ============================================================
-- HABILITAR VARIANTES DE PRODUCTO (medidas / colores)
-- Ejecutar UNA VEZ en cada base. Es seguro volver a correrlo.
--
-- ecommerce/HabilitarVariantesProducto:
--   '1'                           → se pueden administrar productos con variantes
--   '0', NULL, inexistente u otro → solo productos simples (comportamiento actual)
-- Crear las tablas de variantes_01_tablas.sql ANTES de activarlo.
-- ============================================================
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'HabilitarVariantesProducto', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'HabilitarVariantesProducto');

-- Para activar:
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'HabilitarVariantesProducto';
