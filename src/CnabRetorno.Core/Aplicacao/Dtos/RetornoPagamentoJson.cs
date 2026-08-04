namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// JSON de entrada do conversor pro retorno de pagamentos — o corpo que o
/// Robô 2 envia pra <c>POST /v1/convert/sync/upload</c> e recebe de volta
/// como CNAB240.
///
/// A forma é ditada pelo layout FEBRABAN 240 V10.11 (docs/Layout padrao
/// CNAB240 V 10 11 - 21_08_2023-2.pdf, §2.2 e §3.1). A diferença
/// estrutural em relação ao JSON de cobrança é a lista de <see
/// cref="Lotes"/>: o header de lote carrega **uma única** Forma de
/// Lançamento (posições 12-13), então um arquivo de retorno de pagamentos
/// tem um lote por meio de pagamento presente, não um lote só.
///
/// TODO(a-confirmar): o nome do pipeline de conversão de pagamentos não
/// foi fornecido, e portanto **este shape é uma proposta** derivada do
/// layout, não um contrato observado — diferente do JSON de cobrança, que
/// foi modelado 1:1 a partir de resposta real. Validar contra o conversor
/// antes de qualquer teste de ponta a ponta.
/// Serializar com <c>JsonNamingPolicy.CamelCase</c>.
/// </summary>
public sealed record RetornoPagamentoJson
{
    public required ArquivoPagamento Arquivo { get; init; }
    public required IReadOnlyList<LotePagamento> Lotes { get; init; }
    public required TotaisArquivoPagamento Totais { get; init; }
}

/// <summary>Header (tipo '0') + trailer (tipo '9') de arquivo — layout §2.2.</summary>
public sealed record ArquivoPagamento
{
    public string? Banco { get; init; }
    public string? NomeBanco { get; init; }

    /// <summary>G015, header posição 143 — é aqui que o arquivo se declara
    /// retorno ('2'), não no header de lote.</summary>
    public string? CodigoRemessaRetorno { get; init; }

    public string? DataGeracao { get; init; }
    public string? HoraGeracao { get; init; }

    /// <summary>NSA (G018, posições 158-163) — reservado atomicamente em
    /// <c>Pagamento.Parametro.SequencialAtual</c>, nunca reaproveitado do
    /// arquivo de remessa.</summary>
    public int NumeroSequencialArquivo { get; init; }

    public string? VersaoLayout { get; init; }
    public int? Densidade { get; init; }
    public string? CodigoConvenio { get; init; }
    public required EmpresaPagamento Empresa { get; init; }
    public required ContaPagamento Conta { get; init; }
}

/// <summary>Header (tipo '1') + trailer (tipo '5') de lote, mais os
/// detalhes — layout §3.1.2 (segmento A) e §3.1.3 (segmento J).</summary>
public sealed record LotePagamento
{
    public int Numero { get; init; }

    /// <summary>G028, posição 9 — 'C' lançamento a crédito.</summary>
    public string? TipoOperacao { get; init; }

    /// <summary>G025, posições 10-11.</summary>
    public string? TipoServico { get; init; }

    /// <summary>G029, posições 12-13 — define o segmento de detalhe do
    /// lote inteiro ('A' ou 'J').</summary>
    public required string FormaLancamento { get; init; }

    /// <summary>G030, posições 14-16 — '046' pro lote de segmento A,
    /// '040' pro de segmento J (defaults do layout).</summary>
    public string? VersaoLayout { get; init; }

    public required EmpresaPagamento Empresa { get; init; }
    public required ContaPagamento Conta { get; init; }
    public required IReadOnlyList<DetalhePagamento> Pagamentos { get; init; }
    public required TotaisLotePagamento Totais { get; init; }

    /// <summary>G059, trailer de lote posições 231-240.</summary>
    public string? Ocorrencias { get; init; }
}

public sealed record EmpresaPagamento
{
    public string? TipoInscricao { get; init; }
    public string? NumeroInscricao { get; init; }
    public string? Nome { get; init; }
}

public sealed record ContaPagamento
{
    public string? Agencia { get; init; }
    public string? DvAgencia { get; init; }
    public string? Conta { get; init; }
    public string? DvConta { get; init; }
    public string? DvAgenciaConta { get; init; }
}

