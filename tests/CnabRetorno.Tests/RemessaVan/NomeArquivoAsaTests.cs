using CnabRetorno.RemessaVan.Worker.Vans;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.RemessaVan;

public class NomeArquivoAsaTests
{
    private static readonly DateTime Momento = new(2026, 8, 3, 6, 58, 30, DateTimeKind.Unspecified);

    private static readonly DadosNomeArquivo Dados = new(
        Documento: "12345678000199",
        ContaHeader: "000900000900",
        Van: "Finnet",
        ArquivoId: Guid.Parse("8e02c96f-a3ec-480e-a662-5ff69def82ad"),
        NomeOriginal: "CB12345678000199030826.04.REM",
        Momento: Momento);

    private static NomeArquivoAsa Com(string template, string extensaoPadrao = ".txt")
        => new(Options.Create(new NomenclaturaOptions { Template = template, ExtensaoPadrao = extensaoPadrao }));

    [Fact]
    public void Template_padrao_deve_render_no_formato_do_asa()
    {
        var nome = new NomeArquivoAsa(Options.Create(new NomenclaturaOptions())).Renderizar(Dados);

        Assert.Equal("ArquivoRemessa_12345678000199_03082026_065830.REM", nome);
    }

    [Fact]
    public void Deve_substituir_todos_os_tokens()
    {
        var nome = Com("{van}-{documento}-{contaHeader}-{guid}-{original}{ext}").Renderizar(Dados);

        Assert.Equal(
            "Finnet-12345678000199-000900000900-8e02c96f-a3ec-480e-a662-5ff69def82ad-CB12345678000199030826.04.REM",
            nome);
    }

    [Theory]
    [InlineData("{data:ddMMyyyy}", "03082026")]
    [InlineData("{data:HHmmss}", "065830")]
    [InlineData("{data:yyyy-MM-dd}", "2026-08-03")]
    [InlineData("{data}", "20260803065830")]
    public void Deve_formatar_a_data_conforme_o_token(string template, string esperado)
        => Assert.Equal(esperado, Com(template).Renderizar(Dados));

    [Fact]
    public void Sem_extensao_na_origem_deve_usar_a_padrao()
    {
        var nome = Com("{original}{ext}").Renderizar(Dados with { NomeOriginal = "ARQUIVO_SEM_EXTENSAO" });

        Assert.Equal("ARQUIVO_SEM_EXTENSAO.txt", nome);
    }

    [Fact]
    public void Conta_header_ausente_deve_render_vazio_e_nao_quebrar()
    {
        var nome = Com("R_{documento}_{contaHeader}.txt").Renderizar(Dados with { ContaHeader = null });

        Assert.Equal("R_12345678000199_.txt", nome);
    }

    [Fact]
    public void Deve_remover_separador_de_caminho_vindo_de_dado_externo()
    {
        // O nome da VAN e o nome original vêm de fora; um '/' que passasse
        // batido faria o arquivo ser escrito fora da pasta de destino.
        var nome = Com("{van}_{documento}.txt").Renderizar(Dados with { Van = "../../etc" });

        Assert.DoesNotContain('/', nome);
        Assert.Equal("....etc_12345678000199.txt", nome);
    }

    [Fact]
    public void Token_desconhecido_deve_ficar_literal()
    {
        // Falha visível no nome do arquivo é melhor que substituição
        // silenciosa por vazio, que passaria despercebida.
        Assert.Equal("{inexistente}_12345678000199", Com("{inexistente}_{documento}").Renderizar(Dados));
    }
}
