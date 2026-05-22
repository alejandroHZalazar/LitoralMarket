using LitoralMarket.Application.Interfaces;

namespace LitoralMarket.Web.Middleware;

public class AccessModeMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string[] PublicPaths =
    [
        "/login", "/logout", "/error", "/favicon.ico", "/css", "/js", "/images", "/lib",
        // APIs internas (webhook MP, polling de estado de pago, etc.):
        // tienen su propia lógica de autorización y NUNCA deben ser redirigidas a /login,
        // porque eso devolvería HTML al frontend que espera JSON y rompería el polling.
        "/api"
    ];

    public AccessModeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IParametrosService parametros)
    {
        var modo = await parametros.GetModoAccesoAsync();

        if (modo == "credenciales" && !context.User.Identity!.IsAuthenticated)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var esPublica = PublicPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!esPublica)
            {
                context.Response.Redirect("/login");
                return;
            }
        }

        await _next(context);
    }
}
