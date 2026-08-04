using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.PagamentoRetorno;

public class CalculadoraJanelasTests
{
    private static CalculadoraJanelas Com(
        string inicio = "07:00", string fim = "18:00", string intervalo = "01:00",
        bool finsDeSemana = true)
        => new(Options.Create(new JanelaOptions
        {
            HoraInicio = TimeSpan.Parse(inicio),
            HoraFim = TimeSpan.Parse(fim),
            IntervaloParcial = TimeSpan.Parse(intervalo),
            FusoHorario = "America/Sao_Paulo",
            IncluirFinsDeSemana = finsDeSemana,
        }));

    private static readonly DateOnly Segunda = new(2026, 8, 3);

    [Fact]
    public void Dia_padrao_deve_ter_onze_parciais_e_um_consolidado()
    {
        var ocorrencias = Com().OcorrenciasDoDia(Segunda);

        Assert.Equal(12, ocorrencias.Count); // 7h..17h = 11 parciais + 18h
        Assert.Equal(11, ocorrencias.Count(o => o.Tipo == TipoJanela.Parcial));
        Assert.Single(ocorrencias, o => o.Tipo == TipoJanela.Consolidado);
    }

    [Fact]
    public void Primeira_deve_ser_a_hora_de_inicio_e_ultima_a_de_fim()
    {
        var ocorrencias = Com().OcorrenciasDoDia(Segunda);

        Assert.Equal(7, ocorrencias[0].Momento.Hour);
        Assert.Equal(TipoJanela.Parcial, ocorrencias[0].Tipo);

        Assert.Equal(18, ocorrencias[^1].Momento.Hour);
        Assert.Equal(TipoJanela.Consolidado, ocorrencias[^1].Tipo);
    }

    [Fact]
    public void Nao_deve_existir_parcial_no_horario_do_consolidado()
    {
        // As 18h são o fechamento do dia, não mais uma parcial — gerar os
        // dois no mesmo instante mandaria dois arquivos pro cliente.
        var dezoito = Com().OcorrenciasDoDia(Segunda).Where(o => o.Momento.Hour == 18).ToList();

        Assert.Single(dezoito);
        Assert.Equal(TipoJanela.Consolidado, dezoito[0].Tipo);
    }

    [Fact]
    public void Intervalo_deve_ser_parametrizavel()
    {
        var ocorrencias = Com(intervalo: "02:00").OcorrenciasDoDia(Segunda);

        Assert.Equal(6, ocorrencias.Count(o => o.Tipo == TipoJanela.Parcial)); // 7,9,11,13,15,17
        Assert.Single(ocorrencias, o => o.Tipo == TipoJanela.Consolidado);
    }

    [Fact]
    public void Horario_de_fim_fora_da_grade_ainda_deve_ser_consolidado()
    {
        // 7h + 2h = 9h, 11h, 13h, 15h, 17h; 18h não cai na grade e mesmo
        // assim é o fechamento.
        var ocorrencias = Com(intervalo: "02:00", fim: "18:00").OcorrenciasDoDia(Segunda);

        Assert.Equal(18, ocorrencias[^1].Momento.Hour);
        Assert.Equal(TipoJanela.Consolidado, ocorrencias[^1].Tipo);
    }

    [Fact]
    public void Proxima_deve_ser_estritamente_posterior_a_agora()
    {
        var calculadora = Com();
        var oitoEmPonto = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(-3));

        var proxima = calculadora.ProximaApos(oitoEmPonto);

