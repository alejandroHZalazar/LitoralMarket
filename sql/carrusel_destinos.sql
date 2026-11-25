-- ============================================================
-- CARRUSEL DE PROMOCIONES: DESTINO DE CADA BANNER
-- Cada banner puede llevar a los productos de una promocion por cantidad / porcentaje
-- (promociones_reglas.id, las del modulo "Promociones por cantidad / descuento").
-- Ejecutar UNA VEZ en cada base que use el carrusel. Es seguro volver a correrlo.
-- NO modifica ninguna tabla existente. Mientras no se ejecute, el carrusel y su ABM funcionan como siempre.
-- ============================================================

CREATE TABLE IF NOT EXISTS `promocionesCarruselDestinos` (
  `fk_promocionCarrusel` INT NOT NULL,          -- promocionesCarrusel.id (un destino por banner)
  `fk_promocionRegla`    INT NOT NULL,          -- promociones_reglas.id (sin FK: la regla puede eliminarse; se informa al cliente)
  PRIMARY KEY (`fk_promocionCarrusel`),
  KEY `IX_promocionesCarruselDestinos_regla` (`fk_promocionRegla`),
  CONSTRAINT `FK_carruselDestinos_banner` FOREIGN KEY (`fk_promocionCarrusel`)
    REFERENCES `promocionesCarrusel` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Para deshacer (no borra banners ni promociones, solo los enlaces):
--   DROP TABLE IF EXISTS `promocionesCarruselDestinos`;
