-- ============================================================
-- REGISTRO DE VENTA AL CONFIRMAR UN PEDIDO DEL ECOMMERCE
-- Ejecutar UNA VEZ en cada base que lo vaya a usar. Es seguro volver a correrlo.
--
-- Cuando ecommerce/registrarVentaAlConfirmar = '1', al confirmarse un pedido por pago
-- (QR/link de Mercado Pago, transferencia o confirmación del admin) el ecommerce registra la venta
-- igual que Comercial Web: ventas + ventasDetalle + stock + productosMovimientos (tipo 3)
-- + pedidoDetalle.procesado + pedidos.vendido = 1 + pedidos_ventas.
-- Con cualquier otro valor (o sin el parámetro) todo sigue como hasta ahora.
--
-- Los invitados (pedido sin cliente) usan el parámetro EXISTENTE ventas/clienteConsumidorFinal.
-- ============================================================

-- 1. Vínculo pedido ↔ venta. En las bases de Comercial Web ya existe; si falta, se crea con la
--    misma definición que usa Comercial Web (no modifica nada si ya está).
CREATE TABLE IF NOT EXISTS `pedidos_ventas` (
  `id`        INT(11)  NOT NULL AUTO_INCREMENT,
  `fk_pedido` INT(11)  NOT NULL,
  `fk_venta`  BIGINT   NOT NULL,
  `fecha`     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_pedido_venta` (`fk_pedido`, `fk_venta`),
  KEY `idx_pv_venta` (`fk_venta`)
);

-- 2. Parámetros
-- Interruptor (arranca apagado).
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'registrarVentaAlConfirmar', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'registrarVentaAlConfirmar');

-- Opcional: id de usuarios que figura como cajero de las ventas del ecommerce (vacío = sin cajero).
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'cajeroVentaEcommerce', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'cajeroVentaEcommerce');

-- Verificar antes de activar que el cliente consumidor final existe:
--   SELECT valor FROM parametros WHERE modulo = 'ventas' AND parametro = 'clienteConsumidorFinal';
--
-- Para activar:
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'registrarVentaAlConfirmar';
