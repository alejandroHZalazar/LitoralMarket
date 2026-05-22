-- =============================================================
-- Módulo de pagos e-commerce — LitoralMarket
-- =============================================================

-- 1. Tabla de cobros e-commerce
-- =============================================================
CREATE TABLE IF NOT EXISTS cobros_ecommerce (
    id               INT            AUTO_INCREMENT PRIMARY KEY,
    fk_pedido        INT            NOT NULL,
    tipo             VARCHAR(20)    NOT NULL DEFAULT 'reembolso',  -- 'reembolso' | 'mercadopago'
    estado           VARCHAR(20)    NOT NULL DEFAULT 'pendiente',  -- 'pendiente' | 'aprobado' | 'rechazado' | 'cancelado'
    monto            DECIMAL(12,2)  NOT NULL,
    concepto         VARCHAR(200)   NULL,

    -- MercadoPago
    mp_preference_id    VARCHAR(150)   NULL,
    mp_payment_id       VARCHAR(150)   NULL,
    mp_link_pago        VARCHAR(600)   NULL,
    mp_fecha_expiracion DATETIME       NULL,
    mp_status           VARCHAR(50)    NULL,

    -- El PDF del comprobante se genera en memoria bajo demanda (al enviar email)
    -- y no se almacena en disco ni en base de datos.

    fecha_creacion   DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
    fecha_pago       DATETIME       NULL,

    CONSTRAINT fk_cobroe_pedido FOREIGN KEY (fk_pedido) REFERENCES pedidos(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Nuevo estado en pedidos: 'pendiente_pago'
--    (VARCHAR sin ENUM, ya soporta el valor nuevo sin ALTER)

-- 3. Parámetros necesarios
-- =============================================================
INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mercadopago', 'accessToken', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mercadopago' AND parametro='accessToken');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mercadopago', 'publicKey', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mercadopago' AND parametro='publicKey');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mercadopago', 'urlBase', 'https://tudominio.com'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mercadopago' AND parametro='urlBase');

-- Adjuntar PDF del comprobante al email de confirmación (0=no, 1=sí)
-- El PDF se genera en memoria, se adjunta al email y se descarta. No queda en disco ni en BD.
INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'generarPdfPago', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='generarPdfPago');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'metodoPagoReembolso', '1'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='metodoPagoReembolso');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'metodoPagoMercadoPago', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='metodoPagoMercadoPago');

-- 4. Parámetros de configuración de email (SMTP)
-- =============================================================
-- Servidor SMTP
INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'host', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='host');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'port', '587'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='port');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'ssl', '1'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='ssl');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'usuario', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='usuario');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'password', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='password');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'remitente', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='remitente');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'nombreRemitente', 'LitoralMarket'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='nombreRemitente');

-- Email destino del administrador para notificaciones internas
INSERT INTO parametros (modulo, parametro, valor)
SELECT 'mail', 'emailAdmin', ''
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='mail' AND parametro='emailAdmin');

-- Habilitación de envíos
INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'enviarMailConfirmacion', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='enviarMailConfirmacion');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'enviarMailMercadoPago', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='enviarMailMercadoPago');

INSERT INTO parametros (modulo, parametro, valor)
SELECT 'ecommerce', 'enviarMailAdmin', '0'
WHERE NOT EXISTS (SELECT 1 FROM parametros WHERE modulo='ecommerce' AND parametro='enviarMailAdmin');

-- 5. Datos de empresa para el PDF
-- =============================================================
-- Los datos de la empresa ya existen en la tabla parametros bajo el módulo 'empresa':
--   empresa/nombre     → razón social
--   empresa/direccion  → calle y número
--   empresa/localidad  → ciudad y provincia
--   empresa/telefono   → teléfono
--   empresa/mail       → email de contacto
-- No se insertan aquí porque ya están cargados en la BD.
