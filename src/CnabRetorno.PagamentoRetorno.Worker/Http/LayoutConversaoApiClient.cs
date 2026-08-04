using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using Microsoft.Extensions.Options;

namespace CnabRetorno.PagamentoRetorno.Worker.Http;

public class ConversaoOptions
{
    public const string Secao = "Conversao";

    /// <summary>TODO(a-confirmar): o AppID do fluxo de pagamentos não foi
    /// informado — os conhecidos são <c>cash-cobranca</c> e
    /// <c>cash-cobranca-arquivo-van</c>.</summary>
    public string AppId { get; set; } = "cash-pagamento";

    /// <summary>TODO(a-confirmar): nome do pipeline de conversão JSON →
    /// CNAB de pagamentos. Nenhum material forneceu — o único nome de
    /// pipeline conhecido é o de remessa de cobrança
    /// (<c>conversao-cobranca-remessa-asa-cnab240-para-json</c>). Sem o
    /// valor certo a chamada é rejeitada pelo conversor.</summary>
    public string Pipeline { get; set; } = "TODO-confirmar-pipeline-pagamento-retorno";
}

/// <summary>
/// Único lugar do robô que conhece o formato real da API de conversão —
/// mesma regra de adaptador único de docs/evoluindo-com-libs-externas.md,
/// aplicada a uma API HTTP.
/// </summary>
public class LayoutConversaoApiClient(HttpClient httpClient, IOptions<ConversaoOptions> opcoes)
    : HttpApiClientBase(httpClient), ILayoutConversaoApiClient
{
    private readonly ConversaoOptions _opt = opcoes.Value;

    public async Task<ConvertSyncUploadResponse> ConverterJsonParaCnabAsync(
        byte[] jsonSerializado, string nomeArquivo, string id, CancellationToken ct)
    {
        var resposta = await PostMultipartAsync<ConvertSyncUploadResponse>(
            "/v1/convert/sync/upload", jsonSerializado, nomeArquivo,
            new Dictionary<string, string>
            {
                ["appId"] = _opt.AppId,
                ["pipeline"] = _opt.Pipeline,
                ["id"] = id,
            }, ct);

        if (!resposta.Success)
            throw new ConversaoCnabFalhouException(resposta.AppId, resposta.Id);

        return resposta;
    }
}
