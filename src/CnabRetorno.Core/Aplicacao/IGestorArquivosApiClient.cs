namespace CnabRetorno.Core.Aplicacao;

/// <summary>
/// Resposta de <c>POST /presign/upload</c> ou <c>POST /presign/download</c>
/// — mesmo shape pros dois, ver docs/cash-cobranca-referencia.md §3.3.
/// </summary>
public sealed record PresignResposta(
    string Method,
    string Url,
    string AppId,
    string Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Contrato real da API Gestor Arquivo (abstração do S3 usada por todo o
/// ecossistema CASH — ver docs/cash-cobranca-referencia.md §3). Não existe
/// "registrar arquivo": o storage é resolvido inteiramente por presigned
/// URLs — o chamador pede uma URL assinada, faz o PUT/GET diretamente nela.
/// Acesso direto ao S3 não é o padrão real (§5.5), por isso nenhum
/// implementador desta interface deve usar um SDK de S3.
///
/// O <paramref name="id"/> é o <c>ArquivoID</c> da linha em
/// <c>Cobranca.Arquivo</c> — o mesmo identificador em toda a cadeia
/// (registro, conversão, storage), não um GUID novo por chamada.
/// </summary>
public interface IGestorArquivosApiClient
{
    Task<PresignResposta> PresignUploadAsync(string appId, Guid id, CancellationToken ct);

    Task<PresignResposta> PresignDownloadAsync(string appId, Guid id, CancellationToken ct);
}
