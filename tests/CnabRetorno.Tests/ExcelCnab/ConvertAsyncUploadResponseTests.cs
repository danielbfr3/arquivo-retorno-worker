using System.Text.Json;
using CnabRetorno.Core.Aplicacao.Dtos;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O aceite do conversor assíncrono. O robô termina aqui: o CNAB fica
/// pronto depois, e a conclusão chega por fila pra outro worker.
/// </summary>
public class ConvertAsyncUploadResponseTests
{
    private static readonly JsonSerializerOptions Opcoes =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Desserializa_o_aceite_documentado()
    {
        const string corpo = """
        {
          "jobId": "job-123",
          "appId": "cash-cobranca",
          "id": "11111111-1111-1111-1111-111111111111",
          "status": "pending",
          "statusUrl": "https://conversor/v1/jobs/job-123"
        }
        """;

        var resposta = JsonSerializer.Deserialize<ConvertAsyncUploadResponse>(corpo, Opcoes)!;

        Assert.Equal("job-123", resposta.JobId);
        Assert.Equal("cash-cobranca", resposta.AppId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", resposta.Id);
        Assert.True(resposta.Aceito);
    }

    [Theory]
    [InlineData("PENDING", true)]  // status é comparado sem caixa
    [InlineData("failed", false)]
    [InlineData("rejected", false)]
    [InlineData(null, false)]      // resposta 200 sem status não é aceite
    public void So_pending_conta_como_aceite(string? status, bool esperado)
    {
        var resposta = new ConvertAsyncUploadResponse
        {
            AppId = "cash-cobranca",
            Id = "11111111-1111-1111-1111-111111111111",
            Status = status,
        };

        Assert.Equal(esperado, resposta.Aceito);
    }

    [Fact]
    public void Campos_desconhecidos_no_corpo_nao_quebram()
    {
        // A API pode passar a mandar mais campos; isso não pode derrubar
        // um envio que foi aceito.
        const string corpo = """
        {"appId":"cash-cobranca","id":"1","status":"pending","novoCampo":{"x":1}}
        """;

        var resposta = JsonSerializer.Deserialize<ConvertAsyncUploadResponse>(corpo, Opcoes)!;

        Assert.True(resposta.Aceito);
    }
}
