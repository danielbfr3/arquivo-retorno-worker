using Amazon.S3;
using Amazon.S3.Model;
using CnabRetorno.Core.Aplicacao;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RemessaVan.Worker.Storage;

public class ArmazenamentoOptions
{
    public const string Secao = "Storage";

    /// <summary>"GestorArquivos" (padrão) ou "S3".
    ///
    /// As duas versões existem por pedido explícito. O padrão é o Gestor
    /// de Arquivos: é o caminho oficial do ecossistema CASH, e
    /// docs/cash-cobranca-referencia.md §5.5 registra que acesso direto ao
    /// S3 não é o padrão. O modo "S3" existe pra ambiente onde o Gestor
    /// ainda não esteja disponível — na extração de 03/08/2026 ele estava
    /// falhando em AWS e em GCP.</summary>
    public string Modo { get; set; } = "GestorArquivos";

    public S3Options S3 { get; set; } = new();
}

public class S3Options
{
    /// <summary>Bucket de destino — parametrizável, como pedido.</summary>
    public string Bucket { get; set; } = default!;

    /// <summary>Prefixo (pasta) dentro do bucket. Vazio grava na raiz.</summary>
    public string Prefixo { get; set; } = string.Empty;

    public string Region { get; set; } = "sa-east-1";

    /// <summary>Endpoint alternativo pra LocalStack/MinIO em dev.</summary>
    public string? ServiceUrl { get; set; }
}

/// <summary>
/// Grava direto no S3 via <c>PutObject</c>. Alternativa ao
/// <c>GestorArquivoStorage</c>, selecionada por <c>Storage:Modo</c>.
///
/// A chave do objeto inclui o <c>ArquivoID</c> além do nome: dois
/// clientes podem mandar arquivos de mesmo nome no mesmo dia, e a chave
/// precisa bater com o identificador que ficou registrado no banco.
/// </summary>
public class S3Storage(IAmazonS3 s3, IOptions<ArmazenamentoOptions> opcoes) : IArmazenamentoArquivo
{
    private readonly S3Options _opt = opcoes.Value.S3;

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
