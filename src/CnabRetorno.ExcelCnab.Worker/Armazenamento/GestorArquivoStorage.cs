using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>
/// Guarda a cópia via presigned URL da API Gestor Arquivo. Pedir a URL
/// assinada e fazer o PUT nela é uma operação só — não existe endpoint
/// separado de "registrar arquivo".
///
/// São **dois** <see cref="HttpClient"/> de propósito, e isso não é
/// detalhe de estilo: o client da API tem BaseAddress e manda a chave de
/// autenticação em todo request, enquanto a URL assinada é absoluta e
/// aponta pro S3. Reusar o mesmo client mandaria a credencial da API
/// junto do PUT, pra um host de terceiro.
/// </summary>
public class GestorArquivoStorage(
    GestorArquivosApiClient gestorArquivos,
    HttpClient httpUpload,
    IOptions<ArmazenamentoOptions> opcoes) : IArmazenamentoArquivo
{
    private readonly GestorArquivosDestino _opt = opcoes.Value.GestorArquivos;

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
