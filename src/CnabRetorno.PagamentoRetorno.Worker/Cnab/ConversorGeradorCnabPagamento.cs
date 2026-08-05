using System.Text.Json;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.PagamentoRetorno.Worker.Cnab;

/// <summary>
/// Modo padrão (<c>Geracao:Modo = Conversor</c>): serializa o JSON e pede
/// pro conversor externo transformar em CNAB240. É o caminho homologado
/// pelo time — ver <see cref="CnabDiretoGeradorCnabPagamento"/> pro
/// trade-off da alternativa.
/// </summary>
public class ConversorGeradorCnabPagamento(ILayoutConversaoApiClient conversor) : IGeradorCnabPagamento
{
    private static readonly JsonSerializerOptions JsonOpcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<byte[]> GerarAsync(
        RetornoPagamentoJson dados, string documento, string nomeArquivo, string id, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(dados, JsonOpcoes);

        var conversao = await conversor.ConverterJsonParaCnabAsync(json, $"{nomeArquivo}.json", id, ct);

        return conversao.ConteudoCnab();
    }
}
