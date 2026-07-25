using CnabRetorno.Core.Dominio;
using Xunit;

namespace CnabRetorno.Tests.Core;

public class NomeArquivoRetornoTests
{
    [Fact]
    public void Deve_extrair_client_id_de_arquivo_v()
    {
        var ok = NomeArquivoRetorno.TentarExtrairClientId(
            "V1234567890001.txt", out var clientId, out var tipo);

        Assert.True(ok);
        Assert.Equal("1234567890", clientId);
        Assert.Equal(TipoArquivoRetorno.V, tipo);
    }

    [Fact]
    public void Deve_extrair_client_id_de_arquivo_pv()
    {
        var ok = NomeArquivoRetorno.TentarExtrairClientId(
            "PV1234567890301_002.txt", out var clientId, out var tipo);

        Assert.True(ok);
        Assert.Equal("1234567890", clientId);
        Assert.Equal(TipoArquivoRetorno.PV, tipo);
    }

    [Theory]
    [InlineData("RET_QUALQUER.txt")]
    [InlineData("V123.txt")] // curto demais pro tamanho de ClientId
    [InlineData("")]
    public void Deve_retornar_falso_para_nome_fora_do_padrao(string nomeArquivo)
    {
        var ok = NomeArquivoRetorno.TentarExtrairClientId(nomeArquivo, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void PV_deve_ser_reconhecido_antes_de_colidir_com_padrao_v()
    {
        // "PV..." começa com "P", não com "V" — mas o parser precisa checar
        // PV antes de V pra não interpretar isso como um V mal-formado.
        var ok = NomeArquivoRetorno.TentarExtrairClientId(
            "PV1234567890301_002.txt", out _, out var tipo);

        Assert.True(ok);
        Assert.Equal(TipoArquivoRetorno.PV, tipo);
    }

    [Fact]
    public void CorrespondeAoMesmoCliente_deve_bater_com_client_id_do_v()
    {
        var corresponde = NomeArquivoRetorno.CorrespondeAoMesmoCliente(
            "PV1234567890301_002.txt", "1234567890");

        Assert.True(corresponde);
    }

    [Fact]
    public void CorrespondeAoMesmoCliente_deve_recusar_client_id_diferente()
    {
        var corresponde = NomeArquivoRetorno.CorrespondeAoMesmoCliente(
            "PV1234567890301_002.txt", "9999999999");

        Assert.False(corresponde);
    }

    [Fact]
    public void CorrespondeAoMesmoCliente_deve_recusar_arquivo_v()
    {
        // Um arquivo V não pode ser confundido com o PV correspondente.
        var corresponde = NomeArquivoRetorno.CorrespondeAoMesmoCliente(
            "V1234567890001.txt", "1234567890");

        Assert.False(corresponde);
    }
}
