using System.Net.Http.Json;
using System.Text.Json;

namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>
/// Resposta de <c>POST /presign/upload</c> — ver
/// docs/cash-cobranca-referencia.md §3.3.
/// </summary>
public sealed record PresignResposta(
    string Method,
    string Url,
    string AppId,
    string Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Corpo do presign — só <c>appId</c> + <c>id</c> (Guid), sem
/// tempo de expiração: o client real da cash-cobranca-api
/// (<c>ArquivoApiClient</c>) não envia esse campo.</summary>
internal sealed record PresignRequest(string AppId, Guid Id);

/// <summary>
/// Cliente da API Gestor Arquivo — só presigned URLs, nunca acesso direto
/// ao S3 por esta via (docs/cash-cobranca-referencia.md §5.5). O bucket
/// direto é o **outro** destino, com credencial própria.
///
/// Não herda de <c>HttpApiClientBase</c> (Common) de propósito: são cinco
/// linhas de POST JSON, e depender da base faria a remoção do
/// armazenamento precisar mexer num projeto compartilhado. Aqui a pasta
/// inteira sai sem deixar rastro.
/// </summary>
public class GestorArquivosApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<PresignResposta> PresignUploadAsync(string appId, Guid id, CancellationToken ct)
    {
        using var resposta = await httpClient.PostAsJsonAsync(
            "/presign/upload", new PresignRequest(appId, id), JsonOpcoes, ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<PresignResposta>(JsonOpcoes, ct)
            ?? throw new InvalidOperationException(
                $"Resposta vazia de {httpClient.BaseAddress}/presign/upload.");
    }
}
