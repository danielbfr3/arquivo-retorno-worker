using System.Globalization;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Cnab240;
using CnabRetorno.Core.Dominio;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;
using Microsoft.Extensions.Options;

namespace CnabRetorno.PagamentoRetorno.Worker.Json;

public class RetornoOptions
{
    public const string Secao = "Retorno";

    /// <summary>Código do banco na compensação (G001).
    /// TODO(a-confirmar): não informado no material.</summary>
    public string CodigoBanco { get; set; } = "TODO";

    public string NomeBanco { get; set; } = "TODO-confirmar-nome-banco";

    /// <summary>Tipo de Serviço do lote (G025, posições 10-11).
    /// TODO(a-confirmar): '98' = Pagamentos Diversos é o guarda-chuva do
    /// domínio; '20' = Pagamento Fornecedor é o mais usado quando o
    /// contrato é específico. A escolha é de negócio, não do layout.</summary>
    public string TipoServico { get; set; } = "98";

    /// <summary>Versão do layout do arquivo (G019, posições 164-166).</summary>
    public string VersaoLayoutArquivo { get; set; } = "103";

    /// <summary>Template do nome do arquivo gerado. Mesmos tokens do
    /// robô de remessa, mais <c>{tipo}</c> (PARCIAL/CONSOLIDADO).</summary>
    public string TemplateNome { get; set; } = "RetornoPagamento_{documento}_{data:ddMMyyyy}_{data:HHmmss}_{tipo}.ret";
}

/// <summary>
/// Monta o JSON do retorno de pagamentos a partir das movimentações de um
/// cliente.
///
/// A estrutura segue o layout FEBRABAN 240 V10.11: um arquivo, N lotes
/// (um por Forma de Lançamento presente — o header de lote só comporta
/// uma), e dentro de cada lote os registros de detalhe no segmento que
/// aquela forma exige ('A' pras transferências, 'J' pros títulos).
///
/// Lógica pura: recebe dados, devolve DTO. Sem banco, sem HTTP, sem
/// relógio próprio — o instante vem por parâmetro, o que deixa a
/// montagem inteira testável.
/// </summary>
public class MontagemRetornoPagamento(IOptions<RetornoOptions> opcoes)
{
    private readonly RetornoOptions _opt = opcoes.Value;

    /// <summary>Cada pagamento ocupa dois registros de detalhe no lote:
    /// A+B nas transferências (o B carrega tipo/número de inscrição do
    /// favorecido) e J+J-52 nos títulos (o J-52 carrega a identificação do
    /// beneficiário). Por isso a numeração anda de dois em dois.</summary>
    private const int RegistrosPorPagamento = 2;

    public RetornoPagamentoJson Montar(
        MovimentacoesDoCliente cliente, long sequencial, DateTimeOffset momento)
    {
        var empresa = MontarEmpresa(cliente);
        var conta = MontarConta(cliente);

        var lotes = cliente.Movimentacoes
            .GroupBy(ResolverFormaLancamento)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select((grupo, indice) => MontarLote(grupo.Key, [.. grupo], indice + 1, empresa, conta))
            .ToList();

        return new RetornoPagamentoJson
        {
            Arquivo = new ArquivoPagamento
            {
                Banco = _opt.CodigoBanco,
                NomeBanco = _opt.NomeBanco,
                CodigoRemessaRetorno = Cnab240Pagamento.CodigoRemessaRetorno.Retorno,
                DataGeracao = momento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HoraGeracao = momento.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                NumeroSequencialArquivo = (int)sequencial,
                VersaoLayout = _opt.VersaoLayoutArquivo,
                Empresa = empresa,
                Conta = conta,
            },
            Lotes = lotes,
            Totais = new TotaisArquivoPagamento
            {
                QuantidadeLotes = lotes.Count,
                // Header e trailer de arquivo entram na conta (G056: soma
                // dos tipos 0, 1, 3, 5 e 9), e cada lote já contabiliza o
                // próprio header e trailer.
                QuantidadeRegistros = 2 + lotes.Sum(l => l.Totais.QuantidadeRegistros),
                ValorTotal = lotes.Sum(l => l.Totais.ValorTotal),
            },
        };
    }