/// <summary>
/// Um registro de detalhe (tipo '3'). <see cref="Segmento"/> diz qual
/// bloco de propriedades está preenchido: 'A' usa <see cref="Favorecido"/>
/// e <see cref="Credito"/>; 'J' usa <see cref="Titulo"/>.
/// </summary>
public sealed record DetalhePagamento
{
    public required string Segmento { get; init; }

    /// <summary>G038, posições 9-13 — sequencial **dentro do lote**,
    /// reiniciado a cada lote.</summary>
    public int NumeroRegistro { get; init; }

    /// <summary>G060, posição 15 — '0' inclusão, '3' estorno (só retorno).</summary>
    public string? TipoMovimento { get; init; }

    /// <summary>G061, posições 16-17.</summary>
    public string? CodigoInstrucao { get; init; }

    /// <summary>G064, "Seu Número" — <c>IdentificadorExterno</c> da
    /// movimentação.</summary>
    public string? SeuNumero { get; init; }

    /// <summary>G043, "Nosso Número" — atribuído pelo banco.</summary>
    public string? NossoNumero { get; init; }

    /// <summary>G059, posições 231-240 — o desfecho do pagamento.</summary>
    public required string Ocorrencias { get; init; }
    public string? DescricaoOcorrencia { get; init; }

    public FavorecidoPagamento? Favorecido { get; init; }
    public CreditoPagamento? Credito { get; init; }
    public TituloPagamento? Titulo { get; init; }
}

/// <summary>Segmento A posições 18-73 + segmento B (tipo/nº de inscrição).</summary>
public sealed record FavorecidoPagamento
{
    public string? Camara { get; init; }
    public string? Banco { get; init; }
    public string? Agencia { get; init; }
    public string? DvAgencia { get; init; }
    public string? Conta { get; init; }
    public string? DvConta { get; init; }
    public string? TipoConta { get; init; }
    public string? Nome { get; init; }
    public string? TipoInscricao { get; init; }
    public string? NumeroInscricao { get; init; }
    /// <summary>Chave/URL Pix — segmento J-52 PIX posições 132-210.</summary>
    public string? ChavePix { get; init; }
}

/// <summary>Segmento A posições 94-177 — o que foi mandado pagar e o que
/// de fato foi pago.</summary>
public sealed record CreditoPagamento
{
    public string? DataPagamento { get; init; }
    public string? TipoMoeda { get; init; }
    public decimal ValorPagamento { get; init; }
    /// <summary>P003, posições 155-162 — só existe no retorno.</summary>
    public string? DataRealEfetivacao { get; init; }
    /// <summary>P004, posições 163-177 — só existe no retorno.</summary>
    public decimal ValorRealEfetivacao { get; init; }
    public string? Informacao2 { get; init; }
}

/// <summary>Segmento J posições 18-224.</summary>
public sealed record TituloPagamento
{
    public string? CodigoBarras { get; init; }
    public string? NomeBeneficiario { get; init; }
    public string? TipoInscricaoBeneficiario { get; init; }
    public string? NumeroInscricaoBeneficiario { get; init; }
    public string? DataVencimento { get; init; }
    public decimal ValorTitulo { get; init; }
    public decimal ValorDesconto { get; init; }
    public decimal ValorAcrescimos { get; init; }
    public string? DataPagamento { get; init; }
    public decimal ValorPagamento { get; init; }
    public string? CodigoMoeda { get; init; }
}

/// <summary>Trailer de lote (tipo '5') posições 18-59.</summary>
public sealed record TotaisLotePagamento
{
    /// <summary>G057 — soma dos registros tipo 1, 2, 3, 4 e 5 do lote
    /// (header e trailer inclusos).</summary>
    public int QuantidadeRegistros { get; init; }

    /// <summary>P007 (segmento A) / L001 (segmento J).</summary>
    public decimal ValorTotal { get; init; }
}

/// <summary>Trailer de arquivo (tipo '9') posições 18-35.</summary>
public sealed record TotaisArquivoPagamento
{
    /// <summary>G049.</summary>
    public int QuantidadeLotes { get; init; }

    /// <summary>G056 — soma dos registros tipo 0, 1, 3, 5 e 9.</summary>
    public int QuantidadeRegistros { get; init; }

    public decimal ValorTotal { get; init; }
}
