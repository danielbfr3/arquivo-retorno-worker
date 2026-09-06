using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Planilha;

public enum ComparacaoCabecalho
{
    Exata,
    IgnorarCaixaEEspacos,
}

public class PreenchimentoOptions
{
    public const string Secao = "Preenchimento";

    /// <summary>Como o cabeçalho da planilha (linha 1) é comparado com as
    /// chaves do JSON de <c>Cobranca.DocumentoDados.Dados</c>. Padrão
    /// tolerante a espaço/caixa, porque quem escreve a planilha e quem
    /// escreve o JSON são pessoas/sistemas diferentes.</summary>
    public ComparacaoCabecalho ComparacaoCabecalho { get; set; } = ComparacaoCabecalho.IgnorarCaixaEEspacos;

    /// <summary>Nome da aba a preencher. Vazio/nulo usa a primeira aba do
    /// arquivo — suficiente enquanto a planilha tiver uma única aba de
    /// dados.</summary>
    public string? NomeAba { get; set; }
}

/// <summary>Uma ou mais chaves do JSON de dados não bateram com nenhum
/// cabeçalho (linha 1) da planilha, ou o cabeçalho tem uma coluna
/// duplicada. Em qualquer um dos dois casos, a planilha é rejeitada: o
/// arquivo vai pra quarentena e nada é enviado ao conversor.</summary>
public sealed class ColunaNaoEncontradaException(IReadOnlyList<string> chaves)
    : Exception($"Nenhuma coluna na planilha corresponde ao(s) cabeçalho(s): {string.Join(", ", chaves)}")
{
    public IReadOnlyList<string> Chaves { get; } = chaves;
}

/// <summary>A planilha não tem nenhuma linha abaixo do cabeçalho — não há
/// onde escrever os valores.</summary>
public sealed class PlanilhaSemLinhasDeDadosException()
    : Exception("A planilha não tem nenhuma linha de dados abaixo do cabeçalho (linha 1).");

/// <summary>
/// Único lugar do worker que conhece o ClosedXML — mesmo princípio de
/// adaptador único já usado pro conversor (<c>LayoutConversaoApiClient</c>).
///
/// Abre a planilha em memória, casa cada chave do JSON de dados com um
/// cabeçalho da linha 1 e escreve o valor correspondente em todas as
/// linhas de dados existentes (linha 2 até a última usada) — o documento é
/// o mesmo em todas as linhas do arquivo, então o valor se repete.
///
/// Sem I/O de arquivo/rede/banco: testável inteiramente em memória.
/// </summary>
public class PreenchedorPlanilhaExcel(IOptions<PreenchimentoOptions> opcoes)
{
    private readonly PreenchimentoOptions _opt = opcoes.Value;

    public byte[] Preencher(byte[] original, IReadOnlyDictionary<string, string> valores)
    {
        using var entrada = new MemoryStream(original);
        using var workbook = new XLWorkbook(entrada);

        var planilha = string.IsNullOrWhiteSpace(_opt.NomeAba)
            ? workbook.Worksheets.First()
            : workbook.Worksheet(_opt.NomeAba);

        var colunaPorCabecalho = MapearColunas(planilha);

        var naoEncontradas = valores.Keys
            .Where(chave => !colunaPorCabecalho.ContainsKey(Normalizar(chave)))
            .ToList();
        if (naoEncontradas.Count > 0)
            throw new ColunaNaoEncontradaException(naoEncontradas);

        var ultimaLinha = planilha.LastRowUsed()?.RowNumber() ?? 1;
        if (ultimaLinha < 2)
            throw new PlanilhaSemLinhasDeDadosException();

        foreach (var (chave, valor) in valores)
        {
            var coluna = colunaPorCabecalho[Normalizar(chave)];
            for (var linha = 2; linha <= ultimaLinha; linha++)
                // Atribuição direta de string: o XLCellValue do ClosedXML
                // guarda o tipo Text explicitamente, sem tentar inferir
                // número — essencial pra não perder zero à esquerda em
                // campos como agência/conta/CNPJ.
                planilha.Cell(linha, coluna).Value = valor;
        }

        using var saida = new MemoryStream();
        workbook.SaveAs(saida);
        return saida.ToArray();
    }

    private Dictionary<string, int> MapearColunas(IXLWorksheet planilha)
    {
        var mapa = new Dictionary<string, int>();
        foreach (var celula in planilha.Row(1).CellsUsed())
        {
            var cabecalho = celula.GetString();
            if (string.IsNullOrWhiteSpace(cabecalho)) continue;

            // Dois cabeçalhos batendo com a mesma chave normalizada é
            // ambíguo — melhor falhar (quarentena) do que escolher um dos
            // dois em silêncio.
            if (!mapa.TryAdd(Normalizar(cabecalho), celula.Address.ColumnNumber))
                throw new ColunaNaoEncontradaException([$"{cabecalho} (cabeçalho duplicado)"]);
        }
        return mapa;
    }

    private string Normalizar(string cabecalho) =>
        _opt.ComparacaoCabecalho == ComparacaoCabecalho.Exata
            ? cabecalho
            : cabecalho.Trim().ToUpperInvariant();
}
