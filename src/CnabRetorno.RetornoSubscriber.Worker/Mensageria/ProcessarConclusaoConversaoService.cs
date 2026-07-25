using CnabRetorno.Common.Mensageria;
using CnabRetorno.RetornoSubscriber.Worker.Persistencia;
using CnabRetorno.RetornoSubscriber.Worker.Storage;
using Microsoft.Extensions.Logging;

namespace CnabRetorno.RetornoSubscriber.Worker.Mensageria;

/// <summary>
/// Handler do Robô 2, disparado a cada <see cref="ConversaoConcluidaMessage"/>
/// recebida via SQS. Implementa <see cref="IMessageService{TMessage}"/>.
///
/// O fluxo inteiro gira em torno do <c>Id</c> da mensagem: ele é o
/// <c>ArquivoID</c> que o Robô 1 registrou em <c>Cobranca.Arquivo</c>
/// antes de mandar pro conversor, então serve pra (a) recuperar os dados
/// do cliente, (b) identificar o objeto no Gestor de Arquivos (mesmo id no
/// presign) e (c) marcar a linha como registrada no fim.
///
/// Sem persistência própria: a única escrita é o avanço de status/etapa da
/// linha que já existe. Idempotência de redelivery fica por conta do
/// delete manual do <see cref="Common.Mensageria.SqsConsumerHostedService{TMessage}"/>
/// (sem delete, a mensagem reaparece após o visibility timeout) — como o
/// id do presign é determinístico, reprocessar sobrescreve o mesmo objeto
/// em vez de duplicar.
/// </summary>
public class ProcessarConclusaoConversaoService(
    HttpClient httpDownload,
    GestorArquivoStorage storage,
    ArquivoRepository arquivos,
    ILogger<ProcessarConclusaoConversaoService> logger) : IMessageService<ConversaoConcluidaMessage>
{
    public async Task ProcessAsync(ConversaoConcluidaMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processando conclusão de conversão. Id={Id} Sucesso={Success}", message.Id, message.Success);

        if (!message.Success)
        {
            logger.LogError("Conversão Id={Id} não teve sucesso", message.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Data?.OutputUrl))
            throw new InvalidOperationException(
                $"Mensagem do Id {message.Id} com sucesso mas sem OutputUrl.");

        if (!Guid.TryParse(message.Id, out var arquivoId))
            throw new InvalidOperationException(
                $"Id '{message.Id}' não é um ArquivoID válido (esperado o Guid registrado pelo Robô 1).");

        // Dados do cliente vêm da linha registrada pelo Robô 1 — não são
        // inferidos do nome do arquivo nem do conteúdo baixado.
        var arquivo = await arquivos.ObterPorIdAsync(arquivoId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Arquivo {arquivoId} não encontrado em Cobranca.Arquivo — mensagem sem registro correspondente.");

        var conteudo = await httpDownload.GetByteArrayAsync(message.Data.OutputUrl, cancellationToken);

        // Presign + PUT usando o próprio ArquivoID como identificador do
        // objeto — mesmo padrão do fluxo de entrada da cash-cobranca-api.
        var armazenado = await storage.ArmazenarArquivoFinalAsync(arquivoId, conteudo, cancellationToken);

        await arquivos.MarcarRegistradoAsync(arquivo, cancellationToken);

        logger.LogInformation(
            "Arquivo final registrado — ArquivoID={ArquivoID} Nome={Nome} Documento={Documento} AppId={AppId}",
            arquivoId, arquivo.ArquivoNome, arquivo.ClienteDocumento, armazenado.AppId);

        // Confirmação (delete) acontece em SqsConsumerHostedService, só
        // depois deste método retornar sem lançar exceção.
    }
}
