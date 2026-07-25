using CnabRetorno.Core.Aplicacao;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoSubscriber.Worker.Storage;

public class GestorArquivoOptions
{
    public const string Secao = "GestorArquivo";

    /// <summary>AppID com que este worker se identifica no Gestor Arquivo
    /// e no conversor — "cash-cobranca", confirmado em
    /// docs/cash-cobranca-referencia.md §2.4/§3.3 (mesmo valor usado pelo
    /// Robô 1 nas chamadas de conversão).</summary>
    public string AppId { get; set; } = default!;
}

public sealed record ArquivoArmazenado(string AppId, string Id, string Url);

/// <summary>
/// Armazena o arquivo CNAB final via presigned URL da API Gestor Arquivo —
/// nunca acesso direto ao S3 (docs/cash-cobranca-referencia.md §5.5).
/// Upload e "registro" são uma única operação: pedir a URL assinada e
/// fazer o PUT nela; não existe endpoint separado de "registrar arquivo".
///
/// O identificador do objeto é o próprio <c>ArquivoID</c> da linha em
/// <c>Cobranca.Arquivo</c> — mesmo id usado no registro e na conversão.
/// Como é determinístico, um redelivery do SQS sobrescreve o mesmo objeto
/// em vez de criar um duplicado.
/// </summary>
public class GestorArquivoStorage(
    IGestorArquivosApiClient gestorArquivos, HttpClient httpUpload, IOptions<GestorArquivoOptions> opcoes)
{
    private readonly GestorArquivoOptions _opt = opcoes.Value;

    public async Task<ArquivoArmazenado> ArmazenarArquivoFinalAsync(
        Guid arquivoId, byte[] conteudo, CancellationToken ct)
    {
        var presign = await gestorArquivos.PresignUploadAsync(_opt.AppId, arquivoId, ct);

        using var conteudoHttp = new ByteArrayContent(conteudo);
        conteudoHttp.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var resposta = await httpUpload.PutAsync(presign.Url, conteudoHttp, ct);
        resposta.EnsureSuccessStatusCode();

        return new ArquivoArmazenado(presign.AppId, presign.Id, presign.Url);
    }
}
