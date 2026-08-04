using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using CnabRetorno.PagamentoRetorno.Worker.Pipeline;

namespace CnabRetorno.PagamentoRetorno.Worker;

/// <summary>
/// Robô 2 — geração dos arquivos de retorno de pagamentos.
///
/// Processo residente (e não CronJob): o expediente é uma sequência de
/// janelas do mesmo dia, e o tipo de arquivo depende de onde a janela cai
/// nessa sequência — a última é o consolidado. Um CronJob por horário
/// perderia essa noção e exigiria duas definições no cluster.
///
/// Ao subir, o worker não recupera janelas perdidas: se o pod estava fora
/// do ar às 9h, não gera o parcial das 9h retroativamente. Não precisa —
/// a marca d'água é por cliente, então o parcial seguinte já leva o que
/// ficou para trás.
/// </summary>
public class PagamentoRetornoWorker(
    IServiceProvider provider,
    CalculadoraJanelas janelas,
    TimeProvider tempo,
    ILogger<PagamentoRetornoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var proxima = janelas.ProximaApos(tempo.GetUtcNow());
            if (proxima is null)
            {
                logger.LogCritical(
                    "Nenhuma janela encontrada nos próximos 8 dias — revisar a seção Janela do appsettings");
                return;
            }

            logger.LogInformation("Próxima janela: {Momento:o} ({Tipo})", proxima.Momento, proxima.Tipo);

            try
            {
                var espera = proxima.Momento - tempo.GetUtcNow();
                if (espera > TimeSpan.Zero)
                    await Task.Delay(espera, tempo, ct);

                await ExecutarJanelaAsync(proxima, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na janela {Momento:o}", proxima.Momento);
            }
        }
    }

    private async Task ExecutarJanelaAsync(Ocorrencia janela, CancellationToken ct)
    {
        using var escopo = provider.CreateScope();
        var pipeline = escopo.ServiceProvider.GetRequiredService<GerarRetornosPagamentoPipeline>();
        await pipeline.ExecutarAsync(janela, ct);
    }
}
