using System.Text.Json;
using CnabRetorno.Core.Aplicacao.Dtos;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O JSON que vai no corpo da mensagem do conversor — os nomes dos campos
/// são contrato com o outro lado, não detalhe interno.
/// </summary>
public class MetadadosClienteTests
{
    [Fact]
    public void Serializa_com_os_nomes_de_campo_do_contrato()
    {
        var json = new MetadadosCliente("12345678000199", "ACME DISTRIBUIDORA LTDA").Serializar();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("12345678000199", doc.RootElement.GetProperty("cnpj").GetString());
        Assert.Equal("ACME DISTRIBUIDORA LTDA", doc.RootElement.GetProperty("razaoSocial").GetString());
    }

    [Fact]
    public void Acento_na_razao_social_sai_legivel()
    {
        // Escape agressivo produziria "COMÉRCIO" — válido, mas ilegível
        // no log e pra quem inspeciona o payload do outro lado.
        var json = new MetadadosCliente("12345678000199", "COMÉRCIO SÃO JOÃO S/A").Serializar();

        Assert.Contains("COMÉRCIO SÃO JOÃO S/A", json);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("COMÉRCIO SÃO JOÃO S/A", doc.RootElement.GetProperty("razaoSocial").GetString());
    }
}
