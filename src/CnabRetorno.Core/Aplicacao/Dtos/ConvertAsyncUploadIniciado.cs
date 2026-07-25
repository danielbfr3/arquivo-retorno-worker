namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// Resposta de POST /v1/convert/async/upload — submissão do job (JSON →
/// CNAB no layout do cliente), não bloqueia. O resultado chega depois via
/// mensageria (Robô 2, ver <c>ConversaoConcluidaMessage</c>) — este robô
/// não faz polling de <see cref="StatusUrl"/>.
///
/// TODO(a-confirmar): o nome do pipeline reverso (JSON → CNAB do cliente)
/// não veio em nenhum exemplo — só o de CNAB → JSON
/// ("conversao-cobranca-retorno-para-json") foi confirmado.
/// </summary>
public sealed record ConvertAsyncUploadIniciado
{
    public required string JobId { get; init; }
    public required string AppId { get; init; }
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? StatusUrl { get; init; }
}
