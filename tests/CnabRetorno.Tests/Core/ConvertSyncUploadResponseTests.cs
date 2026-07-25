using System.Text.Json;
using CnabRetorno.Core.Aplicacao.Dtos;
using Xunit;

namespace CnabRetorno.Tests.Core;

/// <summary>
/// Desserializa o exemplo REAL de resposta de POST /v1/convert/sync/upload
/// (docs/cash-cobranca-referencia.md §2.4) — garante que
/// <see cref="ConvertSyncUploadResponse"/>/<see cref="DadosConvertidos"/>
/// continuam fiéis ao contrato se alguém mexer nos DTOs no futuro.
/// </summary>
public class ConvertSyncUploadResponseTests
{
    private const string JsonExemplo = """
    {
      "appId": "cash-cobranca",
      "id": "11111111-1111-1111-1111-111111111111",
      "success": true,
      "outputFormat": "json",
      "binary": false,
      "data": {
        "arquivo": {
          "banco": "999",
          "codigoRemessaRetorno": "2",
          "dataGeracao": "2026-07-13",
          "horaGeracao": null,
          "numeroSequencialArquivo": 2,
          "versaoLayout": "040",
          "densidade": 1600,
          "codigoConvenio": "00009999990000099999",
          "nomeBanco": "BANCO EXEMPLO S.A.",
          "reservadoBanco": null,
          "reservadoEmpresa": null,
          "empresa": {
            "tipoInscricao": "2",
            "numeroInscricao": "12345678000199",
            "nome": "IMPORTADORA EXEMPLO LTDA"
          },
          "conta": {
            "agencia": "00001",
            "dvAgencia": "9",
            "conta": "000900000900",
            "dvConta": "8",
            "dvAgenciaConta": "2"
          }
        },
        "lote": {
          "numero": "0001",
          "tipoOperacao": "T",
          "tipoServico": "01",
          "versaoLayout": "030",
          "codigoConvenio": "00009999990000099999",
          "mensagem1": null,
          "mensagem2": null,
          "numeroRemessaRetorno": 2,
          "dataGravacao": "2026-07-13",
          "dataCredito": null,
          "empresa": {
            "tipoInscricao": "2",
            "numeroInscricao": "12345678000199",
            "nome": "IMPORTADORA EXEMPLO LTDA"
          },
          "conta": {
            "agencia": "00001",
            "dvAgencia": "9",
            "conta": "000900000900",
            "dvConta": "8",
            "dvAgenciaConta": "2"
          }
        },
        "titulos": [
          {
            "cliente": {
              "contaHeader": "000900000900",
              "documento": { "tipo": null, "codigo": "2", "inscricao": "12345678000199" }
            },
            "sacado": {
              "documento": { "tipo": null, "codigo": "2", "inscricao": "98765432000188" },
              "nome": "CONFECCOES EXEMPLO LTDA"
            },
            "numeroCarteira": "2",
            "nossoNumero": "00000000001",
            "codigoBanco": "999",
            "correspondente": { "codigoModalidade": "000", "banco": "999", "nossoNumero": "000000000000" },
            "seuNumero": "000001/01",
            "valorNominal": 17659.5,
            "dataVencimento": "2026-07-09",
            "campoLivre": "000001/0001",
            "codigoIndice": "09",
            "contrato": null,
            "ocorrencia": { "codigo": "06", "descricao": null },
            "motivos": "0000000000",
            "cobrador": { "banco": "001", "agencia": "00001", "dvAgencia": null },
            "valorPago": 17659.5,
            "valorLiquido": 17659.5,
            "valorDesconto": 0,
            "valorAbatimento": 0,
            "valorJurosMultaEncargos": 0,
            "valorIof": 0,
            "valorOutrasDespesas": 0,
            "valorOutrosCreditos": 0,
            "valorTarifaCustas": 0,
            "dataOcorrencia": "2026-07-10",
            "dataCredito": "2026-07-13",
            "alegacaoSacado": { "codigo": "0000", "data": "00000000", "valor": 0, "complemento": null },
            "numeroRegistro": 1,
            "direcionamentoCobranca": "0",
            "usoExclusivoAsa": "00",
            "modalidadeComBancoCedente": "112"
          }
        ],
        "totais": {
          "titulos": 2,
          "quantidadeRegistros": 2,
          "valorTotalCobrancaSimples": 0
        }
      }
    }
    """;

    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Deve_desserializar_envelope()
    {
        var resposta = JsonSerializer.Deserialize<ConvertSyncUploadResponse>(JsonExemplo, JsonOpcoes);

        Assert.NotNull(resposta);
        Assert.True(resposta.Success);
        Assert.Equal("cash-cobranca", resposta.AppId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", resposta.Id);
    }

    [Fact]
    public void Deve_desserializar_arquivo_e_lote()
    {
        var resposta = JsonSerializer.Deserialize<ConvertSyncUploadResponse>(JsonExemplo, JsonOpcoes)!;

        Assert.Equal("999", resposta.Data.Arquivo.Banco);
        Assert.Equal("BANCO EXEMPLO S.A.", resposta.Data.Arquivo.NomeBanco);
        Assert.Equal("12345678000199", resposta.Data.Arquivo.Empresa.NumeroInscricao);
        Assert.Equal("0001", resposta.Data.Lote.Numero);
        Assert.Equal(2, resposta.Data.Lote.NumeroRemessaRetorno);
    }

    [Fact]
    public void Deve_desserializar_titulo_com_valores_e_ocorrencia()
    {
        var resposta = JsonSerializer.Deserialize<ConvertSyncUploadResponse>(JsonExemplo, JsonOpcoes)!;

        var titulo = Assert.Single(resposta.Data.Titulos);
        Assert.Equal("CONFECCOES EXEMPLO LTDA", titulo.Sacado.Nome);
        Assert.Equal(17659.5m, titulo.ValorNominal);
        Assert.Equal(17659.5m, titulo.ValorPago);
        Assert.Equal("06", titulo.Ocorrencia!.Codigo);
        Assert.Equal("000001/01", titulo.SeuNumero);
    }

    [Fact]
    public void Deve_desserializar_totais()
    {
        var resposta = JsonSerializer.Deserialize<ConvertSyncUploadResponse>(JsonExemplo, JsonOpcoes)!;

        Assert.Equal(2, resposta.Data.Totais.Titulos);
        Assert.Equal(2, resposta.Data.Totais.QuantidadeRegistros);
    }
}
