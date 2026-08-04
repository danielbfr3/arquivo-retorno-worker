using System.Globalization;
using System.Text;

namespace CnabRetorno.Core.Cnab240;

/// <summary>
/// Leitura/escrita posicional de uma linha CNAB 240 (posições 1-based,
/// exatamente como o manual FEBRABAN 240 V10.11 descreve — mesma convenção
/// do parser removido na simplificação anterior deste projeto, ver
/// docs/regras-de-negocio.md). Cada linha tem sempre 240 caracteres.
///
/// Escrever aqui **não muta** a string recebida (strings são imutáveis em
/// C#) — os métodos "Escrever*" retornam uma nova linha com o trecho
/// substituído, mantendo o resto intacto.
/// </summary>
public static class Cnab240Campos
{
    public const int TamanhoLinha = 240;

    /// <summary>Tipo de registro (posição 8) — '0' header arquivo, '1'
    /// header lote, '3' detalhe, '5' trailer lote, '9' trailer arquivo.</summary>
    public static char TipoRegistro(string linha) => linha[7];

    /// <summary>Segmento do registro de detalhe (posição 14) — 'T', 'U', etc.</summary>
    public static char Segmento(string linha) => linha[13];

    public static string Ler(string linha, int de, int ate)
        => linha.Substring(de - 1, ate - de + 1);

    public static string LerTrim(string linha, int de, int ate)
        => Ler(linha, de, ate).Trim();

    public static int LerInteiro(string linha, int de, int ate)
    {
        var bruto = LerTrim(linha, de, ate);
        return bruto.Length == 0 ? 0 : int.Parse(bruto, CultureInfo.InvariantCulture);
    }

    /// <summary>Valor numérico com 2 decimais implícitos (formato Num 13,2 / 15,2).</summary>
    public static decimal LerValor(string linha, int de, int ate)
    {
        var bruto = LerTrim(linha, de, ate);
        if (bruto.Length == 0) return 0m;
        return decimal.Parse(bruto, CultureInfo.InvariantCulture) / 100m;
    }

    /// <summary>Substitui o trecho [de, ate] por <paramref name="valor"/>,
    /// alinhado à esquerda e preenchido com espaços à direita (campos
    /// alfanuméricos). Trunca se o valor for maior que o campo.</summary>
    public static string EscreverTexto(string linha, int de, int ate, string valor)
    {
        var largura = ate - de + 1;
        var ajustado = valor.Length > largura ? valor[..largura] : valor.PadRight(largura);
        return Substituir(linha, de, ate, ajustado);
    }

    /// <summary>Substitui o trecho [de, ate] por <paramref name="valor"/>,
    /// alinhado à direita e preenchido com zeros à esquerda (campos
    /// numéricos).</summary>
    public static string EscreverNumero(string linha, int de, int ate, long valor)
    {
        var largura = ate - de + 1;
        return Substituir(linha, de, ate, valor.ToString(CultureInfo.InvariantCulture).PadLeft(largura, '0'));
    }

    /// <summary>Valor decimal com 2 casas implícitas, mesma convenção de <see cref="LerValor"/>.</summary>
    public static string EscreverValor(string linha, int de, int ate, decimal valor)
        => EscreverNumero(linha, de, ate, (long)Math.Round(valor * 100m, MidpointRounding.AwayFromZero));

    private static string Substituir(string linha, int de, int ate, string trecho)
    {
        var largura = ate - de + 1;
        if (trecho.Length != largura)
            throw new ArgumentException(
                $"Trecho com {trecho.Length} posições, esperado {largura} (campo {de}-{ate}).");

        return string.Concat(linha.AsSpan(0, de - 1), trecho, linha.AsSpan(ate));
    }

    /// <summary>
    /// Quebra um bloco CNAB em linhas de 240 posições.
    ///
    /// Aceita as duas formas que aparecem na prática: com separador de
    /// linha (arquivo em disco) e sem separador nenhum (o campo
    /// <c>Linhas</c> das tabelas <c>*Info</c>, onde os segmentos vêm
    /// concatenados). Linhas com comprimento diferente de 240 são
    /// descartadas — resto de arquivo truncado não pode virar registro.
    /// </summary>
    public static IReadOnlyList<string> QuebrarLinhas(string bloco)
    {
        if (string.IsNullOrEmpty(bloco)) return [];

        var normalizado = bloco.Replace("\r\n", "\n");

        var comSeparador = normalizado
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length == TamanhoLinha)
            .ToList();

        if (comSeparador.Count > 0) return comSeparador;

        var semSeparador = new List<string>(normalizado.Length / TamanhoLinha);
        for (var i = 0; i + TamanhoLinha <= normalizado.Length; i += TamanhoLinha)
            semSeparador.Add(normalizado.Substring(i, TamanhoLinha));

        return semSeparador;
    }

    /// <summary>Lê um bloco CNAB de bytes em Latin1 — o layout é
    /// posicional e conta bytes, então decodificar como UTF-8 deslocaria
    /// todas as posições de uma linha com acento.</summary>
    public static IReadOnlyList<string> QuebrarLinhas(byte[] conteudo)
        => QuebrarLinhas(Encoding.Latin1.GetString(conteudo));
}
