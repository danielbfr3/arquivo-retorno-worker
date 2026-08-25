using CnabRetorno.ExcelCnab.Worker.Pipeline;
using Cronos;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker;

public class CronOptions
{
    public const string Secao = "Worker";

    /// <summary>"CronJob": executa uma varredura e encerra (frequência
    /// controlada pelo K8s). "Loop": processo residente com agendamento
    /// interno via expressão cron — útil pra rodar local sem depender de
    /// CronJob do cluster, e porque a pasta recebe planilhas ao longo do
    /// dia.</summary>
    public string Modo { get; set; } = "Loop";

    /// <summary>Expressão cron usada apenas no modo Loop. Padrão: de 15 em
    /// 15 minutos.</summary>
    public string Cron { get; set; } = "*/15 * * * *";
}

/// <summary>
/// Varre a pasta de planilhas e entrega cada uma ao conversor.
/// </summary>
public class ExcelCnabWorker(
    IServiceProvider provider,
    IHostApplicationLifetime lifetime,
    IOptions<CronOptions> opcoes,
    ILogger<ExcelCnabWorker> logger) : BackgroundService
{
    private readonly CronOptions _opt = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.Equals(_opt.Modo, "CronJob", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await ExecutarCicloAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogCritical(ex, "Execução abortada");
                Environment.ExitCode = 1;
            }
            finally
            {
                lifetime.StopApplication();
            }
            return;
        }

        var cron = CronExpression.Parse(_opt.Cron);
        while (!ct.IsCancellationRequested)
        {
            var proxima = cron.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
            if (proxima is null) break;

            logger.LogInformation("Próxima varredura: {Proxima:o}", proxima);
            try
            {
                await Task.Delay(proxima.Value - DateTimeOffset.Now, ct);
                await ExecutarCicloAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na varredura agendada");
            }
        }
    }

    private async Task ExecutarCicloAsync(CancellationToken ct)
    {
        using var escopo = provider.CreateScope();
        var pipeline = escopo.ServiceProvider.GetRequiredService<EnviarPlanilhasPipeline>();
        await pipeline.ExecutarAsync(ct);
    }
}
