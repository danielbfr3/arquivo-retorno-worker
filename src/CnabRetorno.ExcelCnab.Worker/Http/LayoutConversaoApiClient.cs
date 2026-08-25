using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Http;

public class ConversaoOptions
{
    public const string Secao = "Conversao";

    /// <summary>AppID da chamada. <c>cash-cobranca</c> é o valor deste
    /// fluxo — o mesmo gravado em <c>Cobranca.Arquivo.AppID</c>.</summary>
    public string AppId { get; set; } = "cash-cobranca";

    /// <summary>Pipeline de conversão da planilha em CNAB.</summary>
    public string Pipeline { get; set; } = "excel-cnab";

    /// <summary>Nome do campo do multipart que carrega o JSON com os dados
    /// do cliente.
    /// TODO(a-confirmar): o contrato registrado em
    /// docs/cash-cobranca-referencia.md §2.4 lista só
    /// <c>file</c>/<c>appId</c>/<c>pipeline</c>/<c>id</c> — o nome do
    /// campo de metadados não foi informado. <c>metadata</c> é
    /// placeholder; se o conversor esperar outro nome, é uma chave de
    /// configuração, não código.</summary>
    public string CampoMetadados { get; set; } = "metadata";
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

    /// <summary>Content-type por extensão: <c>.xls</c> é o formato binário
    /// antigo e <c>.xlsx</c> é OOXML — formatos diferentes, não só
    /// extensões diferentes. Mandar tudo como octet-stream obrigaria o
    /// pipeline a adivinhar pelo nome.</summary>
    private static string ContentTypeDe(string nomeArquivo) =>
        Path.GetExtension(nomeArquivo).ToLowerInvariant() switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            _ => "application/octet-stream",
        };

    public async Task<ConvertAsyncUploadResponse> EnviarParaConversaoAsync(
        byte[] arquivo,
        string nomeArquivo,
        Guid arquivoId,
        string metadadosCliente,
        CancellationToken ct)
    {
        var resposta = await PostMultipartAsync<ConvertAsyncUploadResponse>(
            "/v1/convert/async/upload", arquivo, nomeArquivo, ContentTypeDe(nomeArquivo),
            new Dictionary<string, string>
            {
                ["appId"] = _opt.AppId,
                ["pipeline"] = _opt.Pipeline,
                // Guid "D" (com hífens) — é como o id circula no
                // ecossistema e como volta na mensagem de conclusão.
                ["id"] = arquivoId.ToString(),
                [_opt.CampoMetadados] = metadadosCliente,
            }, ct);

        if (!resposta.Aceito)
            throw new ConversaoNaoAceitaException(resposta.AppId, resposta.Id, resposta.Status);

        return resposta;
    }
}
