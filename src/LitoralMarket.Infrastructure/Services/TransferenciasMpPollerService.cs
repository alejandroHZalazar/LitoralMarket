using LitoralMarket.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Servicio de fondo que detecta las transferencias entrantes de Mercado Pago y las concilia con los
/// cobros de transferencia pendientes. Mercado Pago no documenta un webhook para transferencias,
/// así que se consulta cada <c>mercadopago/transferenciaPollingSegundos</c> (90 s por defecto).
///
/// No hace nada mientras <c>ecommerce/metodoPagoTransferenciaMP</c> no valga '1': solo vuelve a
/// mirar el parámetro cada minuto. Ante errores de la API espera cada vez más (hasta 10 minutos).
/// </summary>
public class TransferenciasMpPollerService : BackgroundService
{
    private static readonly TimeSpan EsperaDeshabilitado = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EsperaMaximaError   = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory                    _scopeFactory;
    private readonly ILogger<TransferenciasMpPollerService> _logger;

    public TransferenciasMpPollerService(
        IServiceScopeFactory scopeFactory,
        ILogger<TransferenciasMpPollerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Deja que la aplicación termine de iniciar antes del primer ciclo.
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

            var fallosSeguidos = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var espera = EsperaDeshabilitado;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var servicio = scope.ServiceProvider.GetRequiredService<IConciliacionTransferenciasService>();
                    var r = await servicio.EjecutarCicloAsync(stoppingToken);

                    if (!r.Habilitado)
                    {
                        fallosSeguidos = 0;
                    }
                    else if (r.Error)
                    {
                        fallosSeguidos++;
                        espera = TimeSpan.FromSeconds(r.PollingSegundos * Math.Pow(2, Math.Min(fallosSeguidos, 4)));
                    }
                    else
                    {
                        fallosSeguidos = 0;
                        espera = TimeSpan.FromSeconds(r.PollingSegundos);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    fallosSeguidos++;
                    espera = TimeSpan.FromSeconds(90 * Math.Pow(2, Math.Min(fallosSeguidos, 4)));
                    _logger.LogError(ex, "Transferencias MP: error inesperado en el ciclo de detección");
                }

                if (espera > EsperaMaximaError) espera = EsperaMaximaError;
                await Task.Delay(espera, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // apagado de la aplicación
        }
    }
}
