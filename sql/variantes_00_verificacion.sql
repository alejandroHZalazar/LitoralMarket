-- ============================================================
-- VARIANTES DE PRODUCTO — VERIFICACIÓN PREVIA (SOLO LECTURA)
-- Correr en la base real ANTES de crear nada. No modifica datos (solo SELECT y SHOW).
--
-- Resultado de la corrida en 'ale' (2026-11-13):
--   · MySQL 8.4.7; todas las tablas InnoDB; Productos y derivadas en latin1_swedish_ci.
--   · stockProductos tiene UNIQUE(fk_producto); sin filas múltiples de stock/precio/costo por producto.
--   · Hay muchos codBarras repetidos entre productos activos (marcadores '0', '001'...'005' y varios EAN
--     reales). NO hay repetidos de (codProveedor, fk_proveedor).
--   · 1 producto sin fila en preciosProveedores (de 2066).
--   · pedidoDetalle.fk_color tiene valores históricos 0..4; la tabla colores tiene datos (Neutro, Celeste, Rosa...).
--   · parametros.id es AUTO_INCREMENT. Solo hay disparadores sobre Rubros. 136 procedimientos almacenados.
-- ============================================================
SELECT VERSION() AS version_mysql, DATABASE() AS base;

-- 1. Motor, charset y nombre real (mayúsculas) de las tablas involucradas
SELECT TABLE_NAME, ENGINE, TABLE_COLLATION
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND LOWER(TABLE_NAME) IN ('productos','stockproductos','preciosproductos','costosproductos',
                            'preciosproveedores','imagenesproductos','parametros','colores',
                            'pedidodetalle','ventasdetalle','productosmovimientos',
                            'variantesmedidas','variantescolores','productosprincipales','productosvariantes');

-- 2. Índices existentes (¿hay unicidad en fk_producto, codBarras o codProveedor?)
SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE,
       GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS columnas
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND LOWER(TABLE_NAME) IN ('productos','stockproductos','preciosproductos','costosproductos',
                            'preciosproveedores','imagenesproductos')
GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
ORDER BY TABLE_NAME, INDEX_NAME;

-- 3. Claves foráneas que apuntan a productos (impiden borrar/renumerar productos)
SELECT TABLE_NAME, CONSTRAINT_NAME, REFERENCED_TABLE_NAME
FROM information_schema.REFERENTIAL_CONSTRAINTS
WHERE CONSTRAINT_SCHEMA = DATABASE() AND LOWER(REFERENCED_TABLE_NAME) = 'productos';

-- 4. Disparadores y cantidad de procedimientos (el repositorio no tiene sus cuerpos)
SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_TIMING, EVENT_MANIPULATION
FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE();
SELECT COUNT(*) AS procedimientos FROM information_schema.ROUTINES
WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'PROCEDURE';
-- Para leer el cuerpo de los más importantes (uno por vez):
--   SHOW CREATE PROCEDURE sp_VentasAddVenta;      SHOW CREATE PROCEDURE sp_VentasAddDetalle;
--   SHOW CREATE PROCEDURE sp_ProductosABM;        SHOW CREATE PROCEDURE sp_Productos_AjusteStock;

-- 5. Filas múltiples por producto (rompen los supuestos "una fila por producto")
SELECT 'stock'  AS tabla, fk_producto, COUNT(*) AS filas FROM stockProductos   GROUP BY fk_producto HAVING COUNT(*) > 1 LIMIT 20;
SELECT 'precio' AS tabla, fk_producto, COUNT(*) AS filas FROM preciosProductos GROUP BY fk_producto HAVING COUNT(*) > 1 LIMIT 20;
SELECT 'costo'  AS tabla, fk_producto, COUNT(*) AS filas FROM costosProductos  GROUP BY fk_producto HAVING COUNT(*) > 1 LIMIT 20;

-- 6. Códigos repetidos entre productos activos
SELECT codBarras, COUNT(*) AS veces FROM Productos
WHERE codBarras IS NOT NULL AND codBarras <> '' AND (baja IS NULL OR baja = 0)
GROUP BY codBarras HAVING COUNT(*) > 1 LIMIT 50;
SELECT codProveedor, fk_proveedor, COUNT(*) AS veces FROM Productos
WHERE codProveedor IS NOT NULL AND codProveedor <> '' AND (baja IS NULL OR baja = 0)
GROUP BY codProveedor, fk_proveedor HAVING COUNT(*) > 1 LIMIT 50;

-- 7. ¿Hay productos sin fila de precio de proveedor? (el detalle de Comercial Web hace INNER JOIN)
SELECT COUNT(*) AS productos_sin_precio_proveedor
FROM Productos p LEFT JOIN preciosProveedores pp ON pp.fk_producto = p.id WHERE pp.id IS NULL;

-- 8. Uso real de fk_color y contenido de la tabla colores
SELECT 'pedidoDetalle' AS tabla, fk_color, COUNT(*) AS filas FROM pedidoDetalle GROUP BY fk_color;
SELECT 'productosMovimientos' AS tabla, fk_color, COUNT(*) AS filas FROM productosMovimientos GROUP BY fk_color;
SELECT * FROM colores LIMIT 20;

-- 9. Columnas de parametros / preciosProveedores / Productos
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN ('parametros','preciosproveedores','productos')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

-- 10. Cantidad de productos (dimensiona el catálogo)
SELECT COUNT(*) AS productos_total FROM Productos;
SELECT COUNT(*) AS productos_activos FROM Productos WHERE baja IS NULL OR baja = 0;
