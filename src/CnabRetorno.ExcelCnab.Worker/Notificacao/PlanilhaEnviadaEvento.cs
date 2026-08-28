using System.Text.Json;
using System.Text.Json.Serialization;

namespace CnabRetorno.ExcelCnab.Worker.Notificacao;

/// <summary>
/// O corpo da mensagem publicada no tópico — o aviso de que o worker
/// terminou o processamento de uma planilha.
///
/// "Terminou" aqui quer dizer: a planilha foi aceita pelo conversor e o
/// arquivo saiu da pasta de entrada. **Não** quer dizer que o CNAB está
/// pronto — quem avisa isso é a mensagem de conclusão do próprio
/// conversor, que chega depois. O <see cref="ArquivoId"/> é o mesmo nos
/// dois eventos, então quem consome consegue parear.
///
/// Os nomes dos campos são escritos à mão com <c>[JsonPropertyName]</c>,
/// e não deixados por conta da convenção de serialização: é contrato com
/// quem assina o tópico, e renomear uma propriedade em C# não pode mudar
/// o payload em silêncio.
/// </summary>
public sealed record PlanilhaEnviadaEvento
{
    /// <summary>Discriminador do tipo de evento. Fixo hoje — existe pra
    /// que um segundo tipo de mensagem no mesmo tópico não force quem
    /// consome a adivinhar pelo formato.</summary>
    [JsonPropertyName("evento")]
    public string Evento { get; init; } = "planilha-enviada-para-conversao";

    /// <summary>O <c>Cobranca.Arquivo.ArquivoID</c> — a chave que amarra
    /// este aviso, a linha no banco e a conclusão da conversão.</summary>
    [JsonPropertyName("arquivoId")]
    public required Guid ArquivoId { get; init; }

    [JsonPropertyName("arquivoNome")]
    public required string ArquivoNome { get; init; }

    [JsonPropertyName("cnpj")]
    public required string Cnpj { get; init; }

    [JsonPropertyName("razaoSocial")]
    public required string RazaoSocial { get; init; }

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("pipeline")]
    public required string Pipeline { get; init; }

    /// <summary>Id do job devolvido pelo conversor no aceite. Anulável: o
    /// aceite pode vir sem ele, e isso não impede o aviso.</summary>
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("ocorridoEm")]
    public required DateTimeOffset OcorridoEm { get; init; }

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        // Mesmo motivo do MetadadosCliente: razão social tem acento, e o
        // escape agressivo padrão deixaria o payload ilegível pra quem
        // inspeciona a mensagem.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Serializar() => JsonSerializer.Serialize(this, Opcoes);
}
