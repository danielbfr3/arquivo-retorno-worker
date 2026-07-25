namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// Envelope da resposta de POST /v1/convert/sync/upload — modelado 1:1 a
/// partir de exemplo real. <see cref="Success"/> pode vir <c>false</c> com
/// <see cref="Data"/> parcial/ausente; o chamador precisa checar antes de
/// acessar <see cref="Data"/> (ver <c>Http.ConversaoCnabFalhouException</c>).
/// </summary>
public sealed record ConvertSyncUploadResponse
{
    public required string AppId { get; init; }
    public required string Id { get; init; }
    public required bool Success { get; init; }
    public string? OutputFormat { get; init; }
    public bool Binary { get; init; }
    public required DadosConvertidos Data { get; init; }
}
