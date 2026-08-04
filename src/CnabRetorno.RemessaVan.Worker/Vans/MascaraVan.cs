using System.Text;
using System.Text.RegularExpressions;

namespace CnabRetorno.RemessaVan.Worker.Vans;

/// <summary>Tipo de arquivo que a máscara identifica. O robô processa
/// apenas <see cref="Remessa"/>; as máscaras de <see cref="Retorno"/>
/// existem na configuração para que um arquivo de retorno que apareça na
/// pasta seja **reconhecido e ignorado**, em vez de cair na quarentena
/// como "não reconhecido".</summary>
public enum TipoArquivoVan
{
    Remessa,
    Retorno,
}

/// <summary>
/// Uma linha da tabela de máscaras das VANs (Finnet, SuplyMidia,
/// Accesstage, Nexxera, Kobana). Vem de configuração
/// (<c>Vans:Mascaras</c>) e não do banco: é dado de integração que muda
/// por ambiente, e mantê-lo em appsettings deixa a inclusão de uma VAN
/// nova ser um deploy de configuração.
/// </summary>
public class MascaraVanConfig
{
    public string Van { get; set; } = default!;

    /// <summary>CNPJ do cliente dono desta máscara. Usado quando a máscara
    /// não tem <c>{cnpj}</c> capturável — aí o documento não sai do nome do
    /// arquivo, sai daqui.</summary>
    public string? Cnpj { get; set; }

    public TipoArquivoVan Tipo { get; set; } = TipoArquivoVan.Remessa;

    /// <summary>Padrão do nome do arquivo. Ver <see cref="MascaraVan"/>
    /// para a sintaxe.</summary>
    public string Mascara { get; set; } = default!;
}

public class VansOptions
{
    public const string Secao = "Vans";

    public List<MascaraVanConfig> Mascaras { get; set; } = [];
}

/// <summary>Arquivo reconhecido: qual VAN, de que cliente, e de que tipo.</summary>
public sealed record ArquivoReconhecido(string Van, string Cnpj, TipoArquivoVan Tipo);

/// <summary>
/// Compila as máscaras das VANs em expressões regulares e casa nomes de
/// arquivo contra elas.
///
/// Sintaxe da máscara (derivada da tabela enviada pelo time em
/// 03/08/2026 — ver docs/regras-de-negocio.md §Robô 1):
/// <list type="bullet">
///   <item><c>{cnpj}</c> — captura 14 dígitos do nome do arquivo. É o
///   passo "extrair dados de CNPJ do cliente do nome do arquivo".</item>
///   <item><c>DDMMYY</c>, <c>DDMMYYYY</c>, <c>YYYYMMDD</c>, <c>DDMM</c> —
///   data no nome; casa a quantidade equivalente de dígitos.</item>
///   <item><c>*</c> — qualquer sequência; <c>?</c> — um caractere.</item>
///   <item>Todo o resto é literal.</item>
/// </list>
///
/// Os tokens de data são reconhecidos como **sequências inteiras**, não
/// letra a letra: senão o <c>M</c> de <c>.REM</c> viraria um dígito e
/// nenhum arquivo Nexxera casaria. Pelo mesmo motivo os tokens são
/// testados do mais longo pro mais curto (<c>DDMMYYYY</c> antes de
/// <c>DDMM</c>).
///
/// Comparação sem diferenciar maiúsculas: a mesma VAN aparece com
/// <c>.REM</c>, <c>.rem</c> e <c>.Rem</c> nos exemplos reais.
/// </summary>
public class MascaraVan
{
    private static readonly (string Token, int Digitos)[] TokensData =
    [
        ("DDMMYYYY", 8),
        ("YYYYMMDD", 8),
        ("DDMMYY", 6),
        ("DDMM", 4),
    ];

    private const string TokenCnpj = "{cnpj}";
    private const string GrupoCnpj = "cnpj";

    private readonly MascaraVanConfig _config;
    private readonly Regex _regex;

    public MascaraVan(MascaraVanConfig config)
    {
        _config = config;
        _regex = new Regex(Compilar(config.Mascara), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Van => _config.Van;
    public TipoArquivoVan Tipo => _config.Tipo;

    /// <summary>Casa o nome do arquivo. O CNPJ sai do grupo capturado
    /// quando a máscara tem <c>{cnpj}</c>; senão, do CNPJ configurado.
    /// Máscara sem <c>{cnpj}</c> e sem CNPJ na configuração não identifica
    /// cliente nenhum — não casa, e o arquivo vai pra quarentena em vez de
    /// ser gravado sem dono.</summary>
    public bool TentarCasar(string nomeArquivo, out ArquivoReconhecido reconhecido)
    {
        reconhecido = default!;

        var casamento = _regex.Match(nomeArquivo);
        if (!casamento.Success) return false;

        var grupo = casamento.Groups[GrupoCnpj];
        var cnpj = grupo.Success ? grupo.Value : _config.Cnpj;
        if (string.IsNullOrWhiteSpace(cnpj)) return false;

        reconhecido = new ArquivoReconhecido(_config.Van, cnpj, _config.Tipo);
        return true;
    }

    internal static string Compilar(string mascara)
    {
        var padrao = new StringBuilder("^");
        var i = 0;

        while (i < mascara.Length)
        {
            if (string.CompareOrdinal(mascara, i, TokenCnpj, 0, TokenCnpj.Length) == 0)
            {
                padrao.Append($"(?<{GrupoCnpj}>\\d{{14}})");
                i += TokenCnpj.Length;
                continue;
            }

            var tokenData = TokensData.FirstOrDefault(t =>
                string.Compare(mascara, i, t.Token, 0, t.Token.Length, StringComparison.OrdinalIgnoreCase) == 0);

            if (tokenData.Token is not null)
            {
                padrao.Append($"\\d{{{tokenData.Digitos}}}");
                i += tokenData.Token.Length;
                continue;
            }

            padrao.Append(mascara[i] switch
            {
                '*' => ".*",
                '?' => ".",
                var c => Regex.Escape(c.ToString()),
            });
            i++;
        }

        return padrao.Append('$').ToString();
    }
}

/// <summary>Conjunto de máscaras configuradas, na ordem do appsettings —
/// a primeira que casar vence.</summary>
public class CatalogoMascarasVan
{
    private readonly List<MascaraVan> _mascaras;

    public CatalogoMascarasVan(Microsoft.Extensions.Options.IOptions<VansOptions> opcoes)
        => _mascaras = [.. opcoes.Value.Mascaras.Select(m => new MascaraVan(m))];

    public bool TentarReconhecer(string nomeArquivo, out ArquivoReconhecido reconhecido)
    {
        foreach (var mascara in _mascaras)
            if (mascara.TentarCasar(nomeArquivo, out reconhecido))
                return true;

        reconhecido = default!;
        return false;
    }
}
