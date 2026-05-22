using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class PdfPagoService : IPdfPagoService
{
    private readonly AppDbContext       _db;
    private readonly IParametrosService _params;
    private readonly IHttpClientFactory _http;

    // Paleta de colores
    private static readonly DeviceRgb ColorVerde   = new(29,  176, 98);
    private static readonly DeviceRgb ColorGris    = new(80,  80,  80);
    private static readonly DeviceRgb ColorGrisCla = new(240, 240, 240);
    private static readonly DeviceRgb ColorBlanco  = new(255, 255, 255);

    public PdfPagoService(AppDbContext db, IParametrosService parametros, IHttpClientFactory http)
    {
        _db     = db;
        _params = parametros;
        _http   = http;
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerarComprobanteAsync(int pedidoId, int cobroId)
    {
        // ── Cargar datos ──────────────────────────────────────────
        var pedido = await _db.Pedidos
            .Include(p => p.Detalles)
            .Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId)
            ?? throw new InvalidOperationException("Pedido no encontrado");

        var cobro = await _db.CobrosEcommerce.FindAsync(cobroId)
            ?? throw new InvalidOperationException("Cobro no encontrado");

        // S() filtra caracteres fuera de WinAnsiEncoding (Helvetica solo soporta Latin-1)
        var empresa   = S(await _params.GetValorAsync("empresa", "nombre")    ?? "LitoralMarket");
        var logoBytes = await _params.GetLogoAsync();
        var direccion = S(await _params.GetValorAsync("empresa", "direccion") ?? string.Empty);
        var localidad = S(await _params.GetValorAsync("empresa", "localidad") ?? string.Empty);
        var telefono  = S(await _params.GetValorAsync("empresa", "telefono")  ?? string.Empty);
        var emailEmp  = S(await _params.GetValorAsync("empresa", "mail")      ?? string.Empty);

        // Dirección completa: calle + localidad
        var direccionCompleta = string.Join(" - ", new[] { direccion, localidad }
            .Where(s => !string.IsNullOrEmpty(s)));

        var nombreCliente = S(pedido.FkCliente.HasValue
            ? (pedido.Cliente?.NombreComercial ?? pedido.NombreCliente ?? "Cliente")
            : (pedido.NombreCliente ?? "Cliente"));
        var emailCliente = S(pedido.FkCliente.HasValue
            ? pedido.Cliente?.Email
            : pedido.EmailCliente);

        // ── Descargar QR si es pago de MercadoPago ────────────────
        byte[]? qrBytes = null;
        if (cobro.Tipo == "mercadopago" && !string.IsNullOrEmpty(cobro.MpLinkPago))
        {
            try
            {
                var qrUrl = "https://api.qrserver.com/v1/create-qr-code/?size=240x240&margin=10&ecc=M&data="
                          + Uri.EscapeDataString(cobro.MpLinkPago);
                qrBytes = await _http.CreateClient().GetByteArrayAsync(qrUrl);
            }
            catch
            {
                // Si falla la descarga del QR, el PDF se genera sin él (no es bloqueante)
                qrBytes = null;
            }
        }

        // ── Generar directamente en MemoryStream ─────────────────
        // SetCloseStream(false) evita que iText cierre el MemoryStream
        // al hacer Dispose del writer/pdfDoc, permitiendo leer los bytes.
        using var ms = new MemoryStream();
        {
            using var writer = new PdfWriter(ms);
            writer.SetCloseStream(false);   // <- clave: no cierra el MemoryStream
            using (var pdfDoc   = new PdfDocument(writer))
            using (var document = new Document(pdfDoc, PageSize.A4))
            {
                document.SetMargins(30, 40, 30, 40);
                var fontBold   = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                // ── HEADER ────────────────────────────────────────────
                var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 2 }))
                    .UseAllAvailableWidth()
                    .SetBorder(Border.NO_BORDER);

                var logoCell = new Cell().SetBorder(Border.NO_BORDER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE);
                if (logoBytes is { Length: > 0 })
                {
                    try
                    {
                        var img = new iText.Layout.Element.Image(ImageDataFactory.Create(logoBytes))
                            .SetMaxWidth(100);
                        logoCell.Add(img);
                    }
                    catch
                    {
                        logoCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(16)
                            .SetFontColor(ColorVerde));
                    }
                }
                else
                {
                    logoCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(16)
                        .SetFontColor(ColorVerde));
                }
                headerTable.AddCell(logoCell);

                var empCell = new Cell().SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT);
                empCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(13)
                    .SetFontColor(ColorVerde));
                if (!string.IsNullOrEmpty(direccionCompleta))
                    empCell.Add(new Paragraph(direccionCompleta).SetFont(fontNormal).SetFontSize(9)
                        .SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(telefono))
                    empCell.Add(new Paragraph($"Tel: {telefono}").SetFont(fontNormal).SetFontSize(9)
                        .SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(emailEmp))
                    empCell.Add(new Paragraph(emailEmp).SetFont(fontNormal).SetFontSize(9)
                        .SetFontColor(ColorGris));
                headerTable.AddCell(empCell);
                document.Add(headerTable);

                var lineaVerde = new SolidLine(2f); lineaVerde.SetColor(ColorVerde);
                document.Add(new LineSeparator(lineaVerde).SetMarginTop(6).SetMarginBottom(10));

                // ── TÍTULO ────────────────────────────────────────────
                document.Add(new Paragraph("COMPROBANTE DE PEDIDO")
                    .SetFont(fontBold).SetFontSize(14).SetFontColor(ColorVerde)
                    .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(4));
                document.Add(new Paragraph(
                    $"Pedido N.° {pedidoId:D6}    |    Fecha: {pedido.Fecha:dd/MM/yyyy HH:mm}")
                    .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris)
                    .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(14));

                // ── DATOS CLIENTE / PAGO ──────────────────────────────
                var infoTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(14);

                var clientCell = new Cell().SetBackgroundColor(ColorGrisCla)
                    .SetPadding(10).SetBorder(Border.NO_BORDER)
                    .SetBorderRadius(new iText.Layout.Properties.BorderRadius(4));
                clientCell.Add(new Paragraph("DATOS DEL CLIENTE")
                    .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris));
                clientCell.Add(new Paragraph(nombreCliente)
                    .SetFont(fontBold).SetFontSize(11).SetMarginTop(3));
                if (!string.IsNullOrEmpty(emailCliente))
                    clientCell.Add(new Paragraph(emailCliente)
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(pedido.TelefonoCliente))
                    clientCell.Add(new Paragraph(S(pedido.TelefonoCliente))
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                infoTable.AddCell(clientCell);

                var pagoLabel = cobro.Tipo == "mercadopago" ? "MercadoPago" : "Pago contra reembolso";
                var pagoCell  = new Cell().SetBackgroundColor(ColorGrisCla)
                    .SetPadding(10).SetBorder(Border.NO_BORDER)
                    .SetBorderRadius(new iText.Layout.Properties.BorderRadius(4));
                pagoCell.Add(new Paragraph("FORMA DE PAGO")
                    .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris));
                pagoCell.Add(new Paragraph(pagoLabel)
                    .SetFont(fontBold).SetFontSize(11).SetMarginTop(3).SetFontColor(ColorVerde));
                if (cobro.Tipo == "mercadopago" && cobro.MpFechaExpiracion.HasValue)
                    pagoCell.Add(new Paragraph(
                        $"Link vence: {cobro.MpFechaExpiracion:dd/MM/yyyy HH:mm}")
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                infoTable.AddCell(pagoCell);
                document.Add(infoTable);

                // ── DIRECCIÓN DE ENTREGA ──────────────────────────────
                if (!string.IsNullOrEmpty(pedido.DireccionEntrega))
                {
                    document.Add(new Paragraph("Dirección de entrega")
                        .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris)
                        .SetMarginBottom(2));
                    document.Add(new Paragraph(S(pedido.DireccionEntrega))
                        .SetFont(fontNormal).SetFontSize(10).SetMarginBottom(12));
                }

                // ── TABLA DE ÍTEMS ────────────────────────────────────
                var itemTable = new Table(
                    UnitValue.CreatePercentArray(new float[] { 5, 1.5f, 2, 2 }))
                    .UseAllAvailableWidth().SetMarginBottom(8);

                foreach (var col in new[] { "Descripción", "Cant.", "Precio unit.", "Subtotal" })
                {
                    itemTable.AddHeaderCell(new Cell().SetBackgroundColor(ColorVerde)
                        .SetPadding(6).SetBorder(Border.NO_BORDER)
                        .Add(new Paragraph(col).SetFont(fontBold).SetFontSize(9)
                            .SetFontColor(ColorBlanco)));
                }

                bool altRow = false;
                foreach (var item in pedido.Detalles)
                {
                    var bg = altRow ? ColorGrisCla : ColorBlanco;
                    altRow = !altRow;
                    itemTable.AddCell(CeldaItem(S(item.Descripcion), fontNormal, bg, TextAlignment.LEFT));
                    itemTable.AddCell(CeldaItem((item.Cantidad ?? 0).ToString("G"), fontNormal, bg, TextAlignment.CENTER));
                    itemTable.AddCell(CeldaItem($"$ {item.PrecioConIva ?? 0:N2}", fontNormal, bg, TextAlignment.RIGHT));
                    itemTable.AddCell(CeldaItem($"$ {item.Subtotal ?? 0:N2}", fontNormal, bg, TextAlignment.RIGHT));
                }
                document.Add(itemTable);

                // ── TOTALES ───────────────────────────────────────────
                var totalTable = new Table(UnitValue.CreatePercentArray(new float[] { 3, 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(20);
                var subtotalProductos = pedido.Detalles.Sum(d => d.Subtotal ?? 0);
                var costoEnvio        = pedido.CostoEnvio ?? 0;
                AgregarFilaTotal(totalTable, "Subtotal productos:", $"$ {subtotalProductos:N2}", fontNormal, false);
                AgregarFilaTotal(totalTable, "Costo de envío:", costoEnvio == 0 ? "Gratis" : $"$ {costoEnvio:N2}", fontNormal, false);
                AgregarFilaTotal(totalTable, "TOTAL:", $"$ {pedido.Total ?? 0:N2}", fontBold, true);
                document.Add(totalTable);

                // ── LINK MERCADOPAGO + QR ─────────────────────────────
                if (cobro.Tipo == "mercadopago" && !string.IsNullOrEmpty(cobro.MpLinkPago))
                {
                    document.Add(new Paragraph("Link de pago MercadoPago:")
                        .SetFont(fontBold).SetFontSize(10).SetFontColor(ColorVerde)
                        .SetMarginBottom(2));
                    document.Add(new Paragraph(S(cobro.MpLinkPago))
                        .SetFont(fontNormal).SetFontSize(9)
                        .SetFontColor(new DeviceRgb(0, 102, 204)).SetMarginBottom(4));
                    document.Add(new Paragraph(
                        $"El link vence el {cobro.MpFechaExpiracion:dd/MM/yyyy 'a las' HH:mm} hs.")
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris)
                        .SetMarginBottom(8));

                    // QR centrado, debajo del link
                    if (qrBytes is { Length: > 0 })
                    {
                        try
                        {
                            var qrImg = new iText.Layout.Element.Image(ImageDataFactory.Create(qrBytes))
                                .SetWidth(140).SetHeight(140)
                                .SetHorizontalAlignment(HorizontalAlignment.CENTER);

                            document.Add(new Paragraph("Escaneá el QR para pagar:")
                                .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris)
                                .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(4));
                            document.Add(qrImg);
                            document.Add(new Paragraph(" ").SetMarginBottom(8));
                        }
                        catch
                        {
                            // Si el QR no se pudo embeber, continuar sin él
                        }
                    }
                }

                // ── OBSERVACIONES ─────────────────────────────────────
                if (!string.IsNullOrEmpty(pedido.Observacion))
                {
                    document.Add(new Paragraph("Observaciones:")
                        .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris)
                        .SetMarginBottom(2));
                    document.Add(new Paragraph(S(pedido.Observacion))
                        .SetFont(fontNormal).SetFontSize(9).SetMarginBottom(12));
                }

                // ── AVISO LEGAL ───────────────────────────────────────
                // Nota: Helvetica usa WinAnsiEncoding; solo se permiten caracteres Latin-1.
                // No usar emojis ni simbolos Unicode extendidos (⚠, etc.)
                var avisoTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(16);
                var avisoCell = new Cell()
                    .SetBackgroundColor(new DeviceRgb(255, 248, 220))
                    .SetBorder(new SolidBorder(new DeviceRgb(200, 160, 0), 1f))
                    .SetBorderRadius(new iText.Layout.Properties.BorderRadius(4))
                    .SetPadding(10);
                avisoCell.Add(new Paragraph("AVISO IMPORTANTE")
                    .SetFont(fontBold).SetFontSize(9)
                    .SetFontColor(new DeviceRgb(130, 80, 0))
                    .SetMarginBottom(4));
                avisoCell.Add(new Paragraph(
                    "Este comprobante NO es valido como factura ni como ticket fiscal. " +
                    "La compra se efectivizara unicamente al recibir el pago correspondiente.")
                    .SetFont(fontNormal).SetFontSize(9)
                    .SetFontColor(new DeviceRgb(100, 60, 0)));
                avisoTable.AddCell(avisoCell);
                document.Add(avisoTable);

                // ── FOOTER ────────────────────────────────────────────
                var lineaGris = new SolidLine(1f); lineaGris.SetColor(ColorGrisCla);
                document.Add(new LineSeparator(lineaGris).SetMarginBottom(6));
                document.Add(new Paragraph(
                    "Gracias por su compra. Este documento es un comprobante de pedido.")
                    .SetFont(fontNormal).SetFontSize(8).SetFontColor(ColorGris)
                    .SetTextAlignment(TextAlignment.CENTER));
            } // document y pdfDoc se cierran aquí, writer hace flush → ms queda abierto
        }     // writer.Dispose() → ms sigue abierto gracias a SetCloseStream(false)

        return ms.ToArray();
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerarComprobanteSinPagoAsync(int pedidoId)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.Detalles)
            .Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId)
            ?? throw new InvalidOperationException("Pedido no encontrado");

        var empresa   = S(await _params.GetValorAsync("empresa", "nombre")    ?? "LitoralMarket");
        var logoBytes = await _params.GetLogoAsync();
        var direccion = S(await _params.GetValorAsync("empresa", "direccion") ?? string.Empty);
        var localidad = S(await _params.GetValorAsync("empresa", "localidad") ?? string.Empty);
        var telefono  = S(await _params.GetValorAsync("empresa", "telefono")  ?? string.Empty);
        var emailEmp  = S(await _params.GetValorAsync("empresa", "mail")      ?? string.Empty);
        var direccionCompleta = string.Join(" - ", new[] { direccion, localidad }
            .Where(s => !string.IsNullOrEmpty(s)));

        var nombreCliente = S(pedido.FkCliente.HasValue
            ? (pedido.Cliente?.NombreComercial ?? pedido.NombreCliente ?? "Cliente")
            : (pedido.NombreCliente ?? "Cliente"));
        var emailCliente = S(pedido.FkCliente.HasValue
            ? pedido.Cliente?.Email
            : pedido.EmailCliente);

        using var ms = new MemoryStream();
        {
            using var writer = new PdfWriter(ms);
            writer.SetCloseStream(false);
            using (var pdfDoc   = new PdfDocument(writer))
            using (var document = new Document(pdfDoc, PageSize.A4))
            {
                document.SetMargins(30, 40, 30, 40);
                var fontBold   = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                // ── HEADER ────────────────────────────────────────────
                var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 2 }))
                    .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);

                var logoCell = new Cell().SetBorder(Border.NO_BORDER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE);
                if (logoBytes is { Length: > 0 })
                {
                    try { logoCell.Add(new iText.Layout.Element.Image(ImageDataFactory.Create(logoBytes)).SetMaxWidth(100)); }
                    catch { logoCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(16).SetFontColor(ColorVerde)); }
                }
                else
                    logoCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(16).SetFontColor(ColorVerde));
                headerTable.AddCell(logoCell);

                var empCell = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);
                empCell.Add(new Paragraph(empresa).SetFont(fontBold).SetFontSize(13).SetFontColor(ColorVerde));
                if (!string.IsNullOrEmpty(direccionCompleta))
                    empCell.Add(new Paragraph(direccionCompleta).SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(telefono))
                    empCell.Add(new Paragraph($"Tel: {telefono}").SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(emailEmp))
                    empCell.Add(new Paragraph(emailEmp).SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                headerTable.AddCell(empCell);
                document.Add(headerTable);

                var lineaVerde = new SolidLine(2f); lineaVerde.SetColor(ColorVerde);
                document.Add(new LineSeparator(lineaVerde).SetMarginTop(6).SetMarginBottom(10));

                // ── TÍTULO ────────────────────────────────────────────
                document.Add(new Paragraph("COMPROBANTE DE PEDIDO")
                    .SetFont(fontBold).SetFontSize(14).SetFontColor(ColorVerde)
                    .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(4));
                document.Add(new Paragraph(
                    $"Pedido N.° {pedidoId:D6}    |    Fecha: {pedido.Fecha:dd/MM/yyyy HH:mm}")
                    .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris)
                    .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(14));

                // ── DATOS DEL CLIENTE (sin columna de pago) ───────────
                var infoTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(14);
                var clientCell = new Cell().SetBackgroundColor(ColorGrisCla)
                    .SetPadding(10).SetBorder(Border.NO_BORDER)
                    .SetBorderRadius(new iText.Layout.Properties.BorderRadius(4));
                clientCell.Add(new Paragraph("DATOS DEL CLIENTE")
                    .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris));
                clientCell.Add(new Paragraph(nombreCliente)
                    .SetFont(fontBold).SetFontSize(11).SetMarginTop(3));
                if (!string.IsNullOrEmpty(emailCliente))
                    clientCell.Add(new Paragraph(emailCliente)
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                if (!string.IsNullOrEmpty(pedido.TelefonoCliente))
                    clientCell.Add(new Paragraph(S(pedido.TelefonoCliente))
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(ColorGris));
                infoTable.AddCell(clientCell);
                document.Add(infoTable);

                // ── DIRECCIÓN DE ENTREGA ──────────────────────────────
                if (!string.IsNullOrEmpty(pedido.DireccionEntrega))
                {
                    document.Add(new Paragraph("Dirección de entrega")
                        .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris).SetMarginBottom(2));
                    document.Add(new Paragraph(S(pedido.DireccionEntrega))
                        .SetFont(fontNormal).SetFontSize(10).SetMarginBottom(12));
                }

                // ── TABLA DE ÍTEMS ────────────────────────────────────
                var itemTable = new Table(UnitValue.CreatePercentArray(new float[] { 5, 1.5f, 2, 2 }))
                    .UseAllAvailableWidth().SetMarginBottom(8);
                foreach (var col in new[] { "Descripción", "Cant.", "Precio unit.", "Subtotal" })
                    itemTable.AddHeaderCell(new Cell().SetBackgroundColor(ColorVerde)
                        .SetPadding(6).SetBorder(Border.NO_BORDER)
                        .Add(new Paragraph(col).SetFont(fontBold).SetFontSize(9).SetFontColor(ColorBlanco)));

                bool altRow = false;
                foreach (var item in pedido.Detalles)
                {
                    var bg = altRow ? ColorGrisCla : ColorBlanco; altRow = !altRow;
                    itemTable.AddCell(CeldaItem(S(item.Descripcion), fontNormal, bg, TextAlignment.LEFT));
                    itemTable.AddCell(CeldaItem((item.Cantidad ?? 0).ToString("G"), fontNormal, bg, TextAlignment.CENTER));
                    itemTable.AddCell(CeldaItem($"$ {item.PrecioConIva ?? 0:N2}", fontNormal, bg, TextAlignment.RIGHT));
                    itemTable.AddCell(CeldaItem($"$ {item.Subtotal ?? 0:N2}", fontNormal, bg, TextAlignment.RIGHT));
                }
                document.Add(itemTable);

                // ── TOTALES ───────────────────────────────────────────
                var totalTable = new Table(UnitValue.CreatePercentArray(new float[] { 3, 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(20);
                var subtotalProductos = pedido.Detalles.Sum(d => d.Subtotal ?? 0);
                var costoEnvio        = pedido.CostoEnvio ?? 0;
                AgregarFilaTotal(totalTable, "Subtotal productos:", $"$ {subtotalProductos:N2}", fontNormal, false);
                AgregarFilaTotal(totalTable, "Costo de envío:", costoEnvio == 0 ? "Gratis" : $"$ {costoEnvio:N2}", fontNormal, false);
                AgregarFilaTotal(totalTable, "TOTAL:", $"$ {pedido.Total ?? 0:N2}", fontBold, true);
                document.Add(totalTable);

                // ── OBSERVACIONES ─────────────────────────────────────
                if (!string.IsNullOrEmpty(pedido.Observacion))
                {
                    document.Add(new Paragraph("Observaciones:")
                        .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorGris).SetMarginBottom(2));
                    document.Add(new Paragraph(S(pedido.Observacion))
                        .SetFont(fontNormal).SetFontSize(9).SetMarginBottom(12));
                }

                // ── FOOTER ────────────────────────────────────────────
                var lineaGris = new SolidLine(1f); lineaGris.SetColor(ColorGrisCla);
                document.Add(new LineSeparator(lineaGris).SetMarginBottom(6));
                document.Add(new Paragraph(
                    "Gracias por su compra. Este documento es un comprobante de pedido.")
                    .SetFont(fontNormal).SetFontSize(8).SetFontColor(ColorGris)
                    .SetTextAlignment(TextAlignment.CENTER));
            }
        }
        return ms.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Elimina caracteres fuera del rango Latin-1 (WinAnsiEncoding de Helvetica).
    /// Cubre: emojis, símbolos Unicode extendidos, etc.
    /// Los caracteres con code point &lt;= 0xFF están todos en Windows-1252.
    /// </summary>
    private static string S(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        var sb = new System.Text.StringBuilder(texto.Length);
        foreach (var c in texto)
            if (c <= 0xFF) sb.Append(c);
        return sb.ToString();
    }

    private static Cell CeldaItem(string texto, PdfFont font, DeviceRgb bg, TextAlignment align) =>
        new Cell()
            .SetBackgroundColor(bg).SetPadding(5).SetBorder(Border.NO_BORDER)
            .SetBorderBottom(new SolidBorder(new DeviceRgb(220, 220, 220), 0.5f))
            .Add(new Paragraph(texto).SetFont(font).SetFontSize(9).SetTextAlignment(align));

    private static void AgregarFilaTotal(Table t, string etiqueta, string valor,
        PdfFont font, bool esTotal)
    {
        var fs   = esTotal ? 12 : 10;
        var bold = esTotal ? PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD) : font;

        t.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetTextAlignment(TextAlignment.RIGHT).SetPaddingRight(8)
            .Add(new Paragraph(etiqueta).SetFont(bold).SetFontSize(fs)
                .SetFontColor(esTotal ? ColorVerde : ColorGris)));

        t.AddCell(new Cell().SetBorder(Border.NO_BORDER)
            .SetTextAlignment(TextAlignment.RIGHT)
            .Add(new Paragraph(valor).SetFont(bold).SetFontSize(fs)
                .SetFontColor(esTotal ? ColorVerde : new DeviceRgb(30, 30, 30))));
    }
}
