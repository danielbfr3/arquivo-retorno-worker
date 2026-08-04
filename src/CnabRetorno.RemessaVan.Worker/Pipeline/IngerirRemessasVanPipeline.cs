using CnabRetorno.RemessaVan.Worker.Origem;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RemessaVan.Worker.Pipeline;

public class PipelineOptions
{
    public const string Secao = "Pipeline";

    /// <summary>Quantos arquivos são processados ao mesmo tempo. Cada um
    /// abre seu próprio escopo de DI (e portanto seu próprio
    /// DbContext) — DbContext não é thread-safe.</summary>
    public int MaxArquivosConcorrentes { get; set; } = 8;
}

public sealed record ResumoIngestao(int Ingeridos, int Duplicados, int NaoReconhecidos, int Ignorados, int Falhas);

/// <summary>
/// Varre a pasta das VANs e processa cada arquivo, isolando falhas: um
/// arquivo que estoura não impede os outros de serem ingeridos. Só a
/// varredura em si é fatal — sem ela não há o que processar.
///
/// A varredura inteira roda sob <see cref="Persistencia.LockExecucaoExclusiva"/>:
/// duas réplicas varrendo a mesma pasta ingeririam o mesmo arquivo duas
/// vezes, cada uma com GUID próprio.
/// </summary>
public class IngerirRemessasVanPipeline(
    PastaOrigemRemessa origem,
    Persistencia.CobrancaDbContext db,
    IServiceScopeFactory escopos,
    IOptions<PipelineOptions> opcoes,
    ILogger<IngerirRemessasVanPipeline> logger)
{
    private const string RecursoLock = "cnab-remessa-van-ingestao";

    private readonly PipelineOptions _opt = opcoes.Value;

    public async Task<ResumoIngestao> ExecutarAsync(CancellationToken ct)
    {
        await using var trava = await Persistencia.LockExecucaoExclusiva
            .TentarAdquirirAsync(db.Database, RecursoLock, ct);
        if (trava is null)
        {
            logger.LogWarning(
                "Varredura pulada — outra instância detém o lock '{Recurso}' (réplica concorrente?)", RecursoLock);
            return new ResumoIngestao(0, 0, 0, 0, 0);
        }

        var pendentes = origem.ListarPendentes();
        if (pendentes.Count == 0)
        {
            logger.LogInformation("Nenhum arquivo pendente na pasta de origem");
            return new ResumoIngestao(0, 0, 0, 0, 0);
        }

        logger.LogInformation("{Total} arquivo(s) pendente(s)", pendentes.Count);

        using var limite = new SemaphoreSlim(Math.Max(1, _opt.MaxArquivosConcorrentes));

        var resultados = await Task.WhenAll(pendentes.Select(async pendente =>
        {
            await limite.WaitAsync(ct);
            try
            {
                using var escopo = escopos.CreateScope();
                var processador = escopo.ServiceProvider
                    .GetRequiredService<ProcessadorArquivoRemessaService>();

                return (await processador.ProcessarAsync(pendente, ct)).Resultado;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao processar {Nome}", pendente.Nome);
                return ResultadoRemessa.Falhou;
            }
            finally
            {
                limite.Release();
            }
        }));

        var resumo = new ResumoIngestao(
            Ingeridos: resultados.Count(r => r == ResultadoRemessa.Ingerido),
            Duplicados: resultados.Count(r => r == ResultadoRemessa.Duplicado),
            NaoReconhecidos: resultados.Count(r => r == ResultadoRemessa.NaoReconhecido),
            Ignorados: resultados.Count(r => r == ResultadoRemessa.IgnoradoNaoEhRemessa),
            Falhas: resultados.Count(r => r == ResultadoRemessa.Falhou));

        logger.LogInformation(
            "Ingestão concluída — {Ingeridos} ingerido(s), {Duplicados} duplicado(s), {NaoReconhecidos} não reconhecido(s), {Ignorados} ignorado(s), {Falhas} falha(s)",
            resumo.Ingeridos, resumo.Duplicados, resumo.NaoReconhecidos, resumo.Ignorados, resumo.Falhas);

        return resumo;
    }
}
