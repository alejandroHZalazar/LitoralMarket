-- ============================================================
-- MIGRACIONES E-COMMERCE LITORALMARKET
-- Ejecutar sobre la base de datos existente
-- ============================================================

-- 1. Tabla Clientes: agregar campos de autenticación
ALTER TABLE `Clientes`
  ADD COLUMN IF NOT EXISTS `passwordHash` VARCHAR(256) NULL,
  ADD COLUMN IF NOT EXISTS `emailConfirmado` TINYINT(1) DEFAULT 0;

-- 2. Tabla Productos: agregar descripción larga e imagen
ALTER TABLE `Productos`
  ADD COLUMN IF NOT EXISTS `descripcionLarga` TEXT NULL,
  ADD COLUMN IF NOT EXISTS `imagen` VARCHAR(255) NULL;

-- 3. Tabla pedidos: agregar campos para e-commerce
ALTER TABLE `pedidos`
  ADD COLUMN IF NOT EXISTS `esEcommerce` TINYINT(1) DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `estadoEcommerce` VARCHAR(20) DEFAULT 'borrador',
  ADD COLUMN IF NOT EXISTS `nombreCliente` VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS `emailCliente` VARCHAR(150) NULL,
  ADD COLUMN IF NOT EXISTS `telefonoCliente` VARCHAR(50) NULL,
  ADD COLUMN IF NOT EXISTS `direccionEntrega` VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS `guestToken` VARCHAR(36) NULL;

-- Índice para búsqueda por guestToken
CREATE INDEX IF NOT EXISTS idx_pedidos_guestToken ON `pedidos` (`guestToken`);
CREATE INDEX IF NOT EXISTS idx_pedidos_ecommerce ON `pedidos` (`esEcommerce`, `estadoEcommerce`);

-- 4. Nuevos parámetros de e-commerce
-- Se insertan solo si no existe el par (modulo, parametro). El id se calcula dinámicamente.
-- NOTA: Cambiar los valores según necesidad antes de ejecutar.

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'modoAcceso', 'publico'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'modoAcceso');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'mostrarSinStock', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'mostrarSinStock');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'titulo', 'LitoralMarket'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'titulo');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'productosPorPagina', '12'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'productosPorPagina');

-- Para actualizar un valor existente: siempre por modulo+parametro, nunca por id
-- UPDATE parametros SET valor = 'credenciales' WHERE modulo = 'ecommerce' AND parametro = 'modoAcceso';
-- UPDATE parametros SET valor = '1'            WHERE modulo = 'ecommerce' AND parametro = 'mostrarSinStock';

-- ============================================================
-- PARA ASIGNAR CONTRASEÑAS A CLIENTES (ejecutar luego desde la app)
-- Los passwords se hashean con PasswordHasher de ASP.NET Core Identity.
-- No se pueden generar directamente desde SQL.
-- Usar el endpoint de administración o la utilidad CLI incluida.
-- ============================================================

-- Verificación post-migración
SELECT
  'Clientes con passwordHash' AS descripcion, COUNT(*) AS cantidad
  FROM Clientes WHERE passwordHash IS NOT NULL
UNION ALL
SELECT
  'Productos con imagen' AS descripcion, COUNT(*) AS cantidad
  FROM Productos WHERE imagen IS NOT NULL
UNION ALL
SELECT
  'Pedidos ecommerce' AS descripcion, COUNT(*) AS cantidad
  FROM pedidos WHERE esEcommerce = 1
UNION ALL
SELECT
  'Parametros ecommerce' AS descripcion, COUNT(*) AS cantidad
  FROM parametros WHERE modulo = 'ecommerce';
