using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class LogoModel : PageModel
{
    private readonly IParametrosService _parametros;

    public LogoModel(IParametrosService parametros) => _parametros = parametros;

    public async Task<IActionResult> OnGetAsync()
    {
        var bytes = await _parametros.GetLogoAsync();

        if (bytes is null || bytes.Length == 0)
            return NotFound();

        // Detectar tipo de imagen por magic bytes
        var contentType = DetectarContentType(bytes);

        // La URL incluye ?v=<hash-del-contenido> (ver _Layout.cshtml), así que
        // esta respuesta es válida para siempre bajo esa URL exacta: si el logo
        // cambia, el hash cambia y el navegador pide una URL nueva. Cache largo
        // e "immutable" evita revalidaciones innecesarias sin arriesgar mostrar
        // una imagen vieja.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(bytes, contentType);
    }

    private static string DetectarContentType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return "image/jpeg";
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50)
            return "image/png";
        if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49)
            return "image/gif";
        return "image/jpeg";
    }
}
