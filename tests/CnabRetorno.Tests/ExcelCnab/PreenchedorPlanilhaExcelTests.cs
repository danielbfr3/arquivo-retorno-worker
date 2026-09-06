using ClosedXML.Excel;
using CnabRetorno.ExcelCnab.Worker.Planilha;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// Cobre o único ponto do worker que abre a planilha de verdade. Todos os
/// testes constroem o workbook em memória com o próprio ClosedXML — sem
/// arquivo fixture no repositório.
/// </summary>
public class PreenchedorPlanilhaExcelTests
{
    private static PreenchedorPlanilhaExcel Criar(PreenchimentoOptions? opcoes = null)
        => new(Options.Create(opcoes ?? new PreenchimentoOptions()));

    private static byte[] CriarPlanilha(string[] cabecalhos, int linhasDeDados)
    {
        using var workbook = new XLWorkbook();
        var planilha = workbook.Worksheets.Add("Dados");

        for (var coluna = 0; coluna < cabecalhos.Length; coluna++)
            planilha.Cell(1, coluna + 1).Value = cabecalhos[coluna];

        for (var linha = 0; linha < linhasDeDados; linha++)
        for (var coluna = 0; coluna < cabecalhos.Length; coluna++)
            planilha.Cell(linha + 2, coluna + 1).Value = "original";

        using var saida = new MemoryStream();
        workbook.SaveAs(saida);
        return saida.ToArray();
    }

    private static IXLWorksheet AbrirPrimeiraAba(byte[] arquivo, out XLWorkbook workbook)
    {
        workbook = new XLWorkbook(new MemoryStream(arquivo));
        return workbook.Worksheets.First();
    }

    [Fact]
    public void Escreve_o_valor_em_todas_as_linhas_de_dados_quando_a_chave_bate_com_o_cabecalho()
    {
        var original = CriarPlanilha(["Nome Cliente", "Valor"], linhasDeDados: 3);

        var resultado = Criar().Preencher(original, new Dictionary<string, string>
        {
            ["Nome Cliente"] = "ACME LTDA",
        });

        var planilha = AbrirPrimeiraAba(resultado, out var workbook);
        using (workbook)
        {
            for (var linha = 2; linha <= 4; linha++)
                Assert.Equal("ACME LTDA", planilha.Cell(linha, 1).GetString());

            // Coluna sem chave correspondente no JSON não é tocada.
            Assert.Equal("original", planilha.Cell(2, 2).GetString());
        }
    }

    [Fact]
    public void Chave_sem_cabecalho_correspondente_lanca_ColunaNaoEncontrada()
    {
        var original = CriarPlanilha(["Nome Cliente"], linhasDeDados: 1);

        var excecao = Assert.Throws<ColunaNaoEncontradaException>(() =>
            Criar().Preencher(original, new Dictionary<string, string>
            {
                ["Nome Cliente"] = "ACME LTDA",
                ["Coluna Inexistente"] = "valor qualquer",
            }));

        Assert.Contains("Coluna Inexistente", excecao.Chaves);
    }

    [Fact]
    public void Planilha_so_com_cabecalho_lanca_PlanilhaSemLinhasDeDados()
    {
        var original = CriarPlanilha(["Nome Cliente"], linhasDeDados: 0);

        Assert.Throws<PlanilhaSemLinhasDeDadosException>(() =>
            Criar().Preencher(original, new Dictionary<string, string> { ["Nome Cliente"] = "ACME LTDA" }));
    }

    [Fact]
    public void Comparacao_padrao_tolera_espaco_e_caixa_no_cabecalho()
    {
        var original = CriarPlanilha([" Nome Cliente "], linhasDeDados: 1);

        var resultado = Criar().Preencher(original, new Dictionary<string, string>
        {
            ["nome cliente"] = "ACME LTDA",
        });

        var planilha = AbrirPrimeiraAba(resultado, out var workbook);
        using (workbook)
            Assert.Equal("ACME LTDA", planilha.Cell(2, 1).GetString());
    }

    [Fact]
    public void Comparacao_exata_nao_tolera_diferenca_de_caixa()
    {
        var original = CriarPlanilha(["Nome Cliente"], linhasDeDados: 1);
        var preenchedor = Criar(new PreenchimentoOptions { ComparacaoCabecalho = ComparacaoCabecalho.Exata });

        Assert.Throws<ColunaNaoEncontradaException>(() =>
            preenchedor.Preencher(original, new Dictionary<string, string> { ["nome cliente"] = "ACME LTDA" }));
    }

    [Fact]
    public void Valor_e_gravado_como_texto_preservando_zero_a_esquerda()
    {
        var original = CriarPlanilha(["Agencia"], linhasDeDados: 1);

        var resultado = Criar().Preencher(original, new Dictionary<string, string> { ["Agencia"] = "0123" });

        var planilha = AbrirPrimeiraAba(resultado, out var workbook);
        using (workbook)
        {
            Assert.Equal("0123", planilha.Cell(2, 1).GetString());
            Assert.Equal(XLDataType.Text, planilha.Cell(2, 1).DataType);
        }
    }
}
