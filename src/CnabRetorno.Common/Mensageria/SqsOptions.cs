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

    /// <summary>
    /// Filas por apelido lógico, resolvidas em configuração — nunca nome
    /// de fila fixo em código. Cada ambiente aponta o mesmo apelido pra
    /// uma fila diferente (<c>Sqs:Filas:ConversorValido</c> em
    /// appsettings, sobrescrito por <c>Sqs__Filas__ConversorValido</c>
    /// como variável de ambiente no cluster).
    /// </summary>
    public Dictionary<string, string> Filas { get; set; } = [];

    /// <exception cref="InvalidOperationException">Apelido sem fila
    /// configurada — falha no start, não na primeira mensagem.</exception>
    public string ResolverFila(string apelido)
        => Filas.TryGetValue(apelido, out var nome) && !string.IsNullOrWhiteSpace(nome)
            ? nome
            : throw new InvalidOperationException(
                $"Fila '{apelido}' não configurada — definir Sqs:Filas:{apelido} " +
                $"(ou a variável de ambiente Sqs__Filas__{apelido}).");
}

public sealed record SqsTopologia(
    string NomeFila,
    int MaxNumberOfMessages = 10, // teto do SQS
    int WaitTimeSeconds = 20, // long-polling máximo do SQS
    int VisibilityTimeoutSeconds = 120);
