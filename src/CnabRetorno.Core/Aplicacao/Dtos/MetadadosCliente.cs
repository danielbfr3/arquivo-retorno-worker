using System.Text.Json;
using System.Text.Json.Serialization;

namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// Os dados do cliente que acompanham a planilha na chamada do conversor —
/// o "corpo da mensagem" em JSON: CNPJ (do nome do arquivo) e razão social
/// (chave <c>DocumentoDados.ChaveRazaoSocial</c> no mesmo JSON de
/// <c>Cobranca.DocumentoDados</c> que preenche a planilha).
///
/// Vai como campo de texto do multipart (nome do campo em
/// <c>Conversao:CampoMetadados</c>), e não no corpo da requisição: o
/// endpoint é multipart com upload de arquivo, não JSON body — ver
/// docs/cash-cobranca-referencia.md §2.4.
///
/// O CNPJ vai normalizado (só dígitos, 14 posições), do mesmo jeito que é
/// gravado em <c>Cobranca.Arquivo.ClienteDocumento</c> — o nome do arquivo
/// pode vir pontuado, e mandar as duas grafias diferentes pro mesmo
/// cliente quebraria a conciliação do outro lado.
/// </summary>
public sealed record MetadadosCliente(
    [property: JsonPropertyName("cnpj")] string Cnpj,
    [property: JsonPropertyName("razaoSocial")] string RazaoSocial)
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        // Sem escape agressivo de não-ASCII: razão social tem acento, e
        // "JOSÉ" no lugar de "JOSÉ" é válido mas ilegível em log e
        // no payload que o time do conversor inspeciona.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Serializar() => JsonSerializer.Serialize(this, Opcoes);
}
