using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>
/// Grava a cópia direto no bucket via <c>PutObject</c>.
///
/// A chave do objeto inclui o <c>ArquivoID</c> além do nome: dois clientes
/// podem mandar planilhas de mesmo nome no mesmo dia, e a chave precisa
/// bater com o identificador que ficou registrado no banco.
/// </summary>
public class S3Storage(IAmazonS3 s3, IOptions<ArmazenamentoOptions> opcoes) : IArmazenamentoArquivo
{
    private readonly S3Destino _opt = opcoes.Value.S3;

    public async Task<ArquivoArmazenado> ArmazenarAsync(
        Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct)
    {
        var chave = string.IsNullOrWhiteSpace(_opt.Prefixo)
            ? $"{arquivoId}/{nomeArquivo}"
            : $"{_opt.Prefixo.TrimEnd('/')}/{arquivoId}/{nomeArquivo}";

        using var fluxo = new MemoryStream(conteudo, writable: false);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _opt.Bucket,
            Key = chave,
            InputStream = fluxo,
            ContentType = "application/octet-stream",
        }, ct);

        return new ArquivoArmazenado("S3", $"s3://{_opt.Bucket}/{chave}");
    }
}
