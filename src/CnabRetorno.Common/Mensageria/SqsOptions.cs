namespace CnabRetorno.Common.Mensageria;

public class SqsOptions
{
    public const string Secao = "Sqs";

    // TODO(a-confirmar): região real não confirmada.
    public string Region { get; set; } = "sa-east-1";

    /// <summary>Endpoint alternativo pra LocalStack/dev — nulo em produção
    /// (usa o endpoint padrão da região).</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Credenciais fixas pra dev/LocalStack — nulas em produção
    /// (usa IAM role/variáveis de ambiente).</summary>
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
}

public sealed record SqsTopologia(
    string NomeFila,
    int MaxNumberOfMessages = 10, // teto do SQS
    int WaitTimeSeconds = 20, // long-polling máximo do SQS
    int VisibilityTimeoutSeconds = 120);
