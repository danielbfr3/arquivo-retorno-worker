using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.RetornoCron.Worker.Http;

/// <summary>
/// Único lugar do Robô 1 que conhece o formato real da API de conversão
/// (endpoints /v1/convert/sync/upload e /v1/convert/async/upload) — mesma
/// regra de adaptador único documentada em docs/evoluindo-com-libs-externas.md,
/// aplicada aqui a uma API HTTP em vez de uma lib.
/// </summary>
public class LayoutConversaoApiClient(HttpClient httpClient)
    : HttpApiClientBase(httpClient), ILayoutConversaoApiClient
{
    private const string AppId = "cash-cobranca";
    private const string PipelineCnabParaJson = "conversao-cobranca-retorno-para-json";

    // TODO(a-confirmar): nenhum exemplo forneceu o nome do pipeline
    // reverso (JSON → CNAB no layout do cliente).
    private const string PipelineJsonParaCnab = "TODO-confirmar-pipeline-json-para-cnab";

    public async Task<ConvertSyncUploadResponse> ConverterCnabParaJsonAsync(
        byte[] conteudoCnab, string nomeArquivo, string id, CancellationToken ct)
    {
        var resposta = await PostMultipartAsync<ConvertSyncUploadResponse>(
            "/v1/convert/sync/upload", conteudoCnab, nomeArquivo,
            new Dictionary<string, string>
            {
                ["appId"] = AppId,
                ["pipeline"] = PipelineCnabParaJson,
                ["id"] = id,
            }, ct);

        if (!resposta.Success)
            throw new ConversaoCnabFalhouException(resposta.AppId, resposta.Id);

        return resposta;
    }

    public Task<ConvertAsyncUploadIniciado> ConverterJsonParaCnabAsync(
        byte[] jsonSerializado, string nomeArquivo, string id, CancellationToken ct)
        => PostMultipartAsync<ConvertAsyncUploadIniciado>(
            "/v1/convert/async/upload", jsonSerializado, nomeArquivo,
            new Dictionary<string, string>
            {
                ["appId"] = AppId,
                ["pipeline"] = PipelineJsonParaCnab,
                ["id"] = id,
            }, ct);
}

/// <summary>Envelope síncrono voltou com <c>success: false</c> — falha
/// isolada do arquivo (não derruba o lote), mesmo tratamento de
/// <c>DadosConvertidosDivergentesException</c>.</summary>
public sealed class ConversaoCnabFalhouException(string appId, string id)
    : Exception($"Conversão síncrona falhou (appId={appId}, id={id}, success=false).");
