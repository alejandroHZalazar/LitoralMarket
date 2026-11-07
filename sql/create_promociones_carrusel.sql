-- ============================================================
-- CARRUSEL DE PROMOCIONES (página de inicio)
-- Ejecutar UNA VEZ en cada base (ale, pañalera, SerNatural, Core...).
-- Es seguro volver a correrlo: no duplica la tabla ni los parámetros.
-- ============================================================

-- 1. Tabla de imágenes del carrusel (el blob se sirve bajo demanda por URL; nunca en listados)
CREATE TABLE IF NOT EXISTS `promocionesCarrusel` (
  `id`                INT          NOT NULL AUTO_INCREMENT,
  `imagen`            LONGBLOB     NOT NULL,
  `contentType`       VARCHAR(50)  NULL,
  `titulo`            VARCHAR(150) NULL,
  `orden`             INT          NOT NULL DEFAULT 0,
  `fechaAlta`         DATETIME     NULL,
  `fechaModificacion` DATETIME     NULL,
  PRIMARY KEY (`id`),
  KEY `IX_promocionesCarrusel_orden` (`orden`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Parámetro de activación. Valor inicial 0 = DESACTIVADO. Solo el valor '1' muestra el carrusel.
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'CarruselPromociones', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'CarruselPromociones');

-- 3. Intervalo del avance automático del carrusel, en SEGUNDOS (entero entre 2 y 60).
--    Si el parámetro no existe, es NULL o no es un entero válido en ese rango, el sitio usa 5.
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'IntervaloCarruselPromociones', '5'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'IntervaloCarruselPromociones');

-- Para activarlo cuando las promociones estén cargadas:
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'CarruselPromociones';
