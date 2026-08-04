using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.Core.Aplicacao;

/// <summary>
/// Contrato do conversor de layout (API externa). Só a interface mora
/// aqui — a implementação concreta (HttpClient real, base URL, auth,
/// nome de pipeline) é infraestrutura do worker que chama a API; ver
/// docs/evoluindo-com-libs-externas.md sobre por que a implementação não
/// entra no domínio compartilhado.
///
/// Um método só: dos dois robôs atuais, apenas o Robô 2 converte. O Robô 1
/// é ingestão pura (renomeia, guarda, registra) — a conversão da remessa
/// de VAN é responsabilidade de outro worker do ecossistema.
/// </summary>
public interface ILayoutConversaoApiClient
{
    /// <summary>
    /// <c>POST /v1/convert/sync/upload</c> (multipart) — envia o JSON do
    /// retorno de pagamentos e recebe de volta o CNAB240 no mesmo
    /// request. Síncrono de propósito: o arquivo precisa estar pronto pra
    /// ser guardado e registrado dentro da mesma janela de execução, sem
    /// depender de uma conclusão que chega por fila depois.
    /// </summary>
    /// <param name="id">O <c>ArquivoID</c> da linha em
    /// <c>Pagamento.Arquivo</c> — o mesmo identificador usado no storage,
    /// nunca um GUID novo por chamada.</param>
    Task<ConvertSyncUploadResponse> ConverterJsonParaCnabAsync(
        byte[] jsonSerializado, string nomeArquivo, string id, CancellationToken ct);
}

/// <summary>Envelope voltou com <c>success: false</c> — falha isolada do
/// arquivo, não derruba a janela inteira.</summary>
public sealed class ConversaoCnabFalhouException(string appId, string id)
    : Exception($"Conversão síncrona falhou (appId={appId}, id={id}, success=false).");
