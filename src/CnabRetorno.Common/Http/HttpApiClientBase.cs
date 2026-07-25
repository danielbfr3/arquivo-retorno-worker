using System.Net.Http.Json;
using System.Text.Json;

namespace CnabRetorno.Common.Http;

/// <summary>
/// Base fina sobre HttpClient com JSON + tratamento de erro padronizado —
/// não sabe nada sobre CNAB, conversão ou arquivos. Cada API client
/// concreto (ILayoutConversaoApiClient, IGestorArquivosApiClient) herda
/// isto e só implementa os métodos específicos do contrato.
/// </summary>
public abstract class HttpApiClientBase(HttpClient httpClient)
{
    protected static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected async Task<TResposta> PostJsonAsync<TCorpo, TResposta>(
        string caminho, TCorpo corpo, CancellationToken ct)
    {
        using var resposta = await httpClient.PostAsJsonAsync(caminho, corpo, JsonOpcoes, ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<TResposta>(JsonOpcoes, ct)
            ?? throw new InvalidOperationException(
                $"Resposta vazia de {httpClient.BaseAddress}{caminho}.");
    }

    protected async Task PostAsync<TCorpo>(string caminho, TCorpo corpo, CancellationToken ct)
    {
        using var resposta = await httpClient.PostAsJsonAsync(caminho, corpo, JsonOpcoes, ct);
        resposta.EnsureSuccessStatusCode();
    }

    /// <summary>POST multipart/form-data com um campo de arquivo binário
    /// ("file") mais campos de texto simples — contrato real da API de
    /// conversão (<c>/v1/convert/sync|async/upload</c>), ver
    /// docs/regras-de-negocio.md. Só o <see cref="MultipartFormDataContent"/>
    /// externo precisa de <c>using</c>: ele descarta cada <see
    /// cref="HttpContent"/> filho adicionado via <c>Add</c> sozinho.</summary>
    protected async Task<TResposta> PostMultipartAsync<TResposta>(
        string caminho, byte[] arquivo, string nomeArquivo,
        IReadOnlyDictionary<string, string> camposExtras, CancellationToken ct)
    {
        using var conteudo = new MultipartFormDataContent();

        var arquivoConteudo = new ByteArrayContent(arquivo);
        arquivoConteudo.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
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
