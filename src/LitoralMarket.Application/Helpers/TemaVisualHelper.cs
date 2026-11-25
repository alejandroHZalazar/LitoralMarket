using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LitoralMarket.Application.Helpers;

/// <summary>Tipografia ofrecida en la configuracion visual (lista curada: ninguna fuente arbitraria).</summary>
/// <param name="Clave">Valor que se guarda (parametros tema/fuenteTitulos y tema/fuenteTexto).</param>
/// <param name="Familia">Valor de font-family (con alternativas de respaldo).</param>
/// <param name="Paquete">Paquete de Fontsource en jsDelivr (el CDN que ya usa el sitio y permite la politica de seguridad).</param>
/// <param name="Estilo">Descripcion corta para el administrador.</param>
public record FuenteTema(string Clave, string Nombre, string Familia, string Paquete, string Estilo);

/// <summary>Configuracion visual de la tienda definida por el administrador (modulo de parametros "tema"). null = valor por defecto.</summary>
public record TemaVisualConfig(
    string? Primario = null, string? Acento = null, string? Navegacion = null, string? Promocion = null,
    string? Fondo = null, string? FuenteTitulos = null, string? FuenteTexto = null);

/// <summary>
/// Configuracion visual centralizada: catalogo de fuentes, calculo de contraste y generacion del CSS que sobreescribe las
/// variables del tema. Es la UNICA fuente de estas reglas (el layout publico y la pantalla de administracion la usan);
/// el calculo de texto es automatico, nunca se configura a mano. Todo valor que entra al CSS se valida: colores con
/// ^#RRGGBB$ y fuentes solo del catalogo, asi no hay forma de inyectar CSS.
/// </summary>
public static class TemaVisualHelper
{
    public const string FontsourceVersion = "5.3.0";
    /// <summary>Texto sobre fondos claros (el mismo que ya usa el sitio para el acento).</summary>
    public const string TextoOscuro = "#12201A";
    public const string TextoClaro  = "#FFFFFF";

    public static readonly IReadOnlyList<FuenteTema> FuentesTitulos = new[]
    {
        new FuenteTema("playfair",   "Playfair Display",   "'Playfair Display', Georgia, 'Times New Roman', serif", "playfair-display",   "Serif elegante y contrastada (lujo, joyería)"),
        new FuenteTema("cormorant",  "Cormorant Garamond", "'Cormorant Garamond', Georgia, serif",                 "cormorant-garamond", "Serif clásica y refinada"),
        new FuenteTema("lora",       "Lora",               "'Lora', Georgia, serif",                               "lora",               "Serif suave y muy legible"),
        new FuenteTema("montserrat", "Montserrat",         "'Montserrat', 'Segoe UI', system-ui, sans-serif",      "montserrat",         "Sans moderna y geométrica"),
        new FuenteTema("poppins",    "Poppins",            "'Poppins', 'Segoe UI', system-ui, sans-serif",         "poppins",            "Sans redondeada y amigable"),
    };

    public static readonly IReadOnlyList<FuenteTema> FuentesTexto = new[]
    {
        new FuenteTema("inter",      "Inter",      "'Inter', 'Segoe UI', system-ui, sans-serif",       "inter",      "Sans neutra, ideal para pantallas"),
        new FuenteTema("lato",       "Lato",       "'Lato', 'Segoe UI', system-ui, sans-serif",        "lato",       "Sans cálida y equilibrada"),
        new FuenteTema("open-sans",  "Open Sans",  "'Open Sans', 'Segoe UI', system-ui, sans-serif",   "open-sans",  "Sans humanista, muy legible"),
        new FuenteTema("montserrat", "Montserrat", "'Montserrat', 'Segoe UI', system-ui, sans-serif",  "montserrat", "Sans moderna y geométrica"),
        new FuenteTema("poppins",    "Poppins",    "'Poppins', 'Segoe UI', system-ui, sans-serif",     "poppins",    "Sans redondeada y amigable"),
    };

    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static bool EsHex(string? c) => c is not null && Hex.IsMatch(c);

    /// <summary>Fuente del catalogo por clave; null si es la predeterminada (clave vacia o desconocida).</summary>
    public static FuenteTema? BuscarFuente(IReadOnlyList<FuenteTema> catalogo, string? clave) =>
        string.IsNullOrWhiteSpace(clave) ? null : catalogo.FirstOrDefault(f => f.Clave == clave);

