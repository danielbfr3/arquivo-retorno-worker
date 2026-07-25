using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.Core.Aplicacao;

/// <summary>
/// Contrato do conversor CNAB ↔ JSON (API externa, endpoints /v1/convert/sync
/// e /v1/convert/async citados no documento de tarefa). Só a interface mora
/// aqui — a implementação concreta (HttpClient real, base URL, auth) é
/// infraestrutura do Robô 1, que é quem de fato chama esta API; ver
/// docs/evoluindo-com-libs-externas.md sobre por que a implementação não
/// entra no domínio compartilhado.
/// </summary>
public interface ILayoutConversaoApiClient
{
    /// <summary>POST /v1/convert/sync/upload (multipart) — usado uma vez
    /// pro V e, se existir, uma vez pro PV (chamadas separadas — a
    /// mesclagem dos dois acontece depois, a nível de JSON, ver
    /// <c>Json.MesclagemDadosConvertidos</c>). <paramref name="id"/> é a
    /// correlação escolhida pelo chamador — reusar o mesmo valor nas
    /// chamadas relacionadas ao mesmo arquivo/cliente (sync-V, sync-PV,
    /// async) ajuda a rastrear ponta-a-ponta do lado da API externa.</summary>
    Task<ConvertSyncUploadResponse> ConverterCnabParaJsonAsync(
        byte[] conteudoCnab, string nomeArquivo, string id, CancellationToken ct);

    /// <summary>POST /v1/convert/async/upload (multipart) — envia o JSON
    /// combinado (V+PV+pendências) pra virar CNAB no layout do cliente.
    /// Retorna imediatamente com o identificador do job; o resultado chega
    /// depois via SQS (consumido pelo Robô 2).</summary>
    Task<ConvertAsyncUploadIniciado> ConverterJsonParaCnabAsync(
        byte[] jsonSerializado, string nomeArquivo, string id, CancellationToken ct);
}
