using System.Text.Json;
using CnabRetorno.RetornoSubscriber.Worker.Mensageria;
using Xunit;

namespace CnabRetorno.Tests.RetornoSubscriber;

/// <summary>
/// Desserializa a mensagem SQS de conclusão da conversão no shape
/// observado no ambiente real (ver docs/cash-cobranca-referencia.md §2.4)
/// — mesma <c>JsonNamingPolicy.CamelCase</c> usada pelo
/// <c>SqsConsumerHostedService</c>.
/// </summary>
public class ConversaoConcluidaMessageTests
{
    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static ConversaoConcluidaMessage? Desserializar(string json)
        => JsonSerializer.Deserialize<ConversaoConcluidaMessage>(json, JsonOpcoes);

    [Fact]
    public void Deve_desserializar_conclusao_com_sucesso()
    {
        var mensagem = Desserializar("""
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "success": true,
          "data": { "outputUrl": "https://storage.exemplo/resultado/arquivo.RET?assinatura=abc" }
        }
        """);

        Assert.NotNull(mensagem);
        Assert.Equal("11111111-1111-1111-1111-111111111111", mensagem.Id);
        Assert.True(mensagem.Success);
        Assert.Equal("https://storage.exemplo/resultado/arquivo.RET?assinatura=abc", mensagem.Data!.OutputUrl);
    }

    [Fact]
    public void Id_deve_ser_o_guid_do_arquivo_registrado_pelo_robo_1()
    {
        var mensagem = Desserializar("""
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "success": true,
          "data": { "outputUrl": "https://storage.exemplo/x" }
        }
        """)!;

        Assert.True(Guid.TryParse(mensagem.Id, out var arquivoId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), arquivoId);
    }

    [Fact]
    public void Deve_desserializar_conclusao_com_falha()
    {
        var mensagem = Desserializar("""
        { "id": "11111111-1111-1111-1111-111111111111", "success": false, "data": null }
        """);

        Assert.NotNull(mensagem);
        Assert.False(mensagem.Success);
        Assert.Null(mensagem.Data);
    }

    [Fact]
    public void Deve_aceitar_mensagem_sem_data()
    {
        var mensagem = Desserializar("""
        { "id": "11111111-1111-1111-1111-111111111111", "success": true }
        """);

        Assert.NotNull(mensagem);
        Assert.Null(mensagem.Data);
    }

    [Fact]
    public void Deve_ignorar_campos_extras_da_mensagem_real()
    {
        // O shape real traz mais coisa (jobId, appId, status, issues,
        // createdAt...) — nada disso é usado por este worker, e campo
        // desconhecido não pode quebrar a desserialização.
        var mensagem = Desserializar("""
        {
          "jobId": "22222222-2222-2222-2222-222222222222",
          "appId": "cash-cobranca",
          "id": "11111111-1111-1111-1111-111111111111",
          "status": "succeeded",
          "success": true,
          "issues": [ { "code": "cnab.batch.item_count", "severity": "Error" } ],
          "createdAt": "2026-07-21T14:30:47.03Z",
          "data": { "outputUrl": "https://storage.exemplo/x", "outputFormat": "cnab" }
        }
        """);

        Assert.NotNull(mensagem);
        Assert.True(mensagem.Success);
        Assert.Equal("https://storage.exemplo/x", mensagem.Data!.OutputUrl);
    }
}
