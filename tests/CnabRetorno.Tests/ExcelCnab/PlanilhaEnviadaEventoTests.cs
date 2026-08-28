using System.Text.Json;
using CnabRetorno.ExcelCnab.Worker.Notificacao;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O JSON publicado no tópico. Os nomes dos campos são contrato com quem
/// assina — renomear uma propriedade em C# não pode mudar o payload em
/// silêncio, e é isso que estes testes travam.
/// </summary>
public class PlanilhaEnviadaEventoTests
{
    private static PlanilhaEnviadaEvento Exemplo(string razaoSocial = "ACME DISTRIBUIDORA LTDA") => new()
    {
        ArquivoId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ArquivoNome = "Simplificado_12345678000199.xlsx",
        Cnpj = "12345678000199",
        RazaoSocial = razaoSocial,
        AppId = "cash-cobranca",
        Pipeline = "excel-cnab",
        JobId = "job-123",
        OcorridoEm = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Serializa_com_os_nomes_de_campo_do_contrato()
    {
        using var doc = JsonDocument.Parse(Exemplo().Serializar());
        var raiz = doc.RootElement;

        Assert.Equal("planilha-enviada-para-conversao", raiz.GetProperty("evento").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", raiz.GetProperty("arquivoId").GetString());
        Assert.Equal("Simplificado_12345678000199.xlsx", raiz.GetProperty("arquivoNome").GetString());
        Assert.Equal("12345678000199", raiz.GetProperty("cnpj").GetString());
        Assert.Equal("ACME DISTRIBUIDORA LTDA", raiz.GetProperty("razaoSocial").GetString());
        Assert.Equal("cash-cobranca", raiz.GetProperty("appId").GetString());
        Assert.Equal("excel-cnab", raiz.GetProperty("pipeline").GetString());
        Assert.Equal("job-123", raiz.GetProperty("jobId").GetString());
        Assert.Equal("2026-08-27T12:00:00+00:00", raiz.GetProperty("ocorridoEm").GetString());
    }

    [Fact]
    public void O_arquivoId_e_o_mesmo_que_amarra_a_conclusao_da_conversao()
    {
        // É a única chave que quem consome tem pra parear este aviso com a
        // linha em Cobranca.Arquivo e com a conclusão do conversor.
        var evento = Exemplo();

        using var doc = JsonDocument.Parse(evento.Serializar());

        Assert.Equal(
            evento.ArquivoId,
            Guid.Parse(doc.RootElement.GetProperty("arquivoId").GetString()!));
    }

    [Fact]
    public void Aceite_sem_jobId_ainda_gera_mensagem()
    {
        // O jobId é anulável no aceite do conversor; a falta dele não pode
        // impedir o aviso de sair.
        var evento = Exemplo() with { JobId = null };

        using var doc = JsonDocument.Parse(evento.Serializar());

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("jobId").ValueKind);
    }

    [Fact]
    public void Acento_na_razao_social_sai_legivel()
    {
        var json = Exemplo("COMÉRCIO SÃO JOÃO S/A").Serializar();

        Assert.Contains("COMÉRCIO SÃO JOÃO S/A", json);
    }

    [Fact]
    public async Task Notificador_desligado_e_no_op()
    {
        // Notificacao:Habilitado=false registra este no-op no lugar do SNS,
        // pra que o processador não precise de if de configuração.
        await new NotificadorDesligado().NotificarAsync(Exemplo(), default);
    }
}
