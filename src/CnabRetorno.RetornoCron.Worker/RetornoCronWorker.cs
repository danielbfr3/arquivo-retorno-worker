using CnabRetorno.RetornoCron.Worker.Pipeline;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker;

/// <summary>
/// Robô 1. Portado do RetornoWorker.cs do pipeline anterior — mesmos modos
/// CronJob (run-once, frequência controlada pelo K8s) e Loop (agendamento
/// interno via Cronos, útil pra rodar local sem depender de CronJob do
/// cluster). Depois de processar os arquivos V/PV do lote, roda o laço de
/// clientes sem arquivo (ver ProcessadorClientesSemArquivoService).
/// </summary>
public class RetornoCronWorker(
    IServiceProvider provider,
    IHostApplicationLifetime lifetime,
    IOptions<CronOptions> opcoes,
    ILogger<RetornoCronWorker> logger) : BackgroundService
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

            logger.LogInformation("Próxima execução: {Proxima:o}", proxima);
            try
            {
                await Task.Delay(proxima.Value - DateTimeOffset.Now, ct);
                await ExecutarCicloAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na execução agendada");
            }
        }
    }

    private async Task ExecutarCicloAsync(CancellationToken ct)
    {
        using var escopo = provider.CreateScope();

        var pipeline = escopo.ServiceProvider.GetRequiredService<ProcessarArquivosVePvPipeline>();
        var resumo = await pipeline.ExecutarAsync(ct);

        var clientesSemArquivo = escopo.ServiceProvider
            .GetRequiredService<ProcessadorClientesSemArquivoService>();
        await clientesSemArquivo.ExecutarAsync(resumo.CnpjsProcessados, ct);
    }
}
