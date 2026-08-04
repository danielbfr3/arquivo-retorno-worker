using CnabRetorno.Core.Cnab240;

namespace CnabRetorno.PagamentoRetorno.Worker.Json;

/// <summary>
/// Os segmentos CNAB da remessa original de um pagamento, extraídos do
/// campo <c>Linhas</c> das tabelas <c>*Info</c>.
///
/// É a fonte de verdade preferida na montagem do retorno: são exatamente
/// os bytes que o cliente enviou. Remontar tudo a partir das colunas
/// arriscaria devolver um dado normalizado — nome truncado de outro jeito,
/// conta sem zeros à esquerda, agência com DV separado — e o cliente
/// concilia o retorno contra o que ele mandou, não contra o que ficou no
/// nosso banco.
///
/// Todos os campos são anuláveis: pagamento criado por API não tem linha
/// nenhuma, e aí a montagem cai nas colunas.
/// </summary>
public sealed record SegmentosRemessa(string? A, string? B, string? J, string? J52)
{
    public static readonly SegmentosRemessa Vazio = new(null, null, null, null);

    public bool TemSegmentoA => A is not null;
    public bool TemSegmentoJ => J is not null;

    public static SegmentosRemessa Analisar(string? linhas)
    {
        if (string.IsNullOrWhiteSpace(linhas)) return Vazio;

        string? a = null, b = null, j = null, j52 = null;

        foreach (var linha in Cnab240Campos.QuebrarLinhas(linhas))
        {
            // Só registros de detalhe interessam — o que fica gravado por
            // pagamento não deveria incluir header/trailer, mas se incluir,
            // ignorar é mais seguro que interpretar.
            if (Cnab240Campos.TipoRegistro(linha) != '3') continue;

            switch (Cnab240Campos.Segmento(linha))
            {
                case Cnab240Pagamento.SegmentoA.Codigo:
                    a ??= linha;
                    break;
                case Cnab240Pagamento.SegmentoB.Codigo:
                    b ??= linha;
                    break;
                case Cnab240Pagamento.SegmentoJ.Codigo when Cnab240Pagamento.SegmentoJ.EhRegistroOpcional(linha):
                    j52 ??= linha;
                    break;
                case Cnab240Pagamento.SegmentoJ.Codigo:
                    j ??= linha;
                    break;
            }
        }

        return new SegmentosRemessa(a, b, j, j52);
    }
}
