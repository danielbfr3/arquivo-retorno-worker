namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// Resposta de <c>POST /v1/convert/async/upload</c> —
/// <c>{jobId, appId, id, status:"pending", statusUrl}</c>, shape
/// registrado em docs/cash-cobranca-referencia.md §2.4.
///
/// Não traz o arquivo convertido: o endpoint só enfileira. O resultado
/// chega depois pela mensagem de conclusão, que devolve o mesmo
/// <see cref="Id"/> (= <c>Cobranca.Arquivo.ArquivoID</c>). Consumir essa
/// conclusão é escopo de outro worker do ecossistema — este robô termina
/// no aceite.
/// </summary>
public sealed record ConvertAsyncUploadResponse
{
    public string? JobId { get; init; }
    public required string AppId { get; init; }
    public required string Id { get; init; }

    /// <summary>"pending" no aceite. Qualquer outro valor é tratado como
    /// recusa pelo client — ver <see cref="ConversaoNaoAceitaException"/>.</summary>
    public string? Status { get; init; }

    public string? StatusUrl { get; init; }

    /// <summary>TODO(a-confirmar): "pending" é o único valor de aceite
    /// observado. Se a API usar outros ("queued", "accepted"), este é o
    /// único lugar a ajustar.</summary>
    public bool Aceito => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);
}
