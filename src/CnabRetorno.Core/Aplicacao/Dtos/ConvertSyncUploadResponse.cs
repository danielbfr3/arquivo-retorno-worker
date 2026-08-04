using System.Text.Json.Serialization;

namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// Envelope da resposta de <c>POST /v1/convert/sync/upload</c> — o
/// envelope (<c>appId</c>/<c>id</c>/<c>success</c>/<c>outputFormat</c>/
/// <c>binary</c>/<c>data</c>) foi modelado 1:1 a partir de exemplo real da
/// conversão CNAB → JSON.
///
/// Aqui o sentido é o inverso (JSON → CNAB), então <c>data</c> traz o
/// arquivo, não um objeto: texto CNAB240 quando <see cref="Binary"/> é
/// <c>false</c>, base64 quando é <c>true</c>. Ver <see
/// cref="ConteudoCnab"/>, que resolve os dois casos.
///
/// <see cref="Success"/> pode vir <c>false</c> com <see cref="Data"/>
/// ausente; o chamador precisa checar antes de usar o conteúdo.
///
/// TODO(a-confirmar): nenhum exemplo real da resposta JSON → CNAB foi
/// fornecido — a forma de <c>data</c> neste sentido é dedução a partir dos
/// campos <c>outputFormat</c>/<c>binary</c> do envelope conhecido.
/// </summary>
public sealed record ConvertSyncUploadResponse
{
    public required string AppId { get; init; }
    public required string Id { get; init; }
    public required bool Success { get; init; }
    public string? OutputFormat { get; init; }
    public bool Binary { get; init; }

    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>
    /// Bytes do CNAB240 gerado. Latin1 (não UTF-8) de propósito: o layout
    /// FEBRABAN é posicional e conta **bytes**, então um caractere
    /// acentuado num nome de favorecido em UTF-8 ocuparia duas posições e
    /// deslocaria a linha inteira.
    /// </summary>
    /// <exception cref="InvalidOperationException">Resposta sem conteúdo.</exception>
    public byte[] ConteudoCnab()
    {
        if (string.IsNullOrEmpty(Data))
            throw new InvalidOperationException(
                $"Conversão {Id} voltou sem conteúdo (success={Success}, binary={Binary}).");

        return Binary
            ? Convert.FromBase64String(Data)
            : System.Text.Encoding.Latin1.GetBytes(Data);
    }
}
