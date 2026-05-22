-- ============================================================
-- TABLA: direccionesEntrega
-- Direcciones de entrega pre-configuradas para el checkout
-- Incluye costo de envío por opción
-- ============================================================

CREATE TABLE IF NOT EXISTS `direccionesEntrega` (
  `id`           INT            NOT NULL AUTO_INCREMENT,
  `descripcion`  VARCHAR(100)   NOT NULL,            -- Nombre visible: "Retiro en Sede", "Envío a domicilio"
  `tipoEntrega`  VARCHAR(20)    NOT NULL DEFAULT 'retiro',
                                                     -- 'retiro'    → el cliente retira en el punto
                                                     -- 'domicilio' → se entrega en dirección del cliente
                                                     -- 'fijo'      → punto fijo distinto a la sede (depósito, sucursal)
  `direccion`    VARCHAR(200)   DEFAULT NULL,        -- Dirección fija (NULL para tipo 'domicilio' y 'otros')
  `localidad`    VARCHAR(100)   DEFAULT NULL,
  `provincia`    VARCHAR(100)   DEFAULT 'Chaco',
  `codigoPostal` VARCHAR(10)    DEFAULT NULL,
  `referencia`   VARCHAR(255)   DEFAULT NULL,        -- "Portón azul, tocar timbre"
  `costoEnvio`   DECIMAL(18,2)  NOT NULL DEFAULT 0.00,
                                                     -- 0.00  = sin costo (retiro en sede, etc.)
                                                     -- >0.00 = costo fijo que se suma al total del pedido
  `esGratis`     TINYINT(1)     NOT NULL DEFAULT 0,  -- 1 = mostrar etiqueta "GRATIS" aunque costoEnvio sea 0
  `permiteLibre` TINYINT(1)     NOT NULL DEFAULT 0,  -- 1 = habilita campo de texto libre para que el usuario
                                                     --     ingrese su propia dirección (opción "Otros / A domicilio")
  `activo`       TINYINT(1)     NOT NULL DEFAULT 1,
  `esDefault`    TINYINT(1)     NOT NULL DEFAULT 0,
  `orden`        INT            NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_activo_orden` (`activo`, `orden`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- DATOS INICIALES (ajustar según la empresa)
-- ============================================================

INSERT INTO `direccionesEntrega`
  (`descripcion`, `tipoEntrega`, `direccion`, `localidad`, `provincia`,
   `codigoPostal`, `referencia`, `costoEnvio`, `esGratis`, `permiteLibre`,
   `activo`, `esDefault`, `orden`)
VALUES
  -- Retiro sin costo en la sede
  ('Retiro en Sede Central',
   'retiro', 'Av. España 265', 'Barranqueras', 'Chaco', '3503',
   'Lunes a viernes de 8 a 17 hs.',
   0.00, 1, 0, 1, 1, 1),

  -- Punto fijo alternativo con costo 0
  ('Retiro en Depósito',
   'fijo', 'Calle Ficticia 1234', 'Resistencia', 'Chaco', '3500',
   'Portón lateral, coordinar previamente.',
   0.00, 1, 0, 1, 0, 2),

  -- Envío a domicilio dentro de Barranqueras
  ('Envío a domicilio — Barranqueras',
   'domicilio', NULL, 'Barranqueras', 'Chaco', NULL,
   NULL,
   800.00, 0, 1, 1, 0, 3),

  -- Envío a domicilio Resistencia (más caro)
  ('Envío a domicilio — Resistencia',
   'domicilio', NULL, 'Resistencia', 'Chaco', NULL,
   NULL,
   1500.00, 0, 1, 1, 0, 4),

  -- Opción libre: el usuario escribe cualquier dirección
  ('Otro destino / consultar',
   'domicilio', NULL, NULL, NULL, NULL,
   'El costo de envío se confirmará por teléfono o WhatsApp.',
   0.00, 0, 1, 1, 0, 99);

-- ============================================================
-- TAMBIÉN: agregar columna costoEnvio al pedido
-- para registrar el costo elegido por el cliente
-- ============================================================
ALTER TABLE `pedidos`
  ADD COLUMN IF NOT EXISTS `fk_direccionEntrega` INT           DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `costoEnvio`           DECIMAL(18,2) DEFAULT 0.00,
  ADD COLUMN IF NOT EXISTS `direccionEntregaTexto` VARCHAR(300) DEFAULT NULL;
  -- direccionEntregaTexto: se usa cuando permiteLibre=1
  --   o para guardar el texto completo de la dirección elegida

-- ============================================================
-- NOTAS DE IMPLEMENTACIÓN
-- ============================================================
-- 1. costoEnvio en pedidos se suma al total del pedido en el checkout.
-- 2. fk_direccionEntrega guarda la opción elegida (puede ser NULL si es "otro").
-- 3. Cuando permiteLibre = 1, el frontend muestra un <input type="text">
--    y guarda lo ingresado en pedidos.direccionEntregaTexto.
-- 4. El campo esGratis permite mostrar "GRATIS" aunque costoEnvio = 0
--    (diferencia entre "gratis" explícito y "sin cargo definido").
-- 5. Para deshabilitar una opción sin borrarla: UPDATE SET activo = 0.
-- 6. Para cambiar el costo: UPDATE SET costoEnvio = X WHERE id = Y.

-- ============================================================
-- VERIFICACIÓN
-- ============================================================
SELECT
  id,
  descripcion,
  tipoEntrega,
  COALESCE(localidad, '(libre)') AS localidad,
  CONCAT('$', FORMAT(costoEnvio, 2)) AS costo,
  IF(esGratis,      'Sí', 'No') AS gratis,
  IF(permiteLibre,  'Sí', 'No') AS permiteLibre,
  IF(esDefault,     'Sí', 'No') AS porDefecto,
  orden
FROM direccionesEntrega
ORDER BY orden;
