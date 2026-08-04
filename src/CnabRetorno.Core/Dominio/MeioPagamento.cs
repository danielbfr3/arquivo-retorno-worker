namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Meio de pagamento — valores idênticos aos de <c>Pagamento.TipoTransacao</c>
/// na base ASA_CASH_PAGAMENTO (extração de 03/08/2026). O valor <c>1 =
/// Arquivo</c> daquela tabela não aparece aqui: não é um meio de pagamento,
/// é o canal de entrada.
///
/// Cada meio vira uma **Forma de Lançamento** diferente no CNAB240
/// (campo G029, header de lote posições 12-13) e, por consequência, um
/// **lote separado** dentro do arquivo de retorno — o header de lote só
/// comporta uma forma de lançamento. Ver <see cref="FormaLancamento"/>.
/// </summary>
public enum MeioPagamento : short
{
    Tef = 2,
    Pix = 3,
    Boleto = 4,
    Tricon = 5,
    Ted = 6,
}

/// <summary>
/// Códigos do domínio G029 (Forma de Lançamento) do layout FEBRABAN 240
/// V10.11 §4-G, e o segmento de detalhe correspondente.
/// </summary>
public static class FormaLancamento
{
    public const string CreditoContaCorrente = "01"; // TEF — crédito em conta no próprio banco
    public const string TedOutraTitularidade = "41";
    public const string PixTransferencia = "45";
    public const string PixQrCode = "47";
    public const string LiquidacaoTituloProprioBanco = "30";
    public const string PagamentoTituloOutrosBancos = "31";

    /// <summary>
    /// Forma de lançamento de cada meio.
    ///
    /// TODO(a-confirmar): três escolhas aqui são decisão de negócio, não
    /// dedução do layout:
    /// <list type="bullet">
    ///   <item>TED: <c>41</c> (outra titularidade) x <c>43</c> (mesma) —
    ///   o layout diz que dá pra derivar do tipo de inscrição do
    ///   favorecido (nota (1) do G029), mas o banco pode exigir um valor
    ///   fixo por contrato.</item>
    ///   <item>PIX: <c>45</c> (transferência) x <c>47</c> (QR-Code) —
    ///   resolvido em runtime por <see cref="DePagamentoPix"/> a partir de
    ///   <c>PixInfo.ChavePixUrl</c>.</item>
    ///   <item>Boleto/Tricon: <c>30</c> (título do próprio banco) x
    ///   <c>31</c> (outros bancos) — resolvido por <see
    ///   cref="DeTitulo"/> comparando o banco do código de barras com o
    ///   banco do ASA.</item>
    /// </list>
    /// </summary>
    public static string De(MeioPagamento meio) => meio switch
    {
        MeioPagamento.Tef => CreditoContaCorrente,
        MeioPagamento.Ted => TedOutraTitularidade,
        MeioPagamento.Pix => PixTransferencia,
        MeioPagamento.Boleto => PagamentoTituloOutrosBancos,
        MeioPagamento.Tricon => PagamentoTituloOutrosBancos,
        _ => throw new ArgumentOutOfRangeException(nameof(meio), meio, "Meio de pagamento sem forma de lançamento mapeada."),
    };

    /// <summary>PIX com chave/URL preenchida é QR-Code (47); sem ela é
    /// transferência por dados bancários (45).</summary>
    public static string DePagamentoPix(string? chavePixUrl)
        => string.IsNullOrWhiteSpace(chavePixUrl) ? PixTransferencia : PixQrCode;

    /// <summary>Título cujo banco emissor é o próprio ASA liquida por
    /// <c>30</c>; qualquer outro banco é <c>31</c>.</summary>
    public static string DeTitulo(string? codigoBancoTitulo, string codigoBancoAsa)
        => string.Equals(codigoBancoTitulo?.TrimStart('0'), codigoBancoAsa.TrimStart('0'), StringComparison.Ordinal)
            ? LiquidacaoTituloProprioBanco
            : PagamentoTituloOutrosBancos;

    /// <summary>Segmento de detalhe exigido pela forma de lançamento —
    /// 'A' pras transferências, 'J' pros títulos (layout §2.1, tabela de
    /// segmentos por lote de serviço).</summary>
    public static char SegmentoDe(string formaLancamento) => formaLancamento switch
    {
        LiquidacaoTituloProprioBanco or PagamentoTituloOutrosBancos or PixQrCode => 'J',
        _ => 'A',
    };
}
