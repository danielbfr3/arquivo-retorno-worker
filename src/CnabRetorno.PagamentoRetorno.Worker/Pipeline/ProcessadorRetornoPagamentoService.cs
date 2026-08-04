using System.Text.Json;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using CnabRetorno.PagamentoRetorno.Worker.Json;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;

namespace CnabRetorno.PagamentoRetorno.Worker.Pipeline;

/// <summary>
/// Gera, converte, guarda e registra o arquivo de retorno de **um**
/// cliente numa janela.
///
/// Ordem das escritas, e o porquê de cada uma:
/// <list type="number">
///   <item>Reserva o NSA (atômico) — se falhar, nada foi criado.</item>
///   <item>Cria a linha em <c>Pagamento.Arquivo</c> pra ter o
///   <c>ArquivoID</c>, que é o id usado no conversor e no storage.</item>
///   <item>Converte e guarda. Qualquer falha daqui pra trás remove a
///   linha: melhor não ter registro do que registrar um arquivo que não
///   existe.</item>
///   <item>Marca a linha como registrada e só então avança a marca
///   d'água. Se o processo morrer entre os dois, o próximo parcial
///   reenvia — duplicar é recuperável, perder não.</item>
/// </list>
/// </summary>
public class ProcessadorRetornoPagamentoService(
    SequencialArquivoRepository sequenciais,
    ArquivoRepository arquivos,
    MontagemRetornoPagamento montagem,
    ILayoutConversaoApiClient conversor,
    IArmazenamentoArquivo armazenamento,
    ControleJanelaRepository controleJanela,
    ILogger<ProcessadorRetornoPagamentoService> logger)
{
    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task ProcessarAsync(
        MovimentacoesDoCliente cliente, Ocorrencia janela, DateOnly dia, CancellationToken ct)
    {
        var tipoNome = janela.Tipo == TipoJanela.Consolidado
            ? TipoJanelaNome.Consolidado
            : TipoJanelaNome.Parcial;

        var sequencial = await sequenciais.ReservarProximoAsync(cliente.Documento, ct);
        var dados = montagem.Montar(cliente, sequencial, janela.Momento);
        var nomeArquivo = montagem.MontarNomeArquivo(cliente.Documento, tipoNome, janela.Momento);

        var arquivoId = await arquivos.RegistrarGeracaoAsync(
            nomeArquivo, cliente.Documento, cliente.TipoDocumento, cliente.ContaHeader, ct);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(dados, JsonOpcoes);

            var conversao = await conversor.ConverterJsonParaCnabAsync(
                json, $"{nomeArquivo}.json", arquivoId.ToString(), ct);

            var armazenado = await armazenamento.ArmazenarAsync(
                arquivoId, nomeArquivo, conversao.ConteudoCnab(), ct);

            await arquivos.MarcarRegistradoAsync(arquivoId, ct);

            logger.LogInformation(
                "Retorno {Tipo} de pagamentos gerado pro cliente {Documento}: {Nome} (ArquivoID {ArquivoId}, NSA {Nsa}, {Lotes} lote(s), {Movimentacoes} movimentação(ões)) em {Destino}",
                tipoNome, cliente.Documento, nomeArquivo, arquivoId, sequencial,
                dados.Lotes.Count, cliente.Movimentacoes.Count, armazenado.Destino);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await arquivos.RemoverAsync(arquivoId, ct);
            logger.LogError(ex,
                "Falha ao gerar o retorno do cliente {Documento} na janela {Janela:o} — linha {ArquivoId} removida; o NSA {Nsa} fica consumido",
                cliente.Documento, janela.Momento, arquivoId, sequencial);
            throw;
        }

        // Só depois do arquivo existir de fato. Avançar antes faria uma
        // falha de conversão descartar as movimentações pra sempre.
        await controleJanela.RegistrarAsync(cliente.Documento, dia, cliente.UltimoInstante, ct);
    }
}
