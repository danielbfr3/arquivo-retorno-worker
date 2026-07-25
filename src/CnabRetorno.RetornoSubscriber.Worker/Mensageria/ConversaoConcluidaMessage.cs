namespace CnabRetorno.RetornoSubscriber.Worker.Mensageria;

/// <summary>Corpo de <see cref="ConversaoConcluidaMessage.Data"/> — só o
/// que este worker usa; campos extras que a mensagem real traga são
/// ignorados na desserialização.</summary>
public sealed record ConversaoConcluidaDados(string? OutputUrl);

/// <summary>
/// Mensagem SQS de conclusão do job assíncrono (JSON → CNAB no layout do
/// cliente, disparado pelo Robô 1).
///
/// <see cref="Id"/> é o <c>ArquivoID</c> da linha que o Robô 1 criou em
/// <c>Cobranca.Arquivo</c> antes de enviar — a API de conversão só ecoa de
/// volta o mesmo <c>id</c> que recebeu. É por ele que este worker
/// reencontra o cliente (ver <c>Persistencia.ArquivoRepository</c>).
/// <see cref="ConversaoConcluidaDados.OutputUrl"/> é a URL assinada de
/// onde baixar o CNAB final.
/// </summary>
public sealed record ConversaoConcluidaMessage(
    string Id,
    bool Success,
    ConversaoConcluidaDados? Data);
