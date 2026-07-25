namespace CnabRetorno.RetornoCron.Worker;

public class CronOptions
{
    public const string Secao = "Worker";

    /// <summary>
    /// "CronJob": executa uma vez e encerra (frequência controlada pelo
    /// K8s — arquivos geralmente chegam por volta das 6h, então o
    /// CronJob roda uma vez nesse horário). "Loop": processo residente
    /// com agendamento interno via expressão cron.
    /// </summary>
    public string Modo { get; set; } = "CronJob";

    /// <summary>Expressão cron usada apenas no modo Loop.</summary>
    public string Cron { get; set; } = "0 6 * * *";
}
