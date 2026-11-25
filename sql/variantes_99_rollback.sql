-- ============================================================
-- VARIANTES DE PRODUCTO — DESHACER (NO ejecutar salvo que se quiera quitar la función)
-- Desactiva el parámetro y borra las 4 tablas nuevas. Las filas de Productos creadas como
-- variantes y fichas NO se borran: quedan como productos comunes (darlas de baja desde el
-- sistema si ya no se quieren). Cada variante conserva su stock, precio y costo.
-- ============================================================
UPDATE parametros SET valor = '0' WHERE modulo = 'ecommerce' AND parametro = 'HabilitarVariantesProducto';
DROP TABLE IF EXISTS `productosVariantes`;
DROP TABLE IF EXISTS `productosPrincipales`;
DROP TABLE IF EXISTS `variantesColores`;
DROP TABLE IF EXISTS `variantesMedidas`;
DELETE FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'HabilitarVariantesProducto';
