namespace CnabRetorno.Common.Mensageria;

/// <summary>
/// Contrato de handler de mensagem — mesma forma usada no anexo do
/// documento de tarefa (<c>IMessageService&lt;ArquivoRetornoMessage&gt;</c>
/// no `ProcessarArquivoRetornoService` real da empresa). Quem implementa
/// esta interface é a classe de aplicação que processa a mensagem; a
/// infraestrutura de broker (<see cref="SqsConsumerHostedService{TMessage}"/>)
/// resolve a implementação via DI e chama <see cref="ProcessAsync"/> pra
/// cada mensagem recebida.
/// </summary>
public interface IMessageService<TMessage>
{
    Task ProcessAsync(TMessage message, CancellationToken cancellationToken);
}
