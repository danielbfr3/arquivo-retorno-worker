using System.Text.Json;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.RetornoCron.Worker.Json;
using CnabRetorno.RetornoCron.Worker.Origem;
using CnabRetorno.RetornoCron.Worker.Persistencia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker.Pipeline;

/// <summary>
/// Laço pós-processamento descrito no documento de tarefa: depois que
/// <see cref="ProcessarArquivosVePvPipeline"/> processa todos os V/PV do
/// lote, compara os CNPJs já processados **nesta execução** com a lista de
/// CNPJs com pendência no CASH_COBRANCA (títulos/instruções negados ou com
/// erro) e repete, pros que ficaram de fora, a geração de retorno — sem
/// arquivo V/PV envolvido. A lista de "processados" vem do resumo em
/// memória da execução atual, não de uma tabela.
///
/// Sem um V real de origem, não há CNAB pra converter de forma síncrona —
/// o JSON (arquivo/lote sintéticos + só as pendências do cliente) é
/// montado direto (ver <see cref="MesclagemDadosConvertidos.MontarSintetico"/>),
/// registrado em <c>Cobranca.Arquivo</c> (ver <see cref="ArquivoRepository"/>)
/// e mandado pro conversor assíncrono com o ID dessa linha como correlação.
/// </summary>
public class ProcessadorClientesSemArquivoService(
    ControlePendenciasReportadasDiario controlePendencias,
    CobrancaPendenciasRepository pendencias,
    PendenciasParaTitulosConvertidosFactory pendenciasFactory,
    MesclagemDadosConvertidos mesclagem,
    ArquivoRepository arquivos,
    SequencialArquivoRepository sequenciais,
    ILayoutConversaoApiClient conversor,
    IOptions<PipelineOptions> opcoes,
    ILogger<ProcessadorClientesSemArquivoService> logger)
{
    private static readonly JsonSerializerOptions JsonOpcoesSaida = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PipelineOptions _opt = opcoes.Value;

    public async Task<ResumoClientesSemArquivo> ExecutarAsync(
        IReadOnlyCollection<string> cnpjsProcessados, CancellationToken ct)
    {
        var dataD1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var todosComPendencia = await pendencias.ListarClientesComPendenciaAsync(dataD1, ct);

        var cnpjsSemArquivo = todosComPendencia
            .Where(cnpj => !cnpjsProcessados.Contains(cnpj))
            .ToList();

        logger.LogInformation(
            "{Qtd} cliente(s) com pendência mas sem arquivo V/PV neste lote", cnpjsSemArquivo.Count);

        var resumo = new ResumoClientesSemArquivo();

        foreach (var cnpj in cnpjsSemArquivo)
        {
            try
            {
                await ProcessarClienteAsync(cnpj, dataD1, ct);
                resumo.Processados++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar cliente sem arquivo {Cnpj}", cnpj);
                resumo.Falhas++;
            }
        }

        return resumo;
    }

    // Sem lock por CNPJ aqui de propósito: este laço roda sequencial
    // (foreach) e só começa depois que ProcessarArquivosVePvPipeline já
    // terminou — sem concorrência intra-execução. Se esse laço for
    // paralelizado no futuro, o lock (igual ao de
    // ProcessadorArquivoRetornoService) volta a ser necessário.
    private async Task ProcessarClienteAsync(string cnpj, DateOnly dataD1, CancellationToken ct)
    {
        var (pendenciasConvertidas, chaves) =
            await pendenciasFactory.ObterPendenciasConvertidasAsync(cnpj, dataD1, ct);

        if (pendenciasConvertidas.Count == 0)
        {
            logger.LogInformation("Cliente {Cnpj} sem pendências D-1 — nada a gerar", cnpj);
            return;
        }

        var header = new HeaderSintetico(_opt.BancoPadrao, cnpj, NomeEmpresa: "");
        var dadosSinteticos = mesclagem.MontarSintetico(header, pendenciasConvertidas);

        // Mesmo controle de sequencial do fluxo principal — aqui é ainda
        // mais necessário, já que não há arquivo V de onde herdar número
        // nenhum (o sintético nasceria com 0).
        var sequencial = await sequenciais.ReservarProximoAsync(cnpj, ct);
        var dadosFinais = mesclagem.AplicarSequencial(dadosSinteticos, sequencial);

        var jsonSerializado = JsonSerializer.SerializeToUtf8Bytes(dadosFinais, JsonOpcoesSaida);

        // Mesmo registro do fluxo principal: a linha em Cobranca.Arquivo
        // nasce aqui e seu ID é a correlação que o Robô 2 vai receber.
        var nomeArquivoRetorno = ProcessadorArquivoRetornoService.MontarNomeArquivoRetorno(cnpj);
        var arquivoId = await arquivos.RegistrarEnvioParaConversaoAsync(
            nomeArquivoRetorno, cnpj, dadosFinais, ct);

        ConvertAsyncUploadIniciado resultadoConversao;
        try
        {
            resultadoConversao = await conversor.ConverterJsonParaCnabAsync(
                jsonSerializado, $"{nomeArquivoRetorno}.json", arquivoId.ToString(), ct);
        }
        catch
        {
            await arquivos.RemoverAsync(arquivoId, ct);
            throw;
        }

        controlePendencias.RegistrarReportadas(chaves);

        logger.LogInformation(
            "Cliente sem arquivo {Cnpj} processado — Sequencial {Sequencial}, ArquivoID {ArquivoID}, JobId {JobId}",
            cnpj, sequencial, arquivoId, resultadoConversao.JobId);
    }
}

public class ResumoClientesSemArquivo
{
    public int Processados { get; set; }
    public int Falhas { get; set; }
}