    /// <summary>Hojas de estilo de Fontsource a cargar (pesos 400 y 700), sin repetir, solo de las fuentes elegidas.</summary>
    public static List<string> UrlsFuentes(TemaVisualConfig cfg)
    {
        var paquetes = new List<string>();
        var t = BuscarFuente(FuentesTitulos, cfg.FuenteTitulos);
        var x = BuscarFuente(FuentesTexto, cfg.FuenteTexto);
        if (t is not null) paquetes.Add(t.Paquete);
        if (x is not null && !paquetes.Contains(x.Paquete)) paquetes.Add(x.Paquete);
        return paquetes.SelectMany(p => new[] { 400, 700 }
            .Select(w => $"https://cdn.jsdelivr.net/npm/@fontsource/{p}@{FontsourceVersion}/{w}.css")).ToList();
    }

    // ── Color: luminancia / contraste (WCAG) ────────────────────────────────

    private static (int R, int G, int B) Rgb(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    public static double Luminancia(string hex)
    {
        static double Canal(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Canal(r) + 0.7152 * Canal(g) + 0.0722 * Canal(b);
    }

    public static double Contraste(string hexA, string hexB)
    {
        double a = Luminancia(hexA), b = Luminancia(hexB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Texto (claro u oscuro) con mejor contraste para dibujar sobre el fondo dado.</summary>
    public static string TextoContrastante(string hexFondo)
    {
        // Misma regla que usaba el sitio para el acento: se compara contra negro y blanco puros (luminancia 0 y 1).
        var l = Luminancia(hexFondo);
        var conNegro = (l + 0.05) / 0.05;
        var conBlanco = 1.05 / (l + 0.05);
        return conNegro >= conBlanco ? TextoOscuro : TextoClaro;
    }

    /// <summary>true si sobre ese fondo corresponde texto claro.</summary>
    public static bool EsOscuro(string hexFondo) => TextoContrastante(hexFondo) == TextoClaro;

    /// <summary>Mezcla <paramref name="hex"/> con <paramref name="con"/> (porcentaje de <paramref name="con"/>, 0 a 1).</summary>
    public static string Mezclar(string hex, string con, double pctCon)
    {
        var (r1, g1, b1) = Rgb(hex);
        var (r2, g2, b2) = Rgb(con);
        int Mix(int a, int b) => (int)Math.Round(a * (1 - pctCon) + b * pctCon, MidpointRounding.AwayFromZero);   // igual que Math.round de JS
        return $"#{Mix(r1, r2):X2}{Mix(g1, g2):X2}{Mix(b1, b2):X2}";
    }

    /// <summary>Texto que mejor se lee sobre DOS fondos a la vez (los dos extremos de un degradado).</summary>
    private static string TextoSobre(string fondoA, string fondoB)
    {
        double Peor(string t) => Math.Min(Contraste(fondoA, t), Contraste(fondoB, t));
        return Peor(TextoClaro) >= Peor(TextoOscuro) ? TextoClaro : TextoOscuro;
    }

    // ── Generacion del CSS ──────────────────────────────────────────────────

    /// <summary>true si el fondo configurado es oscuro: el layout agrega la clase "fondo-oscuro" al html.</summary>
    public static bool FondoOscuro(TemaVisualConfig cfg) => EsHex(cfg.Fondo) && EsOscuro(cfg.Fondo!);

    /// <summary>
    /// CSS que sobreescribe las variables de la hoja de estilos con la configuracion del administrador. Devuelve cadena vacia
    /// si no hay nada configurado (la tienda queda exactamente con su apariencia original).
    /// </summary>
    public static string GenerarCss(TemaVisualConfig cfg)
    {
        var primario    = EsHex(cfg.Primario)   ? cfg.Primario!.ToUpperInvariant()   : null;
        var acento      = EsHex(cfg.Acento)     ? cfg.Acento!.ToUpperInvariant()     : null;
        var navegacion  = EsHex(cfg.Navegacion) ? cfg.Navegacion!.ToUpperInvariant() : null;
        var promocion   = EsHex(cfg.Promocion)  ? cfg.Promocion!.ToUpperInvariant()  : null;
        var fondo       = EsHex(cfg.Fondo)      ? cfg.Fondo!.ToUpperInvariant()      : null;
        var fTitulos    = BuscarFuente(FuentesTitulos, cfg.FuenteTitulos);
        var fTexto      = BuscarFuente(FuentesTexto, cfg.FuenteTexto);

        if (primario is null && acento is null && navegacion is null && promocion is null && fondo is null
            && fTitulos is null && fTexto is null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("/* Configuración visual definida por el administrador: se aplica sobre cualquier tema. */\n");
        sb.Append(":root, :root[data-theme], :root:not([data-theme]) {\n");

        if (primario is not null)
        {
            sb.Append($"  --verde:          {primario} !important;\n");
            sb.Append($"  --verde-hover:    color-mix(in srgb, {primario} 90%, black) !important;\n");
            sb.Append($"  --verde-oscuro:   color-mix(in srgb, {primario} 80%, black) !important;\n");
            sb.Append($"  --verde-profundo: color-mix(in srgb, {primario} 60%, black) !important;\n");
        }
        if (acento is not null)
        {
            sb.Append($"  --dorado:         {acento} !important;\n");
            sb.Append($"  --dorado-claro:   color-mix(in srgb, {acento} 62%, white) !important;\n");
            sb.Append($"  --dorado-suave:   color-mix(in srgb, {acento} 18%, transparent) !important;\n");
            sb.Append($"  --celeste:        {acento} !important;\n");
            sb.Append($"  --celeste-oscuro: color-mix(in srgb, {acento} 80%, black) !important;\n");
            sb.Append($"  --celeste-suave:  color-mix(in srgb, {acento} 18%, transparent) !important;\n");
            sb.Append($"  --acento-texto:   {TextoContrastante(acento)} !important;\n");
        }

        string? navFg = null;
        if (navegacion is not null)
        {
            // Barra superior, menu lateral y pie: degradado sutil desde el color elegido hacia uno apenas mas oscuro.
            var oscuro = Mezclar(navegacion, "#000000", EsOscuro(navegacion) ? 0.22 : 0.08);
            navFg = TextoSobre(navegacion, oscuro);
            var claroFg = navFg == TextoClaro;
            sb.Append($"  --nav-claro:      {navegacion} !important;\n");
            sb.Append($"  --nav-oscuro:     {oscuro} !important;\n");
            sb.Append($"  --nav-fg:         {navFg} !important;\n");
            sb.Append($"  --nav-activo:     {ColorActivoNav(acento, navegacion, oscuro, navFg)} !important;\n");
            sb.Append($"  --nav-sub:        {(claroFg ? "rgba(0,0,0,0.12)" : "rgba(255,255,255,0.40)")} !important;\n");
            sb.Append($"  --nav-sub-fuerte: {(claroFg ? "rgba(0,0,0,0.20)" : "rgba(255,255,255,0.50)")} !important;\n");
            // El pie no se oscurece hacia negro: el texto automatico se calcula sobre el color de la barra.
            sb.Append("  --nav-footer-mezcla: 100% !important;\n");
        }

        if (promocion is not null)
        {
            // Etiquetas y distintivos de ofertas: texto del distintivo con el mejor contraste; texto suelto (sobre el fondo
            // de la pagina) oscurecido si el color es muy claro para leerse sobre blanco.
            var fg = OscurecerHastaLeerse(promocion);
            sb.Append($"  --color-promo:       {promocion} !important;\n");
            sb.Append($"  --color-promo-texto: {TextoContrastante(promocion)} !important;\n");
            sb.Append($"  --color-promo-fg:    {fg} !important;\n");
        }

        if (fTitulos is not null) sb.Append($"  --fuente-titulos: {fTitulos.Familia};\n");
        if (fTexto   is not null) sb.Append($"  --fuente-texto:   {fTexto.Familia};\n");
        sb.Append("}\n");

        // Temas oscuros: el texto de promocion suelto se aclara para leerse sobre fondos oscuros.
        if (promocion is not null)
        {
            var claro = Mezclar(promocion, "#FFFFFF", 0.45);
            sb.Append($":root[data-theme=\"dark\"] {{ --color-promo-fg: {claro} !important; }}\n");
            sb.Append($"@media (prefers-color-scheme: dark) {{ :root:not([data-theme]) {{ --color-promo-fg: {claro} !important; }} }}\n");
            // La etiqueta "PROMO" dorada de los productos marcados como promocion pasa a usar el color de promocion.
            sb.Append($".badge-promo {{ background: var(--color-promo) !important; color: var(--color-promo-texto) !important; }}\n");
        }

        // Texto de la barra y el menu oscuro sobre fondos claros: la cruz del panel movil no debe invertirse.
        if (navFg == TextoOscuro)
            sb.Append(".rubros-sidebar .btn-close { filter: none !important; }\n");

        // Fondo de la pagina: solo en el tema claro (el oscuro y el de alto contraste conservan su propio fondo).
        if (fondo is not null)
        {
            var vars = FondoVariables(fondo, promocion);
            sb.Append($":root[data-theme=\"light\"] {{\n{vars}}}\n");
            sb.Append($"@media (prefers-color-scheme: light) {{ :root:not([data-theme]) {{\n{vars}}} }}\n");

            if (EsOscuro(fondo))
            {
                // Con fondo oscuro, el color de marca usado como TEXTO (precios, enlaces, totales) se aclara lo justo para leerse
                // sobre las tarjetas. Solo dentro del contenido de la pagina: la barra, el menu y el pie conservan sus colores.
                var marca = AclararHastaLeerse(primario ?? "#1DB062", Mezclar(fondo, "#FFFFFF", 0.10));
                var v = $"--verde: {marca}; --verde-hover: {Mezclar(marca, "#FFFFFF", 0.12)}; --verde-oscuro: {marca}; --verde-profundo: {marca};";
                sb.Append($"html.fondo-oscuro[data-theme=\"light\"] .layout-rubros-main {{ {v} }}\n");
                sb.Append($"@media (prefers-color-scheme: light) {{ html.fondo-oscuro:not([data-theme]) .layout-rubros-main {{ {v} }} }}\n");
            }
        }

        // Con una tipografia de texto personalizada (casi siempre mas ancha que la original) el precio no debe partirse en dos lineas.
        if (fTexto is not null)
            sb.Append(".producto-precio { white-space: nowrap; font-size: clamp(1rem, 0.9rem + 0.6vw, 1.2rem); }\n" +
                      ".producto-precio-desde { display: block; margin: 0 0 .1rem; }\n");

        return sb.ToString();
    }

    /// <summary>Aclara el color de a poco (hacia blanco) hasta que se lea (4,5:1) sobre el fondo dado; si ya se lee, no lo toca.</summary>
    public static string AclararHastaLeerse(string hex, string fondo)
    {
        for (var p = 0.0; p <= 0.9; p += 0.05)
        {
            var c = p == 0.0 ? hex : Mezclar(hex, "#FFFFFF", p);
            if (Contraste(c, fondo) >= 4.5) return c;
        }
        return Mezclar(hex, "#FFFFFF", 0.9);
    }

    /// <summary>Oscurece el color de a poco hasta que se lea (4,5:1) sobre blanco; si ya se lee, no lo toca.</summary>
    public static string OscurecerHastaLeerse(string hex)
    {
        for (var p = 0.0; p <= 0.9; p += 0.05)
        {
            var c = p == 0.0 ? hex : Mezclar(hex, "#000000", p);
            if (Contraste(c, "#FFFFFF") >= 4.5) return c;
        }
        return Mezclar(hex, "#000000", 0.9);
    }

    private static string FondoVariables(string fondo, string? promocion)
    {
        var sb = new StringBuilder();
        sb.Append($"  --crema:    {fondo};\n  --bg-page:  {fondo};\n");
        if (EsOscuro(fondo))
        {
            // Fondo oscuro: texto claro y superficies (tarjetas, formularios, menus) apenas mas claras que el fondo.
            var superficie = Mezclar(fondo, "#FFFFFF", 0.10);
            sb.Append("  --gris-texto: #E5E7EB;\n  --text-primary: #F3F4F6;\n  --text-secondary: #9CA3AF;\n  --text-placeholder: #6B7280;\n");
            sb.Append($"  --bg-surface: {superficie};\n  --bg-input: {superficie};\n  --bg-subtle: {Mezclar(fondo, "#FFFFFF", 0.18)};\n");
            sb.Append($"  --border-ui: {Mezclar(fondo, "#FFFFFF", 0.25)};\n  --border-input: {Mezclar(fondo, "#FFFFFF", 0.32)};\n");
            sb.Append($"  --color-promo-fg: {(promocion is null ? "#FF8A80" : Mezclar(promocion, "#FFFFFF", 0.45))} !important;\n");
        }
        else if (Contraste(fondo, "#374151") < 4.5)
        {
            // Fondo claro intermedio donde el gris habitual no alcanza: texto mas oscuro.
            sb.Append("  --gris-texto: #111827;\n  --text-primary: #111827;\n  --text-secondary: #374151;\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Color de los elementos activos del menu lateral (ruta actual): el acento (o el dorado por defecto) si se lee sobre el
    /// fondo de la barra; si no, una version aclarada u oscurecida; si ninguna alcanza, el color de texto de la barra.
    /// </summary>
    private static string ColorActivoNav(string? acento, string navegacion, string navOscuro, string navFg)
    {
        var candidato = acento is null ? "#E8C87A" : Mezclar(acento, "#FFFFFF", 0.38);   // mismo criterio que --dorado-claro
        bool Sirve(string c) => Contraste(c, navegacion) >= 3 && Contraste(c, navOscuro) >= 3;
        if (Sirve(candidato)) return candidato;
        var base_ = acento ?? "#C9A84C";
        foreach (var c in new[] { Mezclar(base_, "#000000", 0.45), Mezclar(base_, "#FFFFFF", 0.6), base_ })
            if (Sirve(c)) return c;
        return navFg;
    }
}
