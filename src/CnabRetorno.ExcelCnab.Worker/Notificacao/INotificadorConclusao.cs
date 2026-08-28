namespace CnabRetorno.ExcelCnab.Worker.Notificacao;

/// <summary>
/// Avisa que o processamento de uma planilha terminou.
///
/// Duas implementações: <see cref="SnsNotificadorConclusao"/>, que publica
/// no tópico, e <see cref="NotificadorDesligado"/>, registrada quando
/// <c>Notificacao:Habilitado=false</c>. O desligado é um no-op de
/// verdade (padrão Null Object) — assim o processador tem sempre a mesma
/// forma, sem <c>if</c> de configuração no meio do fluxo.
/// </summary>
public interface INotificadorConclusao
{
    Task NotificarAsync(PlanilhaEnviadaEvento evento, CancellationToken ct);
}

/// <summary>No-op usado quando o aviso está desligado.</summary>
public class NotificadorDesligado : INotificadorConclusao
{
    public Task NotificarAsync(PlanilhaEnviadaEvento evento, CancellationToken ct)
        => Task.CompletedTask;
}