        // Estritamente posterior: senão o worker reexecutaria a janela que
        // acabou de rodar, em laço.
        Assert.NotNull(proxima);
        Assert.Equal(9, proxima.Momento.Hour);
    }

    [Fact]
    public void Depois_do_consolidado_deve_pular_pro_dia_seguinte()
    {
        var proxima = Com().ProximaApos(new DateTimeOffset(2026, 8, 3, 23, 0, 0, TimeSpan.FromHours(-3)));

        Assert.NotNull(proxima);
        Assert.Equal(4, proxima.Momento.Day);
        Assert.Equal(7, proxima.Momento.Hour);
        Assert.Equal(TipoJanela.Parcial, proxima.Tipo);
    }

    [Fact]
    public void Com_fins_de_semana_desligados_sexta_a_noite_deve_cair_na_segunda()
    {
        // 07/08/2026 é uma sexta.
        var proxima = Com(finsDeSemana: false)
            .ProximaApos(new DateTimeOffset(2026, 8, 7, 23, 0, 0, TimeSpan.FromHours(-3)));

        Assert.NotNull(proxima);
        Assert.Equal(DayOfWeek.Monday, proxima.Momento.DayOfWeek);
        Assert.Equal(10, proxima.Momento.Day);
    }

    [Fact]
    public void Consolidado_anterior_deve_ser_o_das_18h_de_ontem()
    {
        // Às 8h de segunda, o dia útil começou no consolidado de domingo
        // 18h — é o piso de todas as janelas de hoje.
        var anterior = Com().ConsolidadoAnterior(
            new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(-3)));

        Assert.NotNull(anterior);
        Assert.Equal(TipoJanela.Consolidado, anterior.Tipo);
        Assert.Equal(2, anterior.Momento.Day);
        Assert.Equal(18, anterior.Momento.Hour);
    }

    [Fact]
    public void Consolidado_anterior_no_instante_exato_do_consolidado_deve_ser_o_de_ontem()
    {
        // Estritamente anterior: às 18h em ponto, o "anterior" é o de
        // ontem — o de agora é a própria janela sendo executada, e usá-lo
        // como piso zeraria o período do consolidado.
        var anterior = Com().ConsolidadoAnterior(
            new DateTimeOffset(2026, 8, 3, 18, 0, 0, TimeSpan.FromHours(-3)));

        Assert.NotNull(anterior);
        Assert.Equal(2, anterior.Momento.Day);
    }

    [Fact]
    public void Consolidado_anterior_com_fins_de_semana_desligados_deve_pular_pra_sexta()
    {
        // Segunda 10/08 de manhã, fins de semana off: o consolidado
        // anterior é o de sexta 07/08 18h — o dia útil de segunda cobre
        // 72 horas, e é isso que garante que o fim de semana não vira
        // buraco.
        var anterior = Com(finsDeSemana: false).ConsolidadoAnterior(
            new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.FromHours(-3)));

        Assert.NotNull(anterior);
        Assert.Equal(DayOfWeek.Friday, anterior.Momento.DayOfWeek);
        Assert.Equal(7, anterior.Momento.Day);
        Assert.Equal(18, anterior.Momento.Hour);
    }

    [Fact]
    public void Instante_banco_padrao_deve_ser_horario_local_de_parede()
    {
        var calculadora = Com();
        var momento = new DateTimeOffset(2026, 8, 3, 18, 0, 0, TimeSpan.FromHours(-3));

        Assert.Equal(new DateTime(2026, 8, 3, 18, 0, 0), calculadora.InstanteBanco(momento));
    }

    [Fact]
    public void Instante_banco_em_utc_deve_deslocar_tres_horas()
    {
        // A chave que corrige o corte inteiro se a base gravar UTC — ver
        // JanelaOptions.TimestampsBancoEmUtc.
        var calculadora = new CalculadoraJanelas(Options.Create(new JanelaOptions
        {
            FusoHorario = "America/Sao_Paulo",
            TimestampsBancoEmUtc = true,
        }));
        var momento = new DateTimeOffset(2026, 8, 3, 18, 0, 0, TimeSpan.FromHours(-3));

        Assert.Equal(new DateTime(2026, 8, 3, 21, 0, 0), calculadora.InstanteBanco(momento));
    }

    [Fact]
    public void Fim_anterior_ao_inicio_deve_falhar_no_calculo()
        => Assert.Throws<InvalidOperationException>(
            () => Com(inicio: "18:00", fim: "07:00").OcorrenciasDoDia(Segunda));

    [Fact]
    public void Intervalo_zero_deve_falhar_em_vez_de_travar()
        => Assert.Throws<InvalidOperationException>(
            () => Com(intervalo: "00:00").OcorrenciasDoDia(Segunda));
}