    public string MontarNomeArquivo(string documento, TipoJanelaNome tipo, DateTimeOffset momento)
        => _opt.TemplateNome
            .Replace("{documento}", documento, StringComparison.Ordinal)
            .Replace("{tipo}", tipo.ToString().ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{data:ddMMyyyy}", momento.ToString("ddMMyyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{data:HHmmss}", momento.ToString("HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private string ResolverFormaLancamento(MovimentacaoPagamento m) => (MeioPagamento)m.Meio switch
    {
        MeioPagamento.Pix => FormaLancamento.DePagamentoPix(m.ChavePixUrl),
        MeioPagamento.Boleto or MeioPagamento.Tricon => FormaLancamento.DeTitulo(m.CodigoBanco, _opt.CodigoBanco),
        var meio => FormaLancamento.De(meio),
    };

    private LotePagamento MontarLote(
        string formaLancamento,
        IReadOnlyList<MovimentacaoPagamento> movimentacoes,
        int numeroLote,
        EmpresaPagamento empresa,
        ContaPagamento conta)
    {
        var segmento = FormaLancamento.SegmentoDe(formaLancamento);

        var pagamentos = movimentacoes
            .Select((m, i) => MontarDetalhe(m, segmento, numeroRegistro: 1 + i * RegistrosPorPagamento))
            .ToList();

        return new LotePagamento
        {
            Numero = numeroLote,
            TipoOperacao = "C", // G028 — lançamento a crédito
            TipoServico = _opt.TipoServico,
            FormaLancamento = formaLancamento,
            VersaoLayout = segmento == 'J'
                ? Cnab240Pagamento.VersaoLayoutLote.SegmentoJ
                : Cnab240Pagamento.VersaoLayoutLote.SegmentoA,
            Empresa = empresa,
            Conta = conta,
            Pagamentos = pagamentos,
            Totais = new TotaisLotePagamento
            {
                // G057: soma dos registros do lote, header e trailer
                // inclusos.
                QuantidadeRegistros = 2 + pagamentos.Count * RegistrosPorPagamento,
                ValorTotal = pagamentos.Sum(p => p.Credito?.ValorPagamento ?? p.Titulo?.ValorPagamento ?? 0m),
            },
        };
    }

    private static DetalhePagamento MontarDetalhe(
        MovimentacaoPagamento m, char segmento, int numeroRegistro)
    {
        var remessa = SegmentosRemessa.Analisar(m.Linhas);
        var ocorrencias = MovimentacaoRelatavel.ResolverOcorrencias(m.CodigoOcorrencia, m.CodigoStatus);

        var comum = new DetalhePagamento
        {
            Segmento = segmento.ToString(),
            NumeroRegistro = numeroRegistro,
            TipoMovimento = Cnab240Pagamento.TipoMovimento.Inclusao,
            CodigoInstrucao = Cnab240Pagamento.CodigoInstrucao.InclusaoRegistroLiberado,
            Ocorrencias = ocorrencias,
            DescricaoOcorrencia = m.DescricaoOcorrencia,
            SeuNumero = m.IdentificadorExterno,
            NossoNumero = m.NossoNumero ?? m.CodigoAutenticacao,
        };

        if (segmento != 'J')
            return comum with
            {
                Favorecido = MontarFavorecido(m, remessa),
                Credito = MontarCredito(m, remessa),
            };

        // PIX QR-Code cai no lote de segmento J (forma 47), mas a
        // identidade dele não é código de barras — é a chave/URL do
        // J-52 PIX. Sem o Favorecido aqui, a chave se perderia e o
        // retorno sairia sem dizer PRA QUEM o pagamento foi.
        var favorecidoPix = (MeioPagamento)m.Meio == MeioPagamento.Pix
            ? MontarFavorecidoPix(m, remessa)
            : null;

        return comum with
        {
            Titulo = MontarTitulo(m, remessa),
            Favorecido = favorecidoPix,
        };
    }

    /// <summary>Identidade do favorecido de um PIX QR-Code. A chave vem
    /// do J-52 PIX da remessa (posições 132-210) quando gravado — nunca
    /// do J-52 comum de boleto, cujas mesmas posições são o pagador
    /// final —, senão da coluna <c>ChavePixUrl</c>.</summary>
    private static FavorecidoPagamento MontarFavorecidoPix(MovimentacaoPagamento m, SegmentosRemessa remessa)
    {
        var chaveDaRemessa = remessa.J52 is not null
            ? Cnab240Pagamento.SegmentoJ52.ChavePix(remessa.J52)
            : null;

        return new FavorecidoPagamento
        {
            Nome = m.FavorecidoNome,
            TipoInscricao = m.FavorecidoTipoDocumento?.ToString(),
            NumeroInscricao = m.FavorecidoDocumento,
            ChavePix = string.IsNullOrWhiteSpace(chaveDaRemessa) ? m.ChavePixUrl : chaveDaRemessa,
        };
    }

    private static FavorecidoPagamento MontarFavorecido(MovimentacaoPagamento m, SegmentosRemessa remessa)
    {
        // Preferência pela linha da remessa quando ela existe — ver
        // SegmentosRemessa sobre por quê.
        var a = remessa.A;
        var b = remessa.B;

        return new FavorecidoPagamento
        {
            Camara = a is null ? null : Cnab240Pagamento.SegmentoA.FavorecidoCamara(a),
            Banco = a is null ? m.FavorecidoBanco : Cnab240Pagamento.SegmentoA.FavorecidoBanco(a),
            Agencia = a is null ? m.FavorecidoAgencia : Cnab240Pagamento.SegmentoA.FavorecidoAgencia(a),
            DvAgencia = a is null ? null : Cnab240Pagamento.SegmentoA.FavorecidoDvAgencia(a),
            Conta = a is null ? m.FavorecidoConta : Cnab240Pagamento.SegmentoA.FavorecidoConta(a),
            DvConta = a is null ? null : Cnab240Pagamento.SegmentoA.FavorecidoDvConta(a),
            TipoConta = m.FavorecidoTipoConta,
            Nome = a is null ? m.FavorecidoNome : Cnab240Pagamento.SegmentoA.FavorecidoNome(a),
            TipoInscricao = b is null ? m.FavorecidoTipoDocumento?.ToString() : Cnab240Pagamento.SegmentoB.TipoInscricao(b),
            NumeroInscricao = b is null ? m.FavorecidoDocumento : Cnab240Pagamento.SegmentoB.NumeroInscricao(b),
            ChavePix = m.ChavePixUrl,
        };
    }

    private static CreditoPagamento MontarCredito(MovimentacaoPagamento m, SegmentosRemessa remessa)
    {
        var a = remessa.A;
        var desfecho = m.DataAtualizacao ?? m.DataCriacao;

        // Data/valor "real" (P003/P004) só existem no retorno: é o que de
        // fato aconteceu, e não o que foi agendado. Num pagamento que não
        // se efetivou (rejeitado, cancelado, erro) o layout pede zeros —
        // preencher com o valor agendado faria o cliente conciliar uma
        // baixa que não houve.
        var efetivado = m.CodigoStatus == (short)StatusPagamento.Finalizado;

        return new CreditoPagamento
        {
            DataPagamento = a is null
                ? (m.DataTransacao ?? desfecho).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : FormatarDataCnab(Cnab240Pagamento.SegmentoA.DataPagamento(a)),
            TipoMoeda = a is null ? "BRL" : Cnab240Pagamento.SegmentoA.TipoMoeda(a),
            ValorPagamento = a is null ? m.ValorPagamento : Cnab240Pagamento.SegmentoA.ValorPagamento(a),
            DataRealEfetivacao = efetivado ? desfecho.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            ValorRealEfetivacao = efetivado ? m.ValorPagamento : 0m,
            Informacao2 = a is null ? m.Observacao : Cnab240Pagamento.SegmentoA.Informacao2(a),
        };
    }

    private static TituloPagamento MontarTitulo(MovimentacaoPagamento m, SegmentosRemessa remessa)
    {
        var j = remessa.J;
        var j52 = remessa.J52;
        var desfecho = m.DataAtualizacao ?? m.DataCriacao;

        // Mesma regra do segmento A (P003/P004): data e valor de
        // pagamento só existem se o pagamento aconteceu. Num rejeitado,
        // preenchê-los faria o cliente conciliar uma baixa que não houve.
        var efetivado = m.CodigoStatus == (short)StatusPagamento.Finalizado;

        return new TituloPagamento
        {
            CodigoBarras = j is null ? m.CodigoBarra : Cnab240Pagamento.SegmentoJ.CodigoBarras(j),
            NomeBeneficiario = j is null ? m.BeneficiarioNome : Cnab240Pagamento.SegmentoJ.NomeBeneficiario(j),
            TipoInscricaoBeneficiario = j52 is null
                ? m.BeneficiarioTipoDocumento?.ToString()
                : Cnab240Pagamento.SegmentoJ52.BeneficiarioTipoInscricao(j52),
            NumeroInscricaoBeneficiario = j52 is null
                ? m.BeneficiarioDocumento
                : Cnab240Pagamento.SegmentoJ52.BeneficiarioNumeroInscricao(j52),
            DataVencimento = j is null
                ? m.DataVencimento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : FormatarDataCnab(Cnab240Pagamento.SegmentoJ.DataVencimento(j)),
            ValorTitulo = j is null ? m.ValorNominal ?? 0m : Cnab240Pagamento.SegmentoJ.ValorTitulo(j),
            ValorDesconto = j is null ? m.ValorAbatimento ?? 0m : Cnab240Pagamento.SegmentoJ.ValorDesconto(j),
            ValorAcrescimos = j is null ? 0m : Cnab240Pagamento.SegmentoJ.ValorAcrescimos(j),
            DataPagamento = efetivado ? desfecho.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            ValorPagamento = efetivado ? m.ValorPagamento : 0m,
            CodigoMoeda = j is null ? "09" : Cnab240Pagamento.SegmentoJ.CodigoMoeda(j), // 09 = Real
        };
    }

    private EmpresaPagamento MontarEmpresa(MovimentacoesDoCliente cliente) => new()
    {
        TipoInscricao = cliente.TipoDocumento.ToString(CultureInfo.InvariantCulture),
        NumeroInscricao = cliente.Documento,
        // DebitoNome é o nome do titular da conta debitada, ou seja, o
        // próprio cliente. Só as transferências o têm; num dia só de
        // boletos fica nulo e o conversor usa o cadastro.
        Nome = cliente.Movimentacoes
            .Select(m => m.DebitoNome)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
    };

    private static ContaPagamento MontarConta(MovimentacoesDoCliente cliente)
    {
        var comDebito = cliente.Movimentacoes.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.DebitoConta));

        return new ContaPagamento
        {
            Agencia = comDebito?.DebitoAgencia,
            Conta = comDebito?.DebitoConta ?? cliente.ContaHeader,
        };
    }

    /// <summary>Converte DDMMAAAA (formato posicional do CNAB) em
    /// AAAA-MM-DD. Devolve <c>null</c> pra campo zerado ou malformado —
    /// data inválida é pior que data ausente.</summary>
    private static string? FormatarDataCnab(string ddmmaaaa)
        => DateTime.TryParseExact(ddmmaaaa, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
}

/// <summary>Sufixo do nome do arquivo — espelha
/// <c>Agendamento.TipoJanela</c> sem acoplar a montagem ao agendador.</summary>
public enum TipoJanelaNome
{
    Parcial,
    Consolidado,
}
