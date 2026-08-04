namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Projeção unificada de uma movimentação de pagamento — a união das cinco
/// duplas <c>&lt;Tipo&gt;</c> + <c>&lt;Tipo&gt;Info</c> da base
/// ASA_CASH_PAGAMENTO (Pix, Ted, Tef, Tricon, Boleto).
///
/// As cinco tabelas de cabeçalho têm exatamente a mesma estrutura de 15
/// campos (só muda a PK), então a parte comum é fiel. As tabelas
/// <c>Info</c> divergem, e por isso as propriedades específicas de
/// transferência (favorecido/banco) e de título (código de barras/
/// vencimento) convivem aqui como anuláveis: <see cref="Meio"/> diz quais
/// estão preenchidas. Cinco POCOs quase idênticos seriam pior — o consumo
/// é sempre "monta o segmento certo pro meio".
///
/// Projeção **sem chave** (só leitura), materializada por um UNION ALL —
/// ver <c>Persistencia.PagamentoDbContext</c>.
/// </summary>
public class MovimentacaoPagamento
{
    /// <summary>Constante literal na query (uma por ramo do UNION), não
    /// coluna do banco — é o que diz de qual tabela a linha veio.</summary>
    public short Meio { get; init; }

    public Guid PagamentoID { get; init; }
    public short CodigoStatus { get; init; }

    public string? ClienteContaHeader { get; init; }
    public short ClienteTipoDocumento { get; init; }
    public string ClienteDocumento { get; init; } = default!;

    public DateTime DataCriacao { get; init; }
    public DateTime? DataAtualizacao { get; init; }

    /// <summary>Ocorrência FEBRABAN já gravada pelo sistema de pagamento —
    /// <c>varchar(10)</c>, mesma largura do campo G059. Ver <see
    /// cref="MovimentacaoRelatavel.ResolverOcorrencias"/>.</summary>
    public string? CodigoOcorrencia { get; init; }
    public string? DescricaoOcorrencia { get; init; }
    public string? CodigoAutenticacao { get; init; }

    /// <summary>Nº do documento atribuído pela empresa (G064, "Seu
    /// Número").</summary>
    public string? IdentificadorExterno { get; init; }

    /// <summary>
    /// Linhas CNAB da **remessa original** deste pagamento, como o cliente
    /// as enviou. É a fonte de verdade dos valores que voltam no retorno:
    /// remontar a partir das colunas <c>Info</c> arrisca devolver um dado
    /// normalizado diferente do que entrou. Ver
    /// <c>Json.LinhasRemessa</c>, que faz o parse posicional.
    ///
    /// Anulável: pagamentos originados por API (e não por arquivo) não têm
    /// linha nenhuma — nesse caso o segmento é montado pelas colunas.
    /// </summary>
    public string? Linhas { get; init; }

    public decimal ValorPagamento { get; init; }
    public DateTime? DataTransacao { get; init; }
    public string? Observacao { get; init; }

    // --- Transferências (Tef, Ted, Pix) → Segmento A ---
    public string? FavorecidoBanco { get; init; }
    public string? FavorecidoAgencia { get; init; }
    public string? FavorecidoConta { get; init; }
    public string? FavorecidoTipoConta { get; init; }
    public string? FavorecidoNome { get; init; }
    public string? FavorecidoDocumento { get; init; }
    public short? FavorecidoTipoDocumento { get; init; }
    public string? DebitoAgencia { get; init; }
    public string? DebitoConta { get; init; }
    public string? DebitoNome { get; init; }
    /// <summary>Só PIX: preenchida indica QR-Code (forma 47) em vez de
    /// transferência por dados bancários (45).</summary>
    public string? ChavePixUrl { get; init; }

    // --- Títulos (Boleto, Tricon) → Segmento J ---
    public string? CodigoBanco { get; init; }
    public string? CodigoBarra { get; init; }
    public string? LinhaDigitavel { get; init; }
    public string? NossoNumero { get; init; }
    public DateOnly? DataVencimento { get; init; }
    public decimal? ValorNominal { get; init; }
    public decimal? ValorAbatimento { get; init; }
    public string? BeneficiarioNome { get; init; }
    public string? BeneficiarioDocumento { get; init; }
    public short? BeneficiarioTipoDocumento { get; init; }
}
