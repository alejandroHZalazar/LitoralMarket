using LitoralMarket.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Claves de Data Protection persistidas en la BD: así la cookie de auth y el
    // token antiforgery se encriptan/desencriptan con las MISMAS claves entre
    // reinicios y entre múltiples réplicas (Railway). Sin esto, el login rebota
    // en producción porque cada instancia genera claves efímeras distintas.
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys { get; set; }

    public DbSet<Cliente> Clientes { get; set; }
    public DbSet<Rubro> Rubros { get; set; }
    public DbSet<Producto> Productos { get; set; }
    public DbSet<ImagenProducto> ImagenesProductos { get; set; }
    public DbSet<PromocionCarrusel> PromocionesCarrusel { get; set; }
    public DbSet<PromocionCarruselDestino> PromocionesCarruselDestinos { get; set; }
    public DbSet<ProductoInicio> ProductosInicio { get; set; }
    public DbSet<StockProducto> StockProductos { get; set; }
    public DbSet<PrecioProducto> PreciosProductos { get; set; }
    public DbSet<Parametro> Parametros { get; set; }
    public DbSet<Pedido> Pedidos { get; set; }
    public DbSet<PedidoDetalle> PedidoDetalles { get; set; }
    public DbSet<DireccionEntrega> DireccionesEntrega { get; set; }
    public DbSet<CobroEcommerce>        CobrosEcommerce       { get; set; }
    public DbSet<CobroTransferencia>    CobrosTransferencia   { get; set; }
    public DbSet<TransferenciaMp>       TransferenciasMp      { get; set; }
    // Tablas de ventas del sistema comercial (las escribe también Comercial Web)
    public DbSet<Venta>                 Ventas                { get; set; }
    public DbSet<VentaDetalle>          VentasDetalle         { get; set; }
    public DbSet<PedidoVenta>           PedidosVentas         { get; set; }
    // Variantes de producto (medidas/colores). Solo se consultan con ecommerce/HabilitarVariantesProducto = 1.
    public DbSet<PromocionRegla>          PromocionesReglas          { get; set; }
    public DbSet<PromocionReglaProducto>  PromocionesReglasProductos { get; set; }
    public DbSet<PedidoPromoDetalle>      PedidosPromoDetalle        { get; set; }
    public DbSet<VentaPromoDetalle>       VentasPromoDetalle         { get; set; }
    public DbSet<VarianteMedida>        VariantesMedidas      { get; set; }
    public DbSet<VarianteColor>         VariantesColores      { get; set; }
    public DbSet<ProductoPrincipal>     ProductosPrincipales  { get; set; }
    public DbSet<ProductoVariante>      ProductosVariantes    { get; set; }
    public DbSet<PrecioProveedor>       PreciosProveedores    { get; set; }
    public DbSet<ProductosMovimiento>   ProductosMovimientos  { get; set; }
    public DbSet<CostoProducto>         CostosProductos       { get; set; }
    public DbSet<Usuario>               Usuarios              { get; set; }
    public DbSet<Proveedor>             Proveedores           { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("Clientes");
            e.HasKey(x => x.Id);
            e.Property(x => x.NombreComercial).HasColumnName("nombreComercial");
            e.Property(x => x.RazonSocial).HasColumnName("razonSocial");
            e.Property(x => x.Cuil).HasColumnName("cuil");
            e.Property(x => x.Direccion).HasColumnName("direccion");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Telefono).HasColumnName("telefono");
            e.Property(x => x.Celular).HasColumnName("celular");
            e.Property(x => x.Contacto).HasColumnName("contacto");
            e.Property(x => x.FkCondIva).HasColumnName("fk_condIva");
            e.Property(x => x.FkVendedor).HasColumnName("fk_Vendedor");
            e.Property(x => x.Baja).HasColumnName("baja");
            e.Property(x => x.FkLocalidad).HasColumnName("fk_localidad");
            e.Property(x => x.FkZona).HasColumnName("fk_zona");
            e.Property(x => x.SaldoCuentaCorriente).HasColumnName("SaldoCuentaCorriente");
            e.Property(x => x.PasswordHash).HasColumnName("passwordHash");
            e.Property(x => x.EmailConfirmado).HasColumnName("emailConfirmado");
        });

        modelBuilder.Entity<Rubro>(e =>
        {
            e.ToTable("Rubros");
            e.HasKey(x => x.Id);
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.FkRubroPadre).HasColumnName("fk_rubroPadre");
        });

        modelBuilder.Entity<Producto>(e =>
        {
            e.ToTable("Productos");
            e.HasKey(x => x.Id);
            e.Property(x => x.CodProveedor).HasColumnName("codProveedor");
            e.Property(x => x.CodBarras).HasColumnName("codBarras");
            e.Property(x => x.FkRubro).HasColumnName("fk_Rubro");
            e.Property(x => x.Iva).HasColumnName("iva");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.DescripcionLarga).HasColumnName("descripcionLarga");
            e.Property(x => x.Imagen).HasColumnName("imagen").HasColumnType("longblob");
            e.Property(x => x.FkProveedor).HasColumnName("fk_proveedor");
            e.Property(x => x.Baja).HasColumnName("baja");
            e.Property(x => x.Fraccionado).HasColumnName("fraccionado");
            e.Property(x => x.Dolarizado).HasColumnName("dolarizado");
            e.Property(x => x.EsPromocion).HasColumnName("esPromocion");
            e.Property(x => x.CantidadMinimaVenta).HasColumnName("cantidadMinimaVenta").HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Rubro).WithMany(r => r.Productos).HasForeignKey(x => x.FkRubro);
            e.HasOne(x => x.Stock).WithOne(s => s.Producto).HasForeignKey<StockProducto>(s => s.FkProducto);
            e.HasOne(x => x.Precio).WithOne(p => p.Producto).HasForeignKey<PrecioProducto>(p => p.FkProducto);
            e.HasOne(x => x.Costo).WithOne(c => c.Producto).HasForeignKey<CostoProducto>(c => c.FkProducto);
        });

        modelBuilder.Entity<ImagenProducto>(e =>
        {
            e.ToTable("imagenesProductos");
            e.HasKey(x => x.Id);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Imagen).HasColumnName("imagen").HasColumnType("longblob");
            e.Property(x => x.ContentType).HasColumnName("contentType");
            e.Property(x => x.EsPrincipal).HasColumnName("esPrincipal");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.Baja).HasColumnName("baja");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
            e.HasOne(x => x.Producto).WithMany(p => p.Imagenes).HasForeignKey(x => x.FkProducto);
            e.HasIndex(x => new { x.FkProducto, x.Baja, x.EsPrincipal, x.Orden })
             .HasDatabaseName("IX_imagenesProductos_lookup");
        });

        modelBuilder.Entity<ProductoInicio>(e =>
        {
            e.ToTable("productosInicio");
            e.HasKey(x => x.Id);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
        });

        modelBuilder.Entity<PromocionCarruselDestino>(e =>
        {
            e.ToTable("promocionesCarruselDestinos");
            e.HasKey(x => x.FkPromocionCarrusel);
            e.Property(x => x.FkPromocionCarrusel).HasColumnName("fk_promocionCarrusel").ValueGeneratedNever();
            e.Property(x => x.FkPromocionRegla).HasColumnName("fk_promocionRegla");
        });

        modelBuilder.Entity<PromocionCarrusel>(e =>
        {
            e.ToTable("promocionesCarrusel");
            e.HasKey(x => x.Id);
            e.Property(x => x.Imagen).HasColumnName("imagen").HasColumnType("longblob");
            e.Property(x => x.ContentType).HasColumnName("contentType");
            e.Property(x => x.Titulo).HasColumnName("titulo");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
            e.Property(x => x.FechaModificacion).HasColumnName("fechaModificacion");
        });

        modelBuilder.Entity<StockProducto>(e =>
        {
            e.ToTable("stockProductos");
            e.HasKey(x => x.Id);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Cantidad).HasColumnName("cantidad");
            e.Property(x => x.CantidadMinima).HasColumnName("cantidadMinima");
        });

        modelBuilder.Entity<PrecioProducto>(e =>
        {
            e.ToTable("preciosProductos");
            e.HasKey(x => x.Id);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Precio).HasColumnName("precio");
        });

        modelBuilder.Entity<Parametro>(e =>
        {
            e.ToTable("parametros");
            e.HasKey(x => x.Id);
            e.Property(x => x.Modulo).HasColumnName("modulo");
            e.Property(x => x.ParametroNombre).HasColumnName("parametro");
            e.Property(x => x.Valor).HasColumnName("valor");
            e.Property(x => x.Imagen).HasColumnName("imagen").HasColumnType("longblob");
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("pedidos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Total).HasColumnName("total");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.FkCliente).HasColumnName("fk_cliente");
            e.Property(x => x.Iva).HasColumnName("iva");
            e.Property(x => x.Recargo).HasColumnName("recargo");
            e.Property(x => x.Descuento).HasColumnName("descuento");
            e.Property(x => x.FkVendedor).HasColumnName("fk_vendedor");
            e.Property(x => x.Observacion).HasColumnName("observacion");
            e.Property(x => x.Impreso).HasColumnName("impreso");
            e.Property(x => x.Vendido).HasColumnName("vendido");
            e.Property(x => x.EsEcommerce).HasColumnName("esEcommerce");
            e.Property(x => x.EstadoEcommerce).HasColumnName("estadoEcommerce");
            e.Property(x => x.NombreCliente).HasColumnName("nombreCliente");
            e.Property(x => x.EmailCliente).HasColumnName("emailCliente");
            e.Property(x => x.TelefonoCliente).HasColumnName("telefonoCliente");
            e.Property(x => x.DireccionEntrega).HasColumnName("direccionEntrega");
            e.Property(x => x.GuestToken).HasColumnName("guestToken");
            e.HasOne(x => x.Cliente).WithMany(c => c.Pedidos).HasForeignKey(x => x.FkCliente);
        });

        modelBuilder.Entity<PedidoDetalle>(e =>
        {
            e.ToTable("pedidoDetalle");
            e.HasKey(x => x.Linea);
            e.Property(x => x.Linea).HasColumnName("linea");
            e.Property(x => x.FkPedido).HasColumnName("fk_pedido");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.CodBarras).HasColumnName("codBarras");
            e.Property(x => x.CodProveedor).HasColumnName("codProveedor");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.PrecioSinIva).HasColumnName("precioSinIva");
            e.Property(x => x.Cantidad).HasColumnName("cantidad");
            e.Property(x => x.Subtotal).HasColumnName("subtotal");
            e.Property(x => x.Procesado).HasColumnName("procesado");
            e.Property(x => x.CantEntregada).HasColumnName("cantEntregada");
            e.Property(x => x.PrecioOrig).HasColumnName("precioOrig");
            e.Property(x => x.Costo).HasColumnName("costo");
            e.Property(x => x.PrecioConIva).HasColumnName("precioConIva");
            e.Property(x => x.FkColor).HasColumnName("fk_color");
            e.Property(x => x.Observ).HasColumnName("observ");
            e.Property(x => x.Descuento).HasColumnName("descuento");
            e.Property(x => x.Recargo).HasColumnName("recargo");
            e.Property(x => x.SubtotalSinIva).HasColumnName("subtotalSinIva");
            e.HasOne(x => x.Pedido).WithMany(p => p.Detalles).HasForeignKey(x => x.FkPedido);
            e.HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.FkProducto);
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.Property(x => x.FkDireccionEntrega).HasColumnName("fk_direccionEntrega");
            e.Property(x => x.CostoEnvio).HasColumnName("costoEnvio");
            e.Property(x => x.DireccionEntregaTexto).HasColumnName("direccionEntregaTexto");
        });

        modelBuilder.Entity<DireccionEntrega>(e =>
        {
            e.ToTable("direccionesEntrega");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.TipoEntrega).HasColumnName("tipoEntrega");
            e.Property(x => x.Direccion).HasColumnName("direccion");
            e.Property(x => x.Localidad).HasColumnName("localidad");
            e.Property(x => x.Provincia).HasColumnName("provincia");
            e.Property(x => x.CodigoPostal).HasColumnName("codigoPostal");
            e.Property(x => x.Referencia).HasColumnName("referencia");
            e.Property(x => x.CostoEnvio).HasColumnName("costoEnvio");
            e.Property(x => x.EsGratis).HasColumnName("esGratis");
            e.Property(x => x.PermiteLibre).HasColumnName("permiteLibre");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.EsDefault).HasColumnName("esDefault");
            e.Property(x => x.Orden).HasColumnName("orden");
        });

        modelBuilder.Entity<CobroEcommerce>(e =>
        {
            e.ToTable("cobros_ecommerce");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkPedido).HasColumnName("fk_pedido");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Monto).HasColumnName("monto");
            e.Property(x => x.Concepto).HasColumnName("concepto");
            e.Property(x => x.MpPreferenceId).HasColumnName("mp_preference_id");
            e.Property(x => x.MpPaymentId).HasColumnName("mp_payment_id");
            e.Property(x => x.MpLinkPago).HasColumnName("mp_link_pago");
            e.Property(x => x.MpFechaExpiracion).HasColumnName("mp_fecha_expiracion");
            e.Property(x => x.MpStatus).HasColumnName("mp_status");
            e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion");
            e.Property(x => x.FechaPago).HasColumnName("fecha_pago");
            e.HasOne(x => x.Pedido).WithMany().HasForeignKey(x => x.FkPedido);
        });

        modelBuilder.Entity<CobroTransferencia>(e =>
        {
            e.ToTable("cobros_transferencia");
            e.HasKey(x => x.FkCobro);
            e.Property(x => x.FkCobro).HasColumnName("fk_cobro").ValueGeneratedNever();
            e.Property(x => x.MontoTransferencia).HasColumnName("monto_transferencia");
            e.Property(x => x.Vencimiento).HasColumnName("vencimiento");
            e.Property(x => x.MontoActivo).HasColumnName("monto_activo");
            e.HasIndex(x => x.MontoActivo).IsUnique();
            e.HasOne(x => x.Cobro).WithOne().HasForeignKey<CobroTransferencia>(x => x.FkCobro);
        });

        // ── Ventas (mismo mapeo que ComercialDbContext de Comercial Web) ─────────
        modelBuilder.Entity<Venta>(e =>
        {
            e.ToTable("ventas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.TotalVenta).HasColumnName("totalVenta").HasPrecision(18, 4);
            e.Property(x => x.TotalCosto).HasColumnName("totalCosto").HasPrecision(18, 4);
            e.Property(x => x.FkCliente).HasColumnName("fk_cliente");
            e.Property(x => x.FkCajero).HasColumnName("fk_cajero");
            e.Property(x => x.Iva).HasColumnName("IVA").HasPrecision(18, 4);
            e.Property(x => x.Descuento).HasColumnName("descuento").HasPrecision(18, 4);
            e.Property(x => x.Recargo).HasColumnName("recargo").HasPrecision(18, 4);
            e.Property(x => x.FkVendedor).HasColumnName("fk_vendedor");
            e.Property(x => x.Comision).HasColumnName("comision").HasPrecision(18, 4);
            e.Property(x => x.Impuesto).HasColumnName("impuesto").HasPrecision(18, 4);
        });

        modelBuilder.Entity<VentaDetalle>(e =>
        {
            e.ToTable("ventasDetalle");
            e.HasKey(x => x.Linea);
            e.Property(x => x.Linea).HasColumnName("linea");
            e.Property(x => x.FkVenta).HasColumnName("fk_venta");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.CodBarras).HasColumnName("codBarras");
            e.Property(x => x.CodProveedor).HasColumnName("codProveedor");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.PrecioSinIva).HasColumnName("precioSinIva").HasPrecision(18, 4);
            e.Property(x => x.PrecioConIva).HasColumnName("precioConIva").HasPrecision(18, 4);
            e.Property(x => x.Cantidad).HasColumnName("cantidad").HasPrecision(18, 4);
            e.Property(x => x.Costo).HasColumnName("costo").HasPrecision(18, 4);
            e.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(18, 4);
            e.Property(x => x.Descuento).HasColumnName("descuento").HasPrecision(18, 4);
            e.Property(x => x.Recargo).HasColumnName("recargo").HasPrecision(18, 4);
            e.Property(x => x.SubtotalSinIva).HasColumnName("subtotalSinIVA").HasPrecision(18, 4);
        });

        modelBuilder.Entity<PedidoVenta>(e =>
        {
            e.ToTable("pedidos_ventas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkPedido).HasColumnName("fk_pedido");
            e.Property(x => x.FkVenta).HasColumnName("fk_venta");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.HasIndex(x => new { x.FkPedido, x.FkVenta }).IsUnique();
        });

        // ── Promociones por cantidad / porcentaje (tablas del sistema comercial; solo existen si se corrio su script) ──
        modelBuilder.Entity<PromocionRegla>(e =>
        {
            e.ToTable("promociones_reglas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasMaxLength(100).HasColumnName("nombre");
            e.Property(x => x.Tipo).HasMaxLength(4).HasColumnName("tipo");
            e.Property(x => x.Porcentaje).HasPrecision(5, 2).HasColumnName("porcentaje");
            e.Property(x => x.Lleva).HasColumnName("lleva");
            e.Property(x => x.Paga).HasColumnName("paga");
            e.Property(x => x.AgrupaVariantes).HasColumnName("agrupaVariantes");
            e.Property(x => x.VigenteDesde).HasColumnName("vigenteDesde");
            e.Property(x => x.VigenteHasta).HasColumnName("vigenteHasta");
            e.Property(x => x.Activa).HasColumnName("activa");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
            e.Property(x => x.Usuario).HasMaxLength(50).HasColumnName("usuario");
        });
        modelBuilder.Entity<PromocionReglaProducto>(e =>
        {
            e.ToTable("promociones_reglas_productos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkRegla).HasColumnName("fk_regla");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
        });
        modelBuilder.Entity<PedidoPromoDetalle>(e =>
        {
            e.ToTable("pedidos_promo_detalle");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkPedidoDetalle).HasColumnName("fk_pedidoDetalle");
            e.Property(x => x.FkRegla).HasColumnName("fk_regla");
            e.Property(x => x.Nombre).HasMaxLength(100).HasColumnName("nombre");
            e.Property(x => x.Tipo).HasMaxLength(4).HasColumnName("tipo");
            e.Property(x => x.Porcentaje).HasPrecision(5, 2).HasColumnName("porcentaje");
            e.Property(x => x.Lleva).HasColumnName("lleva");
            e.Property(x => x.Paga).HasColumnName("paga");
            e.Property(x => x.CantBonificada).HasPrecision(18, 4).HasColumnName("cantBonificada");
            e.Property(x => x.Ahorro).HasPrecision(18, 4).HasColumnName("ahorro");
        });
        modelBuilder.Entity<VentaPromoDetalle>(e =>
        {
            e.ToTable("ventas_promo_detalle");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkVentaDetalle).HasColumnName("fk_ventaDetalle");
            e.Property(x => x.FkRegla).HasColumnName("fk_regla");
            e.Property(x => x.Nombre).HasMaxLength(100).HasColumnName("nombre");
            e.Property(x => x.Tipo).HasMaxLength(4).HasColumnName("tipo");
            e.Property(x => x.Porcentaje).HasPrecision(5, 2).HasColumnName("porcentaje");
            e.Property(x => x.Lleva).HasColumnName("lleva");
            e.Property(x => x.Paga).HasColumnName("paga");
            e.Property(x => x.CantBonificada).HasPrecision(18, 4).HasColumnName("cantBonificada");
            e.Property(x => x.Ahorro).HasPrecision(18, 4).HasColumnName("ahorro");
        });

        // ── Variantes de producto ────────────────────────────────────────────────
        modelBuilder.Entity<VarianteMedida>(e =>
        {
            e.ToTable("variantesMedidas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.Activa).HasColumnName("activa");
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<VarianteColor>(e =>
        {
            e.ToTable("variantesColores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.CodigoHex).HasColumnName("codigoHex");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.Activa).HasColumnName("activa");
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<ProductoPrincipal>(e =>
        {
            e.ToTable("productosPrincipales");
            e.HasKey(x => x.FkProducto);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto").ValueGeneratedNever();
            e.Property(x => x.UsaMedidas).HasColumnName("usaMedidas");
            e.Property(x => x.UsaColores).HasColumnName("usaColores");
            e.Property(x => x.PreciosIguales).HasColumnName("preciosIguales");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
            e.HasOne(x => x.Producto).WithOne().HasForeignKey<ProductoPrincipal>(x => x.FkProducto);
            e.HasMany(x => x.Variantes).WithOne(v => v.Principal).HasForeignKey(v => v.FkPrincipal);
        });

        modelBuilder.Entity<ProductoVariante>(e =>
        {
            e.ToTable("productosVariantes");
            e.HasKey(x => x.FkProducto);
            e.Property(x => x.FkProducto).HasColumnName("fk_producto").ValueGeneratedNever();
            e.Property(x => x.FkPrincipal).HasColumnName("fk_principal");
            e.Property(x => x.FkMedida).HasColumnName("fk_medida");
            e.Property(x => x.FkColor).HasColumnName("fk_color");
            e.Property(x => x.Activa).HasColumnName("activa");
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.FechaAlta).HasColumnName("fechaAlta");
            e.HasOne(x => x.Producto).WithOne().HasForeignKey<ProductoVariante>(x => x.FkProducto);
            e.HasIndex(x => new { x.FkPrincipal, x.FkMedida, x.FkColor }).IsUnique();
            e.HasIndex(x => new { x.FkPrincipal, x.Activa });
        });

        modelBuilder.Entity<PrecioProveedor>(e =>
        {
            e.ToTable("preciosProveedores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Precio).HasColumnName("precio");
        });

        modelBuilder.Entity<TransferenciaMp>(e =>
        {
            e.ToTable("transferencias_mp");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MpPaymentId).HasColumnName("mp_payment_id");
            e.Property(x => x.Monto).HasColumnName("monto");
            e.Property(x => x.Moneda).HasColumnName("moneda");
            e.Property(x => x.FechaAprobacion).HasColumnName("fecha_aprobacion");
            e.Property(x => x.FechaDetectada).HasColumnName("fecha_detectada");
            e.Property(x => x.PaymentMethodId).HasColumnName("payment_method_id");
            e.Property(x => x.OperationType).HasColumnName("operation_type");
            e.Property(x => x.PayerId).HasColumnName("payer_id");
            e.Property(x => x.StatusMp).HasColumnName("status_mp");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.FkCobro).HasColumnName("fk_cobro");
            e.Property(x => x.Motivo).HasColumnName("motivo");
            e.Property(x => x.AsignadaPor).HasColumnName("asignada_por");
            e.Property(x => x.JsonMp).HasColumnName("json_mp");
            e.HasIndex(x => x.MpPaymentId).IsUnique();
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Password).HasColumnName("password");
            e.Property(x => x.Tipo).HasColumnName("tipo");
            e.Property(x => x.Baja).HasColumnName("baja");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.PasswordMigrated).HasColumnName("password_migrated");
        });

        modelBuilder.Entity<CostoProducto>(e =>
        {
            e.ToTable("costosProductos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.Costo).HasColumnName("costo");
        });

        modelBuilder.Entity<Proveedor>(e =>
        {
            e.ToTable("Proveedores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.NombreComercial).HasColumnName("nombreComercial");
            e.Property(x => x.Cuil).HasColumnName("cuil");
            e.Property(x => x.Direccion).HasColumnName("direccion");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Telefono).HasColumnName("telefono");
            e.Property(x => x.Celular).HasColumnName("celular");
            e.Property(x => x.Ganancia).HasColumnName("ganancia");
            e.Property(x => x.Descuento).HasColumnName("descuento");
            e.Property(x => x.Baja).HasColumnName("baja");
        });

        modelBuilder.Entity<ProductosMovimiento>(e =>
        {
            e.ToTable("productosMovimientos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FkProducto).HasColumnName("fk_producto");
            e.Property(x => x.TipoMovimiento).HasColumnName("tipoMovimiento");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.StockAnt).HasColumnName("stockAnt");
            e.Property(x => x.StockAct).HasColumnName("stockAct");
            e.Property(x => x.Costo).HasColumnName("costo");
            e.Property(x => x.Venta).HasColumnName("venta");
            e.Property(x => x.Cantidad).HasColumnName("cantidad");
            e.Property(x => x.PrecioProveedor).HasColumnName("precio_Proveedor");
            e.Property(x => x.FechaEntrega).HasColumnName("fechaEntrega");
            e.Property(x => x.NroComprobante).HasColumnName("nroComprobante");
            e.Property(x => x.FkColor).HasColumnName("fk_color");
            e.Property(x => x.FechaMov).HasColumnName("fechaMov");
            e.HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.FkProducto);
        });

        // ── Índices de performance ─────────────────────────────────────────────
        // Pedidos: columnas de filtrado frecuente en MisPedidos, Estadísticas y limpieza
        modelBuilder.Entity<Pedido>()
            .HasIndex(p => p.EstadoEcommerce)
            .HasDatabaseName("IX_pedidos_estadoEcommerce");

        modelBuilder.Entity<Pedido>()
            .HasIndex(p => p.Fecha)
            .HasDatabaseName("IX_pedidos_fecha");

        modelBuilder.Entity<Pedido>()
            .HasIndex(p => p.GuestToken)
            .HasDatabaseName("IX_pedidos_guestToken");

        modelBuilder.Entity<Pedido>()
            .HasIndex(p => p.FkCliente)
            .HasDatabaseName("IX_pedidos_fkCliente");

        // Productos: búsqueda por nombre (LIKE '%termino%')
        modelBuilder.Entity<Producto>()
            .HasIndex(p => p.Descripcion)
            .HasDatabaseName("IX_Productos_descripcion");

        // Catálogo: filtro por rubro + orden por descripción en una sola pasada.
        // El índice compuesto sirve la cláusula WHERE fk_Rubro = X y el ORDER BY
        // descripcion sin filesort. Es el índice clave de la navegación del catálogo.
        modelBuilder.Entity<Producto>()
            .HasIndex(p => new { p.FkRubro, p.Descripcion })
            .HasDatabaseName("IX_Productos_fkRubro_descripcion");

        // Precio 1:1 por producto — evita el full scan en el JOIN de precio.
        modelBuilder.Entity<PrecioProducto>()
            .HasIndex(p => p.FkProducto)
            .HasDatabaseName("IX_precios_fkProducto");

        // Costo 1:1 por producto — evita el full scan en la sincronización del carrito.
        modelBuilder.Entity<CostoProducto>()
            .HasIndex(c => c.FkProducto)
            .HasDatabaseName("IX_costos_fkProducto");

        // Parámetros: lookup por (modulo, parametro) — clave natural de consulta
        modelBuilder.Entity<Parametro>()
            .HasIndex(p => new { p.Modulo, p.ParametroNombre })
            .HasDatabaseName("IX_parametros_modulo_parametro");
    }
}
