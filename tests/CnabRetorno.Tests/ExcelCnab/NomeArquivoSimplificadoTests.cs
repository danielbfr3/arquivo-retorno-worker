using CnabRetorno.ExcelCnab.Worker.Origem;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O CNPJ do nome do arquivo é a única identificação do cliente no fluxo
/// inteiro — a planilha nunca é aberta. Um falso positivo aqui manda a
/// planilha de um cliente com o documento de outro, então os testes cobrem
/// tanto o que deve casar quanto o que não pode casar.
/// </summary>
public class NomeArquivoSimplificadoTests
{
    private static NomeArquivoSimplificado Criar(NomenclaturaOptions? opcoes = null)
        => new(Options.Create(opcoes ?? new NomenclaturaOptions()));

    [Theory]
    [InlineData("Simplificado_12345678000199.xlsx", ".xlsx")]
    [InlineData("Simplificado_12345678000199.xls", ".xls")]
    [InlineData("Simplificado_12345678000199.XLSX", ".xlsx")] // extensão em caixa alta
    [InlineData("simplificado_12345678000199.xlsx", ".xlsx")] // prefixo em caixa baixa
    public void Reconhece_o_padrao_e_normaliza_a_extensao(string nome, string extensaoEsperada)
    {
        Assert.True(Criar().TentarReconhecer(nome, out var reconhecido));

        Assert.Equal("12345678000199", reconhecido.Cnpj);
        Assert.Equal(extensaoEsperada, reconhecido.Extensao);
    }

    [Theory]
    [InlineData("Simplificado_12.345.678.0001-99.xlsx")]
    [InlineData("Simplificado_12345678.0001-99.xlsx")]
    public void Aceita_cnpj_pontuado_e_devolve_so_digitos(string nome)
    {
        // Quem nomeia o arquivo é uma pessoa, e as duas grafias são o
        // mesmo cliente — mas o que segue pro banco e pro JSON é sempre a
        // forma de 14 dígitos. A barra do CNPJ canônico não aparece aqui
        // porque nome de arquivo não pode contê-la.
        Assert.True(Criar().TentarReconhecer(nome, out var reconhecido));
        Assert.Equal("12345678000199", reconhecido.Cnpj);
    }

    [Theory]
    [InlineData("Simplificado_12345678000199.csv")]          // não é planilha
    [InlineData("Simplificado_12345678000199.pdf")]
    [InlineData("Simplificado_1234567800019.xlsx")]          // 13 dígitos
    [InlineData("Simplificado_123456780001990.xlsx")]        // 15 dígitos
    [InlineData("Simplificado_.xlsx")]                       // sem documento
    [InlineData("Simplificado12345678000199.xlsx")]          // sem o separador
    [InlineData("Relatorio_12345678000199.xlsx")]            // outro prefixo
    [InlineData("Simplificado_12345678000199_v2.xlsx")]      // sufixo extra
    [InlineData("Backup_Simplificado_12345678000199.xlsx")]  // prefixo extra
    [InlineData("~$Simplificado_12345678000199.xlsx")]       // trava do Excel
    public void Recusa_o_que_nao_esta_exatamente_no_padrao(string nome)
        => Assert.False(Criar().TentarReconhecer(nome, out _));

    [Fact]
    public void Mascara_e_extensoes_vem_de_configuracao()
    {
        var alternativa = Criar(new NomenclaturaOptions
        {
            Mascara = "Cobranca-{cnpj}-mensal",
            Extensoes = [".xlsm"],
        });

        Assert.True(alternativa.TentarReconhecer("Cobranca-12345678000199-mensal.xlsm", out var reconhecido));
        Assert.Equal("12345678000199", reconhecido.Cnpj);

        // A máscara antiga deixa de valer quando a configuração muda.
        Assert.False(alternativa.TentarReconhecer("Simplificado_12345678000199.xlsx", out _));
    }

    [Fact]
    public void Mascara_sem_o_token_cnpj_e_erro_de_configuracao()
    {
        // Falha ao subir, não em silêncio: uma máscara sem {cnpj} nunca
        // reconheceria arquivo nenhum, e a pasta acumularia quarentena.
        var opcoes = Options.Create(new NomenclaturaOptions { Mascara = "Simplificado" });

        Assert.Throws<InvalidOperationException>(() => new NomeArquivoSimplificado(opcoes));
    }
}
