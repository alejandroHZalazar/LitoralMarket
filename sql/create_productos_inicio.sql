-- ============================================================
-- PRODUCTOS DE LA PÁGINA DE INICIO
-- Ejecutar UNA VEZ en cada base (ale, pañalera, SerNatural, Core...).
-- Es seguro volver a correrlo: no duplica la tabla ni el parámetro.
-- ============================================================

-- 1. Parámetro. Valor inicial 0 = el Inicio muestra automáticamente los últimos 20 productos
--    (Productos.id descendente, sin bajas y con el criterio de stock actual).
--    Con valor 1 el administrador elige los productos (hasta 20) desde Admin > Productos del Inicio.
--    Solo el valor '1' activa la selección manual (no existe / NULL / 0 / otro valor = automático).
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'ProductosInicioConfigurables', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'ProductosInicioConfigurables');

-- 2. Tabla de la selección manual. Solo se usa con el parámetro en 1; con 0 puede no existir
--    (el Inicio sigue funcionando). fk_producto es único: un producto no se repite. orden = posición.
CREATE TABLE IF NOT EXISTS `productosInicio` (
  `id`          INT      NOT NULL AUTO_INCREMENT,
  `fk_producto` INT      NOT NULL,
  `orden`       INT      NOT NULL DEFAULT 0,
  `fechaAlta`   DATETIME NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `UX_productosInicio_producto` (`fk_producto`),
  KEY `IX_productosInicio_orden` (`orden`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Para activar la selección manual (después de crear la tabla):
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'ProductosInicioConfigurables';
