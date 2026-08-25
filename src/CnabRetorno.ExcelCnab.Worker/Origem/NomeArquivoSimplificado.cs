using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Origem;

public class NomenclaturaOptions
{
    public const string Secao = "Nomenclatura";

    /// <summary>Máscara do nome do arquivo, sem extensão, com o token
    /// <c>{cnpj}</c> no lugar do documento do cliente. Padrão:
    /// <c>Simplificado_{cnpj}</c>. É configuração porque o prefixo é
    /// convenção de quem deposita o arquivo, não regra de negócio deste
    /// robô — se amanhã a planilha chegar como <c>Cobranca_{cnpj}</c>,
    /// muda a chave e não o código.</summary>
    public string Mascara { get; set; } = "Simplificado_{cnpj}";

    /// <summary>Extensões aceitas. Qualquer outra coisa na pasta vai pra
    /// quarentena — inclusive um <c>.csv</c> que alguém salvou por engano,
    /// que o pipeline rejeitaria lá na frente sem ninguém ver.</summary>
    public string[] Extensoes { get; set; } = [".xlsx", ".xls"];
}

/// <summary>Nome de arquivo reconhecido: o CNPJ já normalizado em 14
/// dígitos e a extensão original (que decide o content-type do
/// upload).</summary>
public sealed record NomeReconhecido(string Cnpj, string Extensao);

/// <summary>
/// Lê o CNPJ do nome do arquivo — <c>Simplificado_12345678000199.xlsx</c>.
///
/// É a única identificação do cliente que existe: o robô não abre a
/// planilha. Por isso a extração é estrita — um nome fora do padrão vira
/// quarentena, nunca um palpite. Enviar a planilha de um cliente com o
/// CNPJ de outro é pior que não enviar.
///
/// O CNPJ pode vir pontuado (<c>12.345.678.0001-99</c>): a pontuação é
/// aceita na leitura e descartada na normalização, porque quem nomeia o
/// arquivo é uma pessoa e as duas grafias significam o mesmo cliente. O
/// que vale daqui pra frente é sempre a forma de 14 dígitos.
///
/// A barra do CNPJ canônico (<c>.../0001-99</c>) não entra na conta: é
/// separador de caminho no Linux e caractere proibido em nome de arquivo
/// no Windows e no SMB, então um arquivo com ela simplesmente não existe.
/// </summary>
public class NomeArquivoSimplificado
{
    private readonly Regex _padrao;
    private readonly string[] _extensoes;

    public NomeArquivoSimplificado(IOptions<NomenclaturaOptions> opcoes)
    {
        var opt = opcoes.Value;

        _extensoes = [.. opt.Extensoes.Select(e => e.StartsWith('.') ? e : "." + e)];

        // O token vira grupo nomeado; o resto da máscara é escapado, pra
        // que um ponto na máscara case com ponto e não com "qualquer
        // caractere".
        var partes = opt.Mascara.Split("{cnpj}", StringSplitOptions.None);
        if (partes.Length != 2)
            throw new InvalidOperationException(
                $"Nomenclatura:Mascara precisa conter exatamente um token {{cnpj}} — recebido '{opt.Mascara}'.");

        _padrao = new Regex(
            $"^{Regex.Escape(partes[0])}(?<cnpj>[0-9.-]{{14,18}}){Regex.Escape(partes[1])}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public bool TentarReconhecer(string nomeArquivo, out NomeReconhecido reconhecido)
    {
        reconhecido = default!;

        var extensao = Path.GetExtension(nomeArquivo);
        if (!_extensoes.Contains(extensao, StringComparer.OrdinalIgnoreCase)) return false;

        var casamento = _padrao.Match(Path.GetFileNameWithoutExtension(nomeArquivo));
        if (!casamento.Success) return false;

        var digitos = new string([.. casamento.Groups["cnpj"].Value.Where(char.IsAsciiDigit)]);
        if (digitos.Length != 14) return false;

        reconhecido = new NomeReconhecido(digitos, extensao.ToLowerInvariant());
        return true;
    }
}
