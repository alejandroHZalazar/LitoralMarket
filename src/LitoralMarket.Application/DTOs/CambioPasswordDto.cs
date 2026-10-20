namespace LitoralMarket.Application.DTOs;

/// <summary>Datos del formulario "Cambiar contraseña". El usuario se identifica por la sesión, nunca por el form.</summary>
public class CambioPasswordDto
{
    public string? Actual       { get; set; }
    public string? Nueva        { get; set; }
    public string? Confirmacion { get; set; }
}

public enum ResultadoCambioPassword
{
    Ok,
    ActualIncorrecta,
    UsuarioNoEncontrado
}
