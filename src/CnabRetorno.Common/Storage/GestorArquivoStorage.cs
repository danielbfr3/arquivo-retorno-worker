using CnabRetorno.Core.Aplicacao;
using Microsoft.Extensions.Options;

namespace CnabRetorno.Common.Storage;

public class GestorArquivoOptions
{
    public const string Secao = "GestorArquivo";

    /// <summary>AppID com que o worker se identifica no Gestor Arquivo.
    /// Não é o mesmo valor pros dois robôs: a extração de 03/08/2026
    /// mostra <c>cash-cobranca-arquivo-van</c> no fluxo de remessa de VAN,
    /// enquanto o fluxo de cobrança usa <c>cash-cobranca</c> — por isso
    /// vem de configuração, e não de constante.</summary>
    public string AppId { get; set; } = default!;
}

/// <summary>
/// Armazena o arquivo via presigned URL da API Gestor Arquivo — nunca
/// acesso direto ao S3 (docs/cash-cobranca-referencia.md §5.5). Upload e
/// "registro" são uma única operação: pedir a URL assinada e fazer o PUT
/// nela; não existe endpoint separado de "registrar arquivo".
/// </summary>
public class GestorArquivoStorage(
    IGestorArquivosApiClient gestorArquivos,
    HttpClient httpUpload,
    IOptions<GestorArquivoOptions> opcoes) : IArmazenamentoArquivo
{
    private readonly GestorArquivoOptions _opt = opcoes.Value;

    public async Task<ArquivoArmazenado> ArmazenarAsync(
        Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct)
    {
        var presign = await gestorArquivos.PresignUploadAsync(_opt.AppId, arquivoId, ct);

        using var conteudoHttp = new ByteArrayContent(conteudo);
        conteudoHttp.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var resposta = await httpUpload.PutAsync(presign.Url, conteudoHttp, ct);
        resposta.EnsureSuccessStatusCode();

        return new ArquivoArmazenado("GestorArquivos", presign.Id);
    }
}
