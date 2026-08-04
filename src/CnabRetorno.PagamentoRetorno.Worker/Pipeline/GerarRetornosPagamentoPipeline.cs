using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;

namespace CnabRetorno.PagamentoRetorno.Worker.Pipeline;

/// <summary>
/// Executa uma janela: descobre quais clientes tiveram movimentação no
/// dia útil e gera um arquivo por cliente.
///
/// **Dia útil = consolidado a consolidado** (18h→18h por padrão), não
/// meia-noite a meia-noite. É o que fecha o buraco pós-18h: um desfecho
/// às 18h30 pertence ao dia útil seguinte e entra na primeira parcial de
/// amanhã — antes, ele não pertencia a janela nenhuma e sumia.
///
/// A janela inteira roda sob <see cref="LockExecucaoExclusiva"/>: duas
/// réplicas do worker acordam no mesmo horário, e sem o lock cada uma
/// geraria um arquivo por cliente com NSAs diferentes.
///
/// A falha de um cliente não derruba a janela — cada arquivo é
/// independente, e barrar os outros por causa de um só aumentaria o
/// estrago.
/// </summary>
public class GerarRetornosPagamentoPipeline(
    PagamentoDbContext db,
    MovimentacoesRepository movimentacoes,
    ControleJanelaRepository controleJanela,
    IServiceScopeFactory escopos,
    CalculadoraJanelas janelas,
    ILogger<GerarRetornosPagamentoPipeline> logger)
{
    private const string RecursoLock = "cnab-pagamento-retorno-janela";

    public async Task ExecutarAsync(Ocorrencia janela, CancellationToken ct)
    {
        await using var trava = await LockExecucaoExclusiva.TentarAdquirirAsync(db.Database, RecursoLock, ct);
        if (trava is null)
        {
            logger.LogWarning(
                "Janela {Momento:o} pulada — outra instância detém o lock '{Recurso}' (réplica concorrente?)",
                janela.Momento, RecursoLock);
            return;
        }

        // Piso do dia útil: o consolidado anterior (exclusivo — o que caiu
        // exatamente nele já foi reportado ontem). Fallback de 24h só se a
        // configuração não produzir consolidado nenhum em 8 dias.
        var pisoDiaUtil = janelas.InstanteBanco(
            janelas.ConsolidadoAnterior(janela.Momento)?.Momento ?? janela.Momento.AddDays(-1));
        var fim = janelas.InstanteBanco(janela.Momento);

        var doDiaUtil = await movimentacoes.ObterPeriodoAsync(pisoDiaUtil, fim, ct);

        var clientes = janela.Tipo == TipoJanela.Consolidado
            ? doDiaUtil
            : await RecortarDeltaAsync(doDiaUtil, pisoDiaUtil, ct);

        if (clientes.Count == 0)
        {
            logger.LogInformation(
                "Janela {Tipo} de {Momento:o}: nenhuma movimentação — nenhum arquivo gerado",
                janela.Tipo, janela.Momento);
            return;
        }

        logger.LogInformation(
            "Janela {Tipo} de {Momento:o}: {Clientes} cliente(s) com movimentação (dia útil desde {Piso:o})",
            janela.Tipo, janela.Momento, clientes.Count, pisoDiaUtil);

        var falhas = 0;

        foreach (var cliente in clientes)
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var processador = escopo.ServiceProvider
                    .GetRequiredService<ProcessadorRetornoPagamentoService>();

                await processador.ProcessarAsync(cliente, janela, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex, "Cliente {Documento} falhou na janela {Momento:o}",
                    cliente.Documento, janela.Momento);
            }
        }

        logger.LogInformation(
            "Janela {Tipo} concluída — {Gerados} arquivo(s) gerado(s), {Falhas} falha(s)",
            janela.Tipo, clientes.Count - falhas, falhas);
    }

    /// <summary>
    /// O recorte do parcial, em duas camadas por cliente:
    ///
    /// <list type="number">
    ///   <item><b>Marca d'água</b> — só desfechos estritamente após o que
    ///   já foi reportado. Por cliente, porque um pode ter falhado na
    ///   janela anterior enquanto os outros passaram.</item>
    ///   <item><b>Pares (PagamentoID, CodigoStatus)</b> — barra o
    ///   pagamento que voltou pro delta por um UPDATE qualquer sem
    ///   mudança de status. Status novo passa: é desfecho novo de
    ///   verdade.</item>
    /// </list>
    /// </summary>
    private async Task<List<MovimentacoesDoCliente>> RecortarDeltaAsync(
        List<MovimentacoesDoCliente> doDiaUtil, DateTime pisoDiaUtil, CancellationToken ct)
    {
        var recortados = new List<MovimentacoesDoCliente>(doDiaUtil.Count);

        foreach (var cliente in doDiaUtil)
        {
            var marca = await controleJanela.ObterMarcaDaguaAsync(cliente.Documento, pisoDiaUtil, ct);

            var aposMarca = cliente.Movimentacoes
                .Where(m => (m.DataAtualizacao ?? m.DataCriacao) > marca)
                .ToList();

            if (aposMarca.Count == 0) continue;

            var reportados = await controleJanela.ObterReportadosAsync(
                [.. aposMarca.Select(m => m.PagamentoID)], ct);

            var novas = aposMarca
                .Where(m => !reportados.Contains((m.PagamentoID, m.CodigoStatus)))
                .ToList();

            if (novas.Count > 0)
                recortados.Add(cliente with { Movimentacoes = novas });
        }

        return recortados;
    }
}
