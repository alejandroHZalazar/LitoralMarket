-- ============================================================
-- VARIANTES DE PRODUCTO — TABLAS NUEVAS
-- Ejecutar UNA VEZ en cada base que vaya a usar variantes. Es seguro volver a correrlo.
-- NO modifica ninguna tabla existente. Las variantes se guardan como filas de Productos
-- (con su propio stock/precio/costo); estas tablas solo agrupan y describen.
-- Antes de ejecutar, correr variantes_00_verificacion.sql (solo lectura).
-- ============================================================

-- 1. Maestro de medidas ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `variantesMedidas` (
  `id`     INT         NOT NULL AUTO_INCREMENT,
  `nombre` VARCHAR(40) NOT NULL,
  `orden`  INT         NOT NULL DEFAULT 0,
  `activa` TINYINT(1)  NOT NULL DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `UX_variantesMedidas_nombre` (`nombre`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Maestro de colores (NO reutiliza la tabla 'colores' del sistema de escritorio) ---
CREATE TABLE IF NOT EXISTS `variantesColores` (
  `id`        INT         NOT NULL AUTO_INCREMENT,
  `nombre`    VARCHAR(40) NOT NULL,
  `codigoHex` CHAR(7)     NULL,                 -- ej. #1F2937, solo para mostrar una muestra
  `orden`     INT         NOT NULL DEFAULT 0,
  `activa`    TINYINT(1)  NOT NULL DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `UX_variantesColores_nombre` (`nombre`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 3. Ficha principal: un producto de Productos que agrupa variantes --------------------
--    fk_producto = Productos.id de la ficha. Su costo/precio comunes son sus propias filas
--    de costosProductos / preciosProductos; su stock (siempre 0) no se usa.
CREATE TABLE IF NOT EXISTS `productosPrincipales` (
  `fk_producto`    INT        NOT NULL,
  `usaMedidas`     TINYINT(1) NOT NULL DEFAULT 0,
  `usaColores`     TINYINT(1) NOT NULL DEFAULT 0,
  `preciosIguales` TINYINT(1) NOT NULL DEFAULT 1,   -- 1 = costo y precio comunes a todas las variantes
  `fechaAlta`      DATETIME   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`fk_producto`),
  CONSTRAINT `CK_productosPrincipales_dimension` CHECK (`usaMedidas` = 1 OR `usaColores` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 4. Variantes: cada una ES una fila de Productos (fk_producto) enlazada a su ficha -----
--    fk_medida / fk_color = 0 significa "esta ficha no usa esa dimensión" (misma convención
--    que el fk_color = 0 existente) y permite que la restricción única funcione (los NULL
--    no chocan entre sí en MySQL).
CREATE TABLE IF NOT EXISTS `productosVariantes` (
  `fk_producto`  INT        NOT NULL,               -- Productos.id de la variante (tiene su propio stock/precio/costo)
  `fk_principal` INT        NOT NULL,               -- productosPrincipales.fk_producto
  `fk_medida`    INT        NOT NULL DEFAULT 0,
  `fk_color`     INT        NOT NULL DEFAULT 0,
  `activa`       TINYINT(1) NOT NULL DEFAULT 1,     -- 0 = combinación deshabilitada (además Productos.baja = 1)
  `orden`        INT        NOT NULL DEFAULT 0,
  `fechaAlta`    DATETIME   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`fk_producto`),
  UNIQUE KEY `UX_productosVariantes_combinacion` (`fk_principal`, `fk_medida`, `fk_color`),
  KEY `IX_productosVariantes_principal` (`fk_principal`, `activa`),
  KEY `IX_productosVariantes_medida` (`fk_medida`),
  KEY `IX_productosVariantes_color` (`fk_color`),
  CONSTRAINT `FK_productosVariantes_principal` FOREIGN KEY (`fk_principal`)
    REFERENCES `productosPrincipales` (`fk_producto`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 5. OPCIONAL: claves foráneas hacia Productos ----------------------------------------
--    La verificación en 'ale' mostró que Productos es InnoDB con id INT y que ya existen otras
--    claves hacia esa tabla (imagenesProductos, promociones), así que se pueden crear.
--    No son necesarias para que la función ande.
-- ALTER TABLE `productosPrincipales` ADD CONSTRAINT `FK_principales_producto` FOREIGN KEY (`fk_producto`) REFERENCES `Productos` (`id`);
-- ALTER TABLE `productosVariantes`   ADD CONSTRAINT `FK_variantes_producto`   FOREIGN KEY (`fk_producto`) REFERENCES `Productos` (`id`);

-- 6. OPCIONAL: acelera la búsqueda por lector de códigos con muchas variantes ----------
--    (en 'ale' no existe índice sobre Productos.codBarras; verificar con SHOW INDEX FROM Productos)
-- CREATE INDEX `IX_Productos_codBarras` ON `Productos` (`codBarras`);
