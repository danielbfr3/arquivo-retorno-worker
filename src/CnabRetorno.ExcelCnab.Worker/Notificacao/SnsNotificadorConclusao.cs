using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Notificacao;

/// <summary>
/// Publica o aviso de conclusão como JSON no tópico SNS.
///
/// O corpo vai como texto puro (<c>Message</c>), que é o que o SNS
/// transporta — o JSON é do payload, não do envelope. Nenhum
/// <c>MessageStructure = "json"</c>: esse modo é pra mandar corpos
/// diferentes por protocolo de assinatura, e exigiria o payload embrulhado
/// num objeto com chave <c>default</c>.
/// </summary>
public class SnsNotificadorConclusao(
    IAmazonSimpleNotificationService sns,
    IOptions<NotificacaoOptions> opcoes) : INotificadorConclusao
{
    private readonly NotificacaoOptions _opt = opcoes.Value;

    public Task NotificarAsync(PlanilhaEnviadaEvento evento, CancellationToken ct)
        => sns.PublishAsync(new PublishRequest
        {
            TopicArn = _opt.TopicoArn,
            Subject = _opt.Assunto,
            Message = evento.Serializar(),
        }, ct);
}
