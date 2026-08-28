namespace CnabRetorno.ExcelCnab.Worker.Notificacao;

/// <summary>
/// Configuração do aviso de conclusão no SNS. **Uma seção só**
/// (<c>Notificacao</c>), pelo mesmo motivo do armazenamento: desativar é
/// mudar uma chave, e remover é apagar esta seção junto com a pasta
/// <c>Notificacao/</c>. Ver "Como desativar / como remover" em
/// docs/regras-de-negocio.md.
/// </summary>
public class NotificacaoOptions
{
    public const string Secao = "Notificacao";

    /// <summary><c>false</c> desliga o aviso: nenhum cliente de SNS é
    /// criado e o passo vira no-op.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>ARN do tópico. Nome de recurso de infra é sempre
    /// configuração — nunca literal em código.</summary>
    public string TopicoArn { get; set; } = default!;

    public string Region { get; set; } = "sa-east-1";

    /// <summary>Endpoint alternativo pra LocalStack em dev.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Assunto da mensagem (opcional no SNS; aparece no e-mail
    /// quando o tópico tem assinatura por e-mail).</summary>
    public string? Assunto { get; set; } = "Planilha enviada para conversão";
}
