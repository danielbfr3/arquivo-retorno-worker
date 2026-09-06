using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.Core.Aplicacao;

/// <summary>
/// Contrato do conversor de layout (API externa). Só a interface mora
/// aqui — a implementação concreta (HttpClient real, base URL, auth, nome
/// de pipeline, nome do campo de metadados) é infraestrutura do worker que
/// chama a API; ver docs/evoluindo-com-libs-externas.md sobre por que a
/// implementação não entra no domínio compartilhado.
/// </summary>
public interface ILayoutConversaoApiClient
{
    /// <summary>
    /// <c>POST /v1/convert/async/upload</c> (multipart) — envia a planilha
    /// do cliente e volta na hora com o job aceito; o resultado da
    /// conversão chega depois por fila, correlacionado pelo
    /// <paramref name="arquivoId"/>.
    ///
    /// Assíncrono de propósito: o robô não espera o CNAB ficar pronto.
    /// A linha em <c>Cobranca.Arquivo</c> criada antes da chamada é o que
    /// permite a quem consome a conclusão recuperar cliente e nome do
    /// arquivo — ver docs/cash-cobranca-referencia.md §2.4.
    /// </summary>
    /// <param name="arquivoId">O <c>ArquivoID</c> da linha em
    /// <c>Cobranca.Arquivo</c> — o mesmo identificador em toda a cadeia,
    /// nunca um GUID novo por chamada.</param>
    /// <param name="metadadosCliente">JSON já serializado com os dados do
    /// cliente (CNPJ e razão social, ambos derivados de
    /// <c>Cobranca.DocumentoDados</c>), enviado como campo de texto do
    /// multipart. Ver <see cref="MetadadosCliente"/>.</param>
    Task<ConvertAsyncUploadResponse> EnviarParaConversaoAsync(
        byte[] arquivo,
        string nomeArquivo,
        Guid arquivoId,
        string metadadosCliente,
        CancellationToken ct);
}

/// <summary>A API aceitou o request mas devolveu um status que não é de
/// job aceito — falha isolada do arquivo, não derruba a varredura.</summary>
public sealed class ConversaoNaoAceitaException(string appId, string id, string? status)
    : Exception($"Conversão assíncrona não foi aceita (appId={appId}, id={id}, status={status ?? "<nulo>"}).");
