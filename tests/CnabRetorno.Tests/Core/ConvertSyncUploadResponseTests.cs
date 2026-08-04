using System.Text;
using System.Text.Json;
using CnabRetorno.Core.Aplicacao.Dtos;
using Xunit;

namespace CnabRetorno.Tests.Core;

/// <summary>
/// Envelope da resposta de <c>POST /v1/convert/sync/upload</c> no sentido
/// JSON → CNAB. O envelope em si veio de exemplo real; a forma de
/// <c>data</c> neste sentido é dedução (ver o TODO no DTO), e estes testes
/// travam o comportamento que o robô depende: reconhecer texto e base64, e
/// falhar alto quando não vem conteúdo.
/// </summary>
public class ConvertSyncUploadResponseTests
{
    private static readonly JsonSerializerOptions Opcoes = new() { PropertyNameCaseInsensitive = true };

    private static ConvertSyncUploadResponse Desserializar(string json)
        => JsonSerializer.Deserialize<ConvertSyncUploadResponse>(json, Opcoes)!;

    [Fact]
    public void Deve_desserializar_o_envelope()
    {
        var resposta = Desserializar("""
        {
          "appId": "cash-pagamento",
          "id": "11111111-1111-1111-1111-111111111111",
          "success": true,
          "outputFormat": "cnab240",
          "binary": false,
          "data": "linha-cnab"
        }
        """);

        Assert.Equal("cash-pagamento", resposta.AppId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", resposta.Id);
        Assert.True(resposta.Success);
        Assert.Equal("cnab240", resposta.OutputFormat);
        Assert.False(resposta.Binary);
    }

    [Fact]
    public void Conteudo_em_texto_deve_ser_decodificado_em_latin1()
    {
        // O layout é posicional e conta bytes: em UTF-8 o "Ç" ocuparia
        // duas posições e deslocaria a linha inteira.
        var resposta = Desserializar("""
        { "appId": "a", "id": "b", "success": true, "binary": false, "data": "AÇÃO" }
        """);

        var bytes = resposta.ConteudoCnab();

        Assert.Equal(4, bytes.Length);
        Assert.Equal("AÇÃO", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void Conteudo_binario_deve_ser_decodificado_de_base64()
    {
        var original = Encoding.Latin1.GetBytes("CNAB");
        var json = $$"""
        { "appId": "a", "id": "b", "success": true, "binary": true, "data": "{{Convert.ToBase64String(original)}}" }
        """;

        Assert.Equal(original, Desserializar(json).ConteudoCnab());
    }

    [Theory]
    [InlineData("""{ "appId": "a", "id": "b", "success": false, "binary": false }""")]
    [InlineData("""{ "appId": "a", "id": "b", "success": true, "binary": false, "data": null }""")]
    [InlineData("""{ "appId": "a", "id": "b", "success": true, "binary": false, "data": "" }""")]
    public void Sem_conteudo_deve_falhar_em_vez_de_devolver_arquivo_vazio(string json)
    {
        // Gravar um arquivo de zero byte no Gestor e marcar o registro
        // como concluído seria pior que a exceção: o cliente receberia um
        // retorno vazio como se fosse legítimo.
        Assert.Throws<InvalidOperationException>(() => Desserializar(json).ConteudoCnab());
    }

    [Fact]
    public void Campos_desconhecidos_devem_ser_ignorados()
    {
        // A API pode ganhar campos novos sem que isso quebre o worker.
        var resposta = Desserializar("""
        {
          "appId": "a", "id": "b", "success": true, "binary": false,
          "data": "x", "campoNovoQueAindaNaoExiste": { "algo": 1 }
        }
        """);

        Assert.True(resposta.Success);
    }
}
