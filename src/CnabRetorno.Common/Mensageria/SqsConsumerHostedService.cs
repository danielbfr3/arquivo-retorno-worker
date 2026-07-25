using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CnabRetorno.Common.Mensageria;

/// <summary>
/// Consumidor genérico via long-polling SQS — resolve <see
/// cref="IMessageService{TMessage}"/> num escopo de DI próprio por
/// mensagem, só confirma (delete) depois do handler ter sucesso.
///
/// Falha no handler → mensagem não é deletada, volta a ficar visível
/// sozinha depois de <see cref="SqsTopologia.VisibilityTimeoutSeconds"/>
/// — risco de loop infinito em falha permanente; política de retry/DLQ
/// ainda TODO(a-confirmar).
/// </summary>
public sealed class SqsConsumerHostedService<TMessage>(
    IAmazonSQS sqsClient,
    IServiceScopeFactory scopeFactory,
    SqsTopologia topologia,
    ILogger<SqsConsumerHostedService<TMessage>> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var queueUrl = (await sqsClient.GetQueueUrlAsync(topologia.NomeFila, ct)).QueueUrl;

        while (!ct.IsCancellationRequested)
        {
            ReceiveMessageResponse resposta;
            try
            {
                resposta = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = topologia.MaxNumberOfMessages,
                    WaitTimeSeconds = topologia.WaitTimeSeconds,
                    VisibilityTimeout = topologia.VisibilityTimeoutSeconds,
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao consultar SQS {Fila} — nova tentativa em 5s", topologia.NomeFila);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            foreach (var mensagem in resposta.Messages)
                await ProcessarMensagemAsync(mensagem, queueUrl, ct);
        }
    }

    private async Task ProcessarMensagemAsync(Message mensagemSqs, string queueUrl, CancellationToken ct)
    {
        try
        {
            var mensagem = JsonSerializer.Deserialize<TMessage>(mensagemSqs.Body, JsonOpcoes)
                ?? throw new InvalidOperationException(
                    $"Corpo da mensagem vazio ou inválido pra {typeof(TMessage).Name}.");

            using var escopo = scopeFactory.CreateScope();
            var handler = escopo.ServiceProvider.GetRequiredService<IMessageService<TMessage>>();
            await handler.ProcessAsync(mensagem, ct);

            await sqsClient.DeleteMessageAsync(queueUrl, mensagemSqs.ReceiptHandle, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Falha ao processar mensagem {Tipo} da fila {Fila} — sem delete, volta a " +
                "ficar visível após {Segundos}s (equivalente a nack+requeue)",
                typeof(TMessage).Name, topologia.NomeFila, topologia.VisibilityTimeoutSeconds);
        }
    }
}
