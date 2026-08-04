using CnabRetorno.RemessaVan.Worker.Vans;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.RemessaVan;

/// <summary>
/// Os nomes de arquivo usados aqui seguem a **forma** dos exemplos reais
/// enviados pelo time em 03/08/2026, com CNPJs fictícios — nenhum dado de
/// cliente entra no repositório.
/// </summary>
public class MascaraVanTests
{
    private const string CnpjFicticio = "12345678000199";

    private static MascaraVan Mascara(string padrao, TipoArquivoVan tipo = TipoArquivoVan.Remessa, string? cnpj = null)
        => new(new MascaraVanConfig { Van = "TesteVan", Mascara = padrao, Tipo = tipo, Cnpj = cnpj });

    [Fact]
    public void Deve_capturar_o_cnpj_do_nome_do_arquivo()
    {
        var casou = Mascara("CB{cnpj}*").TentarCasar($"CB{CnpjFicticio}030826.04.REM", out var reconhecido);

        Assert.True(casou);
        Assert.Equal(CnpjFicticio, reconhecido.Cnpj);
        Assert.Equal("TesteVan", reconhecido.Van);
        Assert.Equal(TipoArquivoVan.Remessa, reconhecido.Tipo);
    }

    [Theory]
    [InlineData("CB{cnpj}DDMMYY.*", "CB12345678000199030826.C01.rem")]
    [InlineData("CB{cnpj}DDMM*.REM", "CB123456780001990308xy.REM")]
    [InlineData("CB{cnpj}DDMMYY.*.REM", "CB12345678000199270726.000.REM")]
    [InlineData("ArquivoRetorno_{cnpj}_*", "ArquivoRetorno_12345678000199_03082026_065830.txt")]
    [InlineData("CB.{cnpj}.*.RET", "CB.12345678000199.6000006751.030826.XX.RET")]
    public void Deve_casar_as_formas_reais_de_mascara(string padrao, string nomeArquivo)
        => Assert.True(Mascara(padrao).TentarCasar(nomeArquivo, out _));

    [Fact]
    public void Token_de_data_nao_pode_comer_letras_literais()
    {
        // O 'M' de ".REM" precisa continuar literal: se cada letra D/M/Y
        // virasse dígito, nenhum arquivo Nexxera casaria.
        var mascara = Mascara("CB{cnpj}DDMM*.REM");

        Assert.True(mascara.TentarCasar($"CB{CnpjFicticio}0308abc.REM", out _));
        Assert.False(mascara.TentarCasar($"CB{CnpjFicticio}0308abc.RE1", out _));
    }

    [Fact]
    public void Token_de_data_deve_exigir_digitos()
    {
        Assert.False(Mascara("CB{cnpj}DDMMYY.*").TentarCasar($"CB{CnpjFicticio}ABCDEF.C01.rem", out _));
    }

    [Fact]
    public void Deve_ignorar_diferenca_de_maiusculas()
    {
        // A mesma VAN aparece com .REM, .rem e .Rem nos exemplos reais.
        var mascara = Mascara("CB{cnpj}DDMM*.REM");

        Assert.True(mascara.TentarCasar($"CB{CnpjFicticio}0308aa.rem", out _));
        Assert.True(mascara.TentarCasar($"CB{CnpjFicticio}0308aa.Rem", out _));
    }

    [Fact]
    public void Mascara_deve_ancorar_nas_duas_pontas()
    {
        // Sem âncoras, "xxCB<cnpj>...zz" casaria e o arquivo errado
        // entraria no fluxo.
        Assert.False(Mascara("CB{cnpj}DDMMYY.*.REM").TentarCasar($"prefixoCB{CnpjFicticio}030826.000.REM", out _));
    }

    [Fact]
    public void Sem_token_de_cnpj_deve_usar_o_cnpj_configurado()
    {
        var casou = Mascara("ArquivoRetorno_*.txt", cnpj: CnpjFicticio)
            .TentarCasar("ArquivoRetorno_qualquer_coisa.txt", out var reconhecido);

        Assert.True(casou);
        Assert.Equal(CnpjFicticio, reconhecido.Cnpj);
    }

    [Fact]
    public void Sem_cnpj_no_nome_nem_na_configuracao_nao_deve_casar()
    {
        // Não dá pra registrar um arquivo sem saber de que cliente ele é —
        // melhor mandar pra quarentena do que gravar sem dono.
        Assert.False(Mascara("ArquivoRetorno_*.txt").TentarCasar("ArquivoRetorno_qualquer.txt", out _));
    }

    [Fact]
    public void Catalogo_deve_respeitar_a_ordem_configurada()
    {
        var catalogo = new CatalogoMascarasVan(Options.Create(new VansOptions
        {
            Mascaras =
            [
                new() { Van = "Especifica", Mascara = "CB{cnpj}DDMMYY.*.REM", Tipo = TipoArquivoVan.Remessa },
                new() { Van = "Generica", Mascara = "CB{cnpj}*", Tipo = TipoArquivoVan.Remessa },
            ],
        }));

        Assert.True(catalogo.TentarReconhecer($"CB{CnpjFicticio}270726.000.REM", out var reconhecido));
        Assert.Equal("Especifica", reconhecido.Van);

        Assert.True(catalogo.TentarReconhecer($"CB{CnpjFicticio}qualquer", out var generico));
        Assert.Equal("Generica", generico.Van);
    }

    [Fact]
    public void Catalogo_deve_recusar_nome_desconhecido()
    {
        var catalogo = new CatalogoMascarasVan(Options.Create(new VansOptions
        {
            Mascaras = [new() { Van = "Finnet", Mascara = "CB{cnpj}*" }],
        }));

        Assert.False(catalogo.TentarReconhecer("LEIAME.txt", out _));
    }
}
