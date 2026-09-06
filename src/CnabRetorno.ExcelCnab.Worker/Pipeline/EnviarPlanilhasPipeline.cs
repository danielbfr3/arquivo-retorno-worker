using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Pipeline;

public class PipelineOptions
{
    public const string Secao = "Pipeline";

    /// <summary>Quantos arquivos são processados ao mesmo tempo. Cada um
    /// abre seu próprio escopo de DI (e portanto seus próprios
    /// DbContext) — DbContext não é thread-safe.</summary>
    public int MaxArquivosConcorrentes { get; set; } = 8;
}

public sealed record ResumoVarredura(
    int Enviados,
    int NaoReconhecidos,
    int SemCliente,
    int SemDocumentoDados,
    int ColunaNaoEncontrada,
    int Falhas);

/// <summary>
/// Varre a pasta e processa cada planilha, isolando falhas: um arquivo que
/// estoura não impede os outros de serem enviados. Só a varredura em si é
/// fatal — sem ela não há o que processar.
///
/// A varredura inteira roda sob <see cref="LockExecucaoExclusiva"/>: duas
/// réplicas varrendo a mesma pasta enviariam a mesma planilha duas vezes,
/// cada uma com ArquivoID próprio — dois CNABs para o mesmo cliente.
/// </summary>
public class EnviarPlanilhasPipeline(
    PastaOrigemExcel origem,
    CobrancaDbContext db,
    IServiceScopeFactory escopos,
    IOptions<PipelineOptions> opcoes,
    ILogger<EnviarPlanilhasPipeline> logger)
{
    private const string RecursoLock = "excel-cnab-varredura";

    private readonly PipelineOptions _opt = opcoes.Value;

    public async Task<ResumoVarredura> ExecutarAsync(CancellationToken ct)
    {
        await using var trava = await LockExecucaoExclusiva
            .TentarAdquirirAsync(db.Database, RecursoLock, ct);
        if (trava is null)
        {
            logger.LogWarning(
                "Varredura pulada — outra instância detém o lock '{Recurso}' (réplica concorrente?)", RecursoLock);
            return new ResumoVarredura(0, 0, 0, 0, 0, 0);
        }

        var pendentes = origem.ListarPendentes();
        if (pendentes.Count == 0)
        {
            logger.LogInformation("Nenhuma planilha pendente na pasta de origem");
            return new ResumoVarredura(0, 0, 0, 0, 0, 0);
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
                    .GetRequiredService<ProcessadorArquivoExcelService>();

                return (await processador.ProcessarAsync(pendente, ct)).Resultado;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao processar {Nome}", pendente.Nome);
                return ResultadoEnvio.Falhou;
            }
            finally
            {
                limite.Release();
            }
        }));

        var resumo = new ResumoVarredura(
            Enviados: resultados.Count(r => r == ResultadoEnvio.Enviado),
            NaoReconhecidos: resultados.Count(r => r == ResultadoEnvio.NaoReconhecido),
            SemCliente: resultados.Count(r => r == ResultadoEnvio.ClienteNaoEncontrado),
            SemDocumentoDados: resultados.Count(r => r == ResultadoEnvio.DocumentoSemDados),
            ColunaNaoEncontrada: resultados.Count(r => r == ResultadoEnvio.ColunaNaoEncontrada),
            Falhas: resultados.Count(r => r == ResultadoEnvio.Falhou));

        logger.LogInformation(
            "Varredura concluída — {Enviados} enviada(s), {NaoReconhecidos} fora do padrão, {SemCliente} sem cliente na adesão, " +
            "{SemDocumentoDados} sem dados na tabela, {ColunaNaoEncontrada} com coluna não encontrada, {Falhas} falha(s)",
            resumo.Enviados, resumo.NaoReconhecidos, resumo.SemCliente,
            resumo.SemDocumentoDados, resumo.ColunaNaoEncontrada, resumo.Falhas);

        return resumo;
    }
}
