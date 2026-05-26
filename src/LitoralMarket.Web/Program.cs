using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using LitoralMarket.Infrastructure.Repositories;
using LitoralMarket.Infrastructure.Services;

using LitoralMarket.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Detrás de un reverse proxy (Railway, Heroku, etc.) ─────────────────
// Railway termina SSL en el edge y reenvía HTTP al contenedor.
// Sin esto, Request.IsHttps == false y UseHttpsRedirection() entra en loop.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;
    // Aceptar headers desde cualquier proxy (Railway no expone una IP fija)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// EF Core + MySQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("Default"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("Default"))
    ));

// Autenticación por cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/error";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // SameAsRequest: en HTTPS marca Secure, en HTTP no. Esto evita que en
        // Railway (que reenvía HTTP al contenedor) las cookies queden bloqueadas.
        // Con ForwardedHeaders activo, Request.IsHttps refleja correctamente el TLS del edge.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Name = "litoral_auth";
    });

// Rate limiting (built-in .NET 7+)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginMaxRequests", 5);
        limiter.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:LoginWindowSeconds", 60));
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Sesión
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "litoral_session";
});

// Cache en memoria
builder.Services.AddMemoryCache();

// Razor Pages con antiforgery
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// Web API Controllers (webhooks, etc.)
builder.Services.AddControllers();

// DI — Repositorios y Servicios
builder.Services.AddScoped<IProductoRepository, ProductoRepository>();
builder.Services.AddScoped<IProductoAdminService, ProductoAdminService>();
builder.Services.AddScoped<IPedidoAdminService, PedidoAdminService>();
builder.Services.AddScoped<IProveedorAdminService, ProveedorAdminService>();
builder.Services.AddScoped<IRubroAdminService, RubroAdminService>();
builder.Services.AddScoped<IDireccionEntregaAdminService, DireccionEntregaAdminService>();
builder.Services.AddScoped<IProveedorRepository, ProveedorRepository>();
builder.Services.AddScoped<IDireccionEntregaRepository, DireccionEntregaRepository>();
builder.Services.AddScoped<IParametrosService, ParametrosService>();
builder.Services.AddScoped<IParametrosAdminService, ParametrosAdminService>();
builder.Services.AddScoped<IEstadisticasService, EstadisticasService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICarritoService, CarritoService>();
builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddScoped<IPagoEcommerceService, PagoEcommerceService>();
builder.Services.AddScoped<IPdfPagoService, PdfPagoService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IMercadoPagoOAuthService, MercadoPagoOAuthService>();

// HttpClient para MercadoPago
builder.Services.AddHttpClient("MercadoPago", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});

// Poller de pagos MercadoPago (background service)
builder.Services.AddHostedService<LitoralMarket.Infrastructure.Services.MercadoPagoPollerService>();

// HttpContextAccessor para acceder al context en servicios
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// CRÍTICO: ForwardedHeaders debe ir ANTES que cualquier middleware que mire
// Request.IsHttps o Request.Scheme (auth cookies, HttpsRedirection, etc.)
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// En Railway el TLS lo termina el proxy; no redirigimos HTTPS adentro del contenedor.
// En desarrollo local sí queremos forzar HTTPS.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Archivos estáticos del wwwroot (CSS, JS, imágenes propias)
app.UseStaticFiles();

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.UseMiddleware<AccessModeMiddleware>();

app.MapRazorPages();
app.MapControllers();

// Endpoint público para servir imágenes de productos (longblob en BD)
app.MapGet("/images/productos/{id:int}", async (int id, LitoralMarket.Infrastructure.Data.AppDbContext db, HttpContext ctx) =>
{
    var imagen = await db.Productos
        .Where(p => p.Id == id)
        .Select(p => p.Imagen)
        .FirstOrDefaultAsync();

    if (imagen is null or { Length: 0 }) return Results.NotFound();

    string mime = imagen.Length >= 2 && imagen[0] == 0xFF && imagen[1] == 0xD8 ? "image/jpeg"
                : imagen.Length >= 4 && imagen[0] == 0x89 && imagen[1] == 0x50 ? "image/png"
                : imagen.Length >= 3 && imagen[0] == 0x47 && imagen[1] == 0x49 ? "image/gif"
                : "image/webp";

    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(imagen, mime, enableRangeProcessing: false);
});

app.Run();
