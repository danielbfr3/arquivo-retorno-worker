using CnabRetorno.Core.Dominio;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O JSON de <c>Dados</c> é o contrato entre quem popula
/// <c>Cobranca.DocumentoDados</c> e o preenchimento da planilha — casos de
/// erro aqui viram "documento sem dados" (quarentena), nunca uma exceção
/// subindo pelo pipeline.
/// </summary>
public class DocumentoDadosTests
{
    private static DocumentoDados Criar(string dados)
        => new() { NumeroDocumento = "12345678000199", Dados = dados };

    [Fact]
    public void Desserializa_objeto_json_valido()
    {
        var valores = Criar("""{"Nome Cliente": "ACME LTDA", "Valor": "1500.00"}""").DesserializarDados();

        Assert.NotNull(valores);
        Assert.Equal("ACME LTDA", valores["Nome Cliente"]);
        Assert.Equal("1500.00", valores["Valor"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{")]
    [InlineData("[1, 2, 3]")]
    [InlineData("42")]
    [InlineData("{}")]
    public void Devolve_null_para_json_invalido_ou_sem_nada_a_preencher(string dados)
        => Assert.Null(Criar(dados).DesserializarDados());
}
