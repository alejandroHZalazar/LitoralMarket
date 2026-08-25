namespace LitoralMarket.Application.DTOs;

/// <summary>
/// Ítem para sincronizar el conjunto de imágenes de un producto desde el ABM.
/// Si trae <see cref="Id"/> es una imagen existente (se conserva); si trae
/// <see cref="DataUrl"/> (data:...;base64,...) es una imagen nueva a insertar.
/// Las imágenes existentes que no lleguen en la lista se dan de baja.
/// </summary>
public class ImagenSyncItem
{
    public int? Id { get; set; }
    public string? DataUrl { get; set; }
    public bool EsPrincipal { get; set; }
    public int Orden { get; set; }
}
