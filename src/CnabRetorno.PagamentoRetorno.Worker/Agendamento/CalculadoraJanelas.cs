using Microsoft.Extensions.Options;

namespace CnabRetorno.PagamentoRetorno.Worker.Agendamento;

public class JanelaOptions
{
    public const string Secao = "Janela";

    /// <summary>Primeira geração do dia.</summary>
    public TimeSpan HoraInicio { get; set; } = new(7, 0, 0);

    /// <summary>Horário do arquivo consolidado — também o fim do
    /// expediente de geração.</summary>
    public TimeSpan HoraFim { get; set; } = new(18, 0, 0);

    /// <summary>Espaçamento entre os arquivos parciais.</summary>
    public TimeSpan IntervaloParcial { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Fuso em que os horários acima são interpretados. Sem isso,
    /// um pod em UTC geraria o "arquivo das 7h" às 4h da manhã.
    /// TODO(a-confirmar): confirmar o fuso oficial da operação.</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";

    /// <summary>Gera também aos sábados e domingos?
    /// TODO(a-confirmar): não especificado. Padrão conservador é gerar
    /// todo dia — deixar de mandar um retorno é pior que mandar um vazio,
    /// e arquivo sem movimentação nem chega a ser gerado.</summary>
    public bool IncluirFinsDeSemana { get; set; } = true;
}

public enum TipoJanela
{
    /// <summary>Só as movimentações novas desde a janela anterior.</summary>
    Parcial,

    /// <summary>O dia inteiro, como fechamento.</summary>
    Consolidado,
}

public sealed record Ocorrencia(DateTimeOffset Momento, TipoJanela Tipo);

/// <summary>
/// Calcula quando o robô deve acordar e que tipo de arquivo gerar.
///
/// Lógica pura, sem relógio nem I/O — recebe "agora" e devolve a próxima
/// ocorrência. É a peça que os testes cobrem; o <see
/// cref="PagamentoRetornoWorker"/> só dorme até o horário devolvido.
/// </summary>
public class CalculadoraJanelas(IOptions<JanelaOptions> opcoes)
{
    private readonly JanelaOptions _opt = opcoes.Value;

    public TimeZoneInfo Fuso => TimeZoneInfo.FindSystemTimeZoneById(_opt.FusoHorario);

    /// <summary>
    /// Todas as ocorrências de um dia: uma parcial a cada intervalo a
    /// partir de <see cref="JanelaOptions.HoraInicio"/>, e a consolidada
    /// no <see cref="JanelaOptions.HoraFim"/>.
    ///
    /// O horário de fim é sempre consolidado, mesmo que não caia certinho
    /// na grade do intervalo — é o fechamento do dia, não mais uma parcial.
    /// </summary>
    public IReadOnlyList<Ocorrencia> OcorrenciasDoDia(DateOnly dia)
    {
        if (!_opt.IncluirFinsDeSemana &&
            dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return [];

        if (_opt.IntervaloParcial <= TimeSpan.Zero)
            throw new InvalidOperationException("Janela:IntervaloParcial precisa ser maior que zero.");

        if (_opt.HoraFim <= _opt.HoraInicio)
            throw new InvalidOperationException("Janela:HoraFim precisa ser posterior a Janela:HoraInicio.");

        var ocorrencias = new List<Ocorrencia>();

        for (var hora = _opt.HoraInicio; hora < _opt.HoraFim; hora += _opt.IntervaloParcial)
            ocorrencias.Add(new Ocorrencia(EmFuso(dia, hora), TipoJanela.Parcial));

        ocorrencias.Add(new Ocorrencia(EmFuso(dia, _opt.HoraFim), TipoJanela.Consolidado));

        return ocorrencias;
    }

    /// <summary>Primeira ocorrência estritamente depois de <paramref
    /// name="agora"/>. Varre até 8 dias à frente pra cobrir o caso de fins
    /// de semana desligados (sexta 18h → segunda 7h) sem laço infinito se
    /// a configuração ficar impossível.</summary>
    public Ocorrencia? ProximaApos(DateTimeOffset agora)
    {
        var local = TimeZoneInfo.ConvertTime(agora, Fuso);

        for (var salto = 0; salto <= 8; salto++)
        {
            var dia = DateOnly.FromDateTime(local.Date).AddDays(salto);
            var proxima = OcorrenciasDoDia(dia).FirstOrDefault(o => o.Momento > agora);
            if (proxima is not null) return proxima;
        }

        return null;
    }

    private DateTimeOffset EmFuso(DateOnly dia, TimeSpan hora)
    {
        var semFuso = dia.ToDateTime(TimeOnly.MinValue).Add(hora);
        return new DateTimeOffset(semFuso, Fuso.GetUtcOffset(semFuso));
    }
}
