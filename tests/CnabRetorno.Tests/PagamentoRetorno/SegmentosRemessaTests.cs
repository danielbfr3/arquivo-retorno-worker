using CnabRetorno.PagamentoRetorno.Worker.Json;
using Xunit;

namespace CnabRetorno.Tests.PagamentoRetorno;

public class SegmentosRemessaTests
{
    /// <summary>Monta uma linha de 240 posições com tipo de registro
    /// (pos. 8) e segmento (pos. 14) nos lugares certos.</summary>
    internal static string Linha(char tipoRegistro, char segmento, string? posicao18a19 = null)
    {
        var linha = new char[240];
        Array.Fill(linha, ' ');
        linha[7] = tipoRegistro;
        linha[13] = segmento;

        if (posicao18a19 is not null)
        {
            linha[17] = posicao18a19[0];
            linha[18] = posicao18a19[1];
        }

        return new string(linha);
    }

    [Fact]
    public void Deve_separar_os_segmentos_de_uma_transferencia()
    {
        var segmentos = SegmentosRemessa.Analisar(Linha('3', 'A') + Linha('3', 'B'));

        Assert.True(segmentos.TemSegmentoA);
        Assert.NotNull(segmentos.B);
        Assert.Null(segmentos.J);
    }

    [Fact]
    public void Deve_distinguir_J_de_J52_pelas_posicoes_18_e_19()
    {
        // Os dois são segmento 'J'; só o registro opcional traz "52" nas
        // posições 18-19. Confundi-los faria o código de barras ser lido
        // de um registro que não o tem.
        var segmentos = SegmentosRemessa.Analisar(Linha('3', 'J') + Linha('3', 'J', "52"));

        Assert.True(segmentos.TemSegmentoJ);
        Assert.NotNull(segmentos.J52);
        Assert.NotEqual(segmentos.J, segmentos.J52);
    }

    [Fact]
    public void Deve_aceitar_linhas_concatenadas_sem_separador()
    {
        // É como o campo Linhas vem das tabelas *Info.
        var segmentos = SegmentosRemessa.Analisar(Linha('3', 'A') + Linha('3', 'B'));

        Assert.NotNull(segmentos.A);
        Assert.NotNull(segmentos.B);
    }

    [Fact]
    public void Deve_aceitar_linhas_separadas_por_quebra()
    {
        var segmentos = SegmentosRemessa.Analisar($"{Linha('3', 'A')}\r\n{Linha('3', 'B')}\r\n");

        Assert.NotNull(segmentos.A);
        Assert.NotNull(segmentos.B);
    }

    [Fact]
    public void Deve_ignorar_header_e_trailer_se_vierem_junto()
    {
        var segmentos = SegmentosRemessa.Analisar(
            Linha('1', ' ') + Linha('3', 'A') + Linha('5', ' '));

        Assert.NotNull(segmentos.A);
        Assert.Null(segmentos.B);
    }

    [Fact]
    public void Deve_descartar_linha_truncada()
    {
        // Resto de gravação incompleta não pode virar registro.
        var segmentos = SegmentosRemessa.Analisar(Linha('3', 'A') + "3            J");

        Assert.NotNull(segmentos.A);
        Assert.Null(segmentos.J);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_linhas_deve_devolver_vazio(string? linhas)
    {
        var segmentos = SegmentosRemessa.Analisar(linhas);

        Assert.False(segmentos.TemSegmentoA);
        Assert.False(segmentos.TemSegmentoJ);
    }
}
