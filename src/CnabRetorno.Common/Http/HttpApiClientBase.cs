using System.Net.Http.Json;
using System.Text.Json;

namespace CnabRetorno.Common.Http;

/// <summary>
/// Base fina sobre HttpClient com JSON + tratamento de erro padronizado —
/// não sabe nada sobre planilhas, conversão ou arquivos. Cada API client
/// concreto (hoje só <c>ILayoutConversaoApiClient</c>) herda isto e
/// implementa os métodos específicos do seu contrato.
/// </summary>
public abstract class HttpApiClientBase(HttpClient httpClient)
{
    protected static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// POST multipart/form-data com um campo de arquivo binário ("file")
    /// mais campos de texto simples — contrato real da API de conversão
    /// (<c>/v1/convert/async|sync/upload</c>), ver
    /// docs/cash-cobranca-referencia.md §2.4.
    ///
    /// O <paramref name="contentType"/> é explícito porque o nome e o tipo
    /// do arquivo são o que o pipeline usa pra saber que planilha recebeu:
    /// <c>.xls</c> (formato binário antigo) e <c>.xlsx</c> (OOXML) são
    /// formatos diferentes, não só extensões diferentes.
    ///
    /// Só o <see cref="MultipartFormDataContent"/> externo precisa de
    /// <c>using</c>: ele descarta cada <see cref="HttpContent"/> filho
    /// adicionado via <c>Add</c> sozinho.
    /// </summary>
    protected async Task<TResposta> PostMultipartAsync<TResposta>(
        string caminho,
        byte[] arquivo,
        string nomeArquivo,
        string contentType,
        IReadOnlyDictionary<string, string> camposExtras,
        CancellationToken ct)
    {
        using var conteudo = new MultipartFormDataContent();

        var arquivoConteudo = new ByteArrayContent(arquivo);
        arquivoConteudo.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        conteudo.Add(arquivoConteudo, "file", nomeArquivo);

        foreach (var (chave, valor) in camposExtras)
            conteudo.Add(new StringContent(valor), chave);

        using var resposta = await httpClient.PostAsync(caminho, conteudo, ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<TResposta>(JsonOpcoes, ct)
            ?? throw new InvalidOperationException(
                $"Resposta vazia de {httpClient.BaseAddress}{caminho}.");
    }
}
