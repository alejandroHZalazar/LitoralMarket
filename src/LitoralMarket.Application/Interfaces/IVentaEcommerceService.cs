using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Registra la venta de un pedido confirmado del ecommerce con las mismas reglas que Comercial Web
/// (VentaService.GrabarVentaAsync): ventas + ventasDetalle + stock + productosMovimientos (tipo 3)
/// + pedidoDetalle.procesado + pedidos.vendido + pedidos_ventas.
/// No registra cuenta corriente, cobros, caja, formas de pago ni factura electrónica: el pedido ya está pagado.
/// </summary>
public interface IVentaEcommerceService
{
    /// <summary>ecommerce/registrarVentaAlConfirmar == "1".</summary>
    Task<bool> HabilitadoAsync();

    /// <summary>
    /// Registra la venta del pedido (con sus detalles cargados). Debe llamarse DENTRO de la transacción que
    /// confirma el pedido: incluye el descuento de stock, que reemplaza al que hacía la confirmación.
    /// Si el pedido ya tiene una venta vinculada (pedidos_ventas) no hace nada y devuelve null.
    /// Lanza excepción si no se puede registrar (sin cliente, sin líneas, faltan tablas...):
    /// el llamador revierte la transacción y el pedido no queda confirmado.
    /// </summary>
    Task<long?> RegistrarVentaAsync(Pedido pedido);

    /// <summary>Estado para mostrar al administrador: habilitado, tablas presentes y cliente consumidor final válido.</summary>
    Task<DiagnosticoVentaEcommerce> DiagnosticoAsync();
}

public record DiagnosticoVentaEcommerce(bool Habilitado, bool TablasListas, int? ClienteConsumidorFinal, bool ClienteValido);
