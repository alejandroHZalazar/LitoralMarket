-- ============================================================
-- TRANSFERENCIA DE MERCADO PAGO COMO MEDIO DE PAGO
-- Ejecutar UNA VEZ en cada base que vaya a usarla (la que tiene la cuenta de Mercado Pago conectada).
-- Es seguro volver a correrlo: no duplica tablas ni parámetros.
--
-- NO modifica ninguna tabla existente (cobros_ecommerce queda intacta): crea tablas nuevas.
-- Por eso el código puede desplegarse antes o después de correr este script sin romper el checkout
-- actual; la opción "Transferencia" simplemente no se puede habilitar hasta que exista.
-- ============================================================

-- 1. Datos del cobro de transferencia (1:1 con cobros_ecommerce) ------------------------------
--    monto_activo = monto_transferencia mientras el cobro está PENDIENTE y NULL en cualquier otro
--    caso (lo mantiene la aplicación). El índice UNIQUE hace que la base impida, aun con dos pedidos
--    simultáneos, que dos cobros pendientes compartan el mismo importe (MySQL admite varios NULL).
CREATE TABLE IF NOT EXISTS `cobros_transferencia` (
  `fk_cobro`            INT           NOT NULL,
  `monto_transferencia` DECIMAL(12,2) NOT NULL,
  `vencimiento`         DATETIME      NOT NULL,
  `monto_activo`        DECIMAL(12,2) NULL,
  PRIMARY KEY (`fk_cobro`),
  UNIQUE KEY `UX_cobros_transferencia_monto_activo` (`monto_activo`),
  CONSTRAINT `fk_cobrotr_cobro` FOREIGN KEY (`fk_cobro`) REFERENCES `cobros_ecommerce` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Transferencias entrantes detectadas ------------------------------------------------------
--    mp_payment_id UNIQUE: una misma transferencia nunca se procesa dos veces.
CREATE TABLE IF NOT EXISTS `transferencias_mp` (
  `id`                INT           NOT NULL AUTO_INCREMENT,
  `mp_payment_id`     VARCHAR(64)   NOT NULL,
  `monto`             DECIMAL(12,2) NOT NULL,
  `moneda`            VARCHAR(10)   NULL,
  `fecha_aprobacion`  DATETIME      NULL,
  `fecha_detectada`   DATETIME      NOT NULL,
  `payment_method_id` VARCHAR(50)   NULL,
  `operation_type`    VARCHAR(50)   NULL,
  `payer_id`          VARCHAR(64)   NULL,
  `status_mp`         VARCHAR(50)   NULL,
  `estado`            VARCHAR(20)   NOT NULL DEFAULT 'sin_asignar',  -- sin_asignar | asignada | ambigua | importe_distinto | fuera_de_termino | ignorada
  `fk_cobro`          INT           NULL,
  `motivo`            VARCHAR(500)  NULL,
  `asignada_por`      VARCHAR(10)   NULL,                            -- auto | admin
  `json_mp`           TEXT          NULL,                            -- resumen de auditoría (sin datos personales)
  PRIMARY KEY (`id`),
  UNIQUE KEY `UX_transferencias_mp_payment` (`mp_payment_id`),
  KEY `IX_transferencias_mp_estado` (`estado`, `fecha_aprobacion`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 3. Parámetros -------------------------------------------------------------------------------
--    Solo el valor '1' en metodoPagoTransferenciaMP habilita la opción y el proceso de detección.
--    Se activa recién después de cargar alias o CVU y el titular (Admin > Medios de pago).
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'metodoPagoTransferenciaMP', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'metodoPagoTransferenciaMP');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'ecommerce', 'transferenciaVencimientoHoras', '24'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'ecommerce' AND parametro = 'transferenciaVencimientoHoras');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'mercadopago', 'transferenciaAlias', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'mercadopago' AND parametro = 'transferenciaAlias');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'mercadopago', 'transferenciaCvu', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'mercadopago' AND parametro = 'transferenciaCvu');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'mercadopago', 'transferenciaTitular', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'mercadopago' AND parametro = 'transferenciaTitular');

INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'mercadopago', 'transferenciaPollingSegundos', '90'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'mercadopago' AND parametro = 'transferenciaPollingSegundos');

-- Marca de agua de la última lectura (la mantiene el servicio; no editar a mano).
INSERT INTO `parametros` (id, modulo, parametro, valor)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM parametros), 'mercadopago', 'transferenciaUltimaLectura', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo = 'mercadopago' AND parametro = 'transferenciaUltimaLectura');

-- Para activar (después de cargar alias/CVU y titular desde Admin > Medios de pago):
--   UPDATE parametros SET valor = '1' WHERE modulo = 'ecommerce' AND parametro = 'metodoPagoTransferenciaMP';
