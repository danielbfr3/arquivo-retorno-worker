using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;

namespace CnabRetorno.PagamentoRetorno.Worker.Pipeline;

/// <summary>
/// Executa uma janela: descobre quais clientes tiveram movimentação,
/// e gera um arquivo por cliente.
///
/// A falha de um cliente não derruba a janela — cada arquivo é
/// independente, e barrar os outros por causa de um só aumentaria o
/// estrago.
/// </summary>
public class GerarRetornosPagamentoPipeline(
    MovimentacoesRepository movimentacoes,
    ControleJanelaRepository controleJanela,
    IServiceScopeFactory escopos,
    CalculadoraJanelas janelas,
    ILogger<GerarRetornosPagamentoPipeline> logger)
{
    public async Task ExecutarAsync(Ocorrencia janela, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(janela.Momento, janelas.Fuso);
        var dia = DateOnly.FromDateTime(local.Date);
        var inicioDoDia = local.Date;
        var fim = janela.Momento.DateTime;

        var clientes = janela.Tipo == TipoJanela.Consolidado
            ? await movimentacoes.ObterDoDiaAsync(inicioDoDia, fim, ct)
            : await ObterDeltaPorClienteAsync(inicioDoDia, fim, dia, ct);

        if (clientes.Count == 0)
        {
            logger.LogInformation(
                "Janela {Tipo} de {Momento:o}: nenhuma movimentação — nenhum arquivo gerado",
                janela.Tipo, janela.Momento);
            return;
        }

        logger.LogInformation(
            "Janela {Tipo} de {Momento:o}: {Clientes} cliente(s) com movimentação",
            janela.Tipo, janela.Momento, clientes.Count);

        var falhas = 0;

        foreach (var cliente in clientes)
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var processador = escopo.ServiceProvider
                    .GetRequiredService<ProcessadorRetornoPagamentoService>();

                await processador.ProcessarAsync(cliente, janela, dia, ct);
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
    /// O parcial é delta, e a marca d'água é **por cliente** — cada um
    /// tem seu próprio ponto de corte, porque um cliente pode ter falhado
    /// na janela anterior enquanto os outros passaram. Por isso o filtro
    /// não pode ser um único intervalo na consulta: busca-se o dia todo e
    /// corta-se por cliente.
    /// </summary>
    private async Task<List<MovimentacoesDoCliente>> ObterDeltaPorClienteAsync(
        DateTime inicioDoDia, DateTime fim, DateOnly dia, CancellationToken ct)
    {
        var doDia = await movimentacoes.ObterDoDiaAsync(inicioDoDia, fim, ct);
        var recortados = new List<MovimentacoesDoCliente>(doDia.Count);

        foreach (var cliente in doDia)
        {
            var marca = await controleJanela.ObterMarcaDaguaAsync(cliente.Documento, dia, inicioDoDia, ct);

            var novas = cliente.Movimentacoes
                .Where(m => (m.DataAtualizacao ?? m.DataCriacao) > marca)
                .ToList();

            if (novas.Count > 0)
                recortados.Add(cliente with { Movimentacoes = novas });
        }

        return recortados;
    }
}
