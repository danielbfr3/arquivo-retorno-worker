using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RemessaVan.Worker.Vans;

public class NomenclaturaOptions
{
    public const string Secao = "Nomenclatura";

    /// <summary>
    /// Template do nome no padrão ASA. Ver <see cref="NomeArquivoAsa"/>
    /// para os tokens disponíveis.
    ///
    /// TODO(a-confirmar): o padrão ASA oficial não foi especificado. O
    /// default abaixo é o espelho da convenção que o próprio ASA já usa
    /// nos arquivos de **retorno** que envia às VANs
    /// (<c>ArquivoRetorno_&lt;cnpj&gt;_&lt;ddMMyyyy&gt;_&lt;HHmmss&gt;.txt</c>,
    /// visível na tabela de máscaras de 03/08/2026), aplicada ao sentido
    /// de remessa. Trocar o template é mudança de configuração, não de
    /// código.
    /// </summary>
    public string Template { get; set; } = "ArquivoRemessa_{documento}_{data:ddMMyyyy}_{data:HHmmss}{ext}";

    /// <summary>Extensão usada quando o arquivo de origem não tem
    /// nenhuma.</summary>
    public string ExtensaoPadrao { get; set; } = ".txt";
}

/// <summary>Tudo que um template pode referenciar.</summary>
public sealed record DadosNomeArquivo(
    string Documento,
    string? ContaHeader,
    string Van,
    Guid ArquivoId,
    string NomeOriginal,
    DateTime Momento);

/// <summary>
/// Renderiza o nome no padrão ASA a partir de um template configurável.
///
/// Tokens:
/// <list type="bullet">
///   <item><c>{documento}</c> — CNPJ/CPF do cliente.</item>
///   <item><c>{contaHeader}</c> — conta do cliente (vazio se não
///   resolvida).</item>
///   <item><c>{van}</c> — nome da VAN de origem.</item>
///   <item><c>{guid}</c> — o <c>ArquivoID</c>, o mesmo id do registro e do
///   storage.</item>
///   <item><c>{original}</c> — nome do arquivo como veio da VAN, sem
///   extensão.</item>
///   <item><c>{ext}</c> — extensão original, com ponto.</item>
///   <item><c>{data:&lt;formato&gt;}</c> — qualquer formato de data do
///   .NET, ex.: <c>{data:ddMMyyyy}</c>, <c>{data:HHmmss}</c>.</item>
/// </list>
///
/// Caracteres inválidos pra nome de arquivo são removidos do resultado:
/// os valores vêm de dados externos (nome de VAN, nome original), e um
/// separador de caminho que passasse batido escreveria fora da pasta de
/// destino.
/// </summary>
public class NomeArquivoAsa(IOptions<NomenclaturaOptions> opcoes)
{
    private static readonly Regex Token = new(@"\{(?<nome>\w+)(?::(?<formato>[^}]+))?\}", RegexOptions.CultureInvariant);

    private readonly NomenclaturaOptions _opt = opcoes.Value;

    public string Renderizar(DadosNomeArquivo dados)
    {
        var extensao = Path.GetExtension(dados.NomeOriginal);
        if (string.IsNullOrEmpty(extensao)) extensao = _opt.ExtensaoPadrao;

        var renderizado = Token.Replace(_opt.Template, casamento =>
        {
            var nome = casamento.Groups["nome"].Value.ToLowerInvariant();
            var formato = casamento.Groups["formato"].Value;

            return nome switch
            {
                "documento" => dados.Documento,
                "contaheader" => dados.ContaHeader ?? string.Empty,
                "van" => dados.Van,
                "guid" => dados.ArquivoId.ToString(),
                "original" => Path.GetFileNameWithoutExtension(dados.NomeOriginal),
                "ext" => extensao,
                "data" => dados.Momento.ToString(
                    string.IsNullOrEmpty(formato) ? "yyyyMMddHHmmss" : formato,
                    CultureInfo.InvariantCulture),
                _ => casamento.Value, // token desconhecido fica literal — erro visível no nome
            };
        });

        return Sanitizar(renderizado);
    }

    private static string Sanitizar(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        return string.Concat(nome.Where(c => !invalidos.Contains(c)));
    }
}
