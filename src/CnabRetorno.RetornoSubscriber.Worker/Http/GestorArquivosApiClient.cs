using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;

namespace CnabRetorno.RetornoSubscriber.Worker.Http;

/// <summary>Corpo idêntico pros dois endpoints de presign — só
/// <c>appId</c> + <c>id</c> (Guid), sem tempo de expiração: o client real
/// da cash-cobranca-api (<c>ArquivoApiClient</c>) não envia esse campo.</summary>
internal sealed record PresignRequest(string AppId, Guid Id);

/// <summary>
/// Implementação real da API Gestor Arquivo — só presigned URLs, nunca
/// acesso direto ao S3 (docs/cash-cobranca-referencia.md §3, §5.5).
/// </summary>
public class GestorArquivosApiClient(HttpClient httpClient)
    : HttpApiClientBase(httpClient), IGestorArquivosApiClient
{
    public Task<PresignResposta> PresignUploadAsync(string appId, Guid id, CancellationToken ct)
        => PostJsonAsync<PresignRequest, PresignResposta>(
            "/presign/upload", new PresignRequest(appId, id), ct);

    public Task<PresignResposta> PresignDownloadAsync(string appId, Guid id, CancellationToken ct)
        => PostJsonAsync<PresignRequest, PresignResposta>(
            "/presign/download", new PresignRequest(appId, id), ct);
}
