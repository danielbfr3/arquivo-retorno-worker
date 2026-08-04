using CnabRetorno.Core.Dominio;
using CnabRetorno.PagamentoRetorno.Worker.Json;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.PagamentoRetorno;

public class MontagemRetornoPagamentoTests
{
    private const string BancoAsa = "999";
    private const string DocumentoCliente = "12345678000199";

    private static readonly DateTimeOffset Momento = new(2026, 8, 3, 18, 0, 0, TimeSpan.FromHours(-3));

    private static MontagemRetornoPagamento Montagem() => new(Options.Create(new RetornoOptions
    {
        CodigoBanco = BancoAsa,
        NomeBanco = "BANCO TESTE",
        TipoServico = "98",
        VersaoLayoutArquivo = "103",
    }));

    private static MovimentacaoPagamento Movimentacao(
        MeioPagamento meio = MeioPagamento.Tef,
        short status = (short)StatusPagamento.Finalizado,
        string? codigoOcorrencia = null,
        string? linhas = null,
        string? chavePixUrl = null,
        string? codigoBanco = null,
        decimal valor = 150.75m,
        bool comDadosDeDebito = true) => new()
        {
            Meio = (short)meio,
            PagamentoID = Guid.NewGuid(),
            CodigoStatus = status,
            ClienteDocumento = DocumentoCliente,
            ClienteTipoDocumento = 2,
            ClienteContaHeader = "0009000009",
            DataCriacao = new DateTime(2026, 8, 3, 8, 0, 0),
            DataAtualizacao = new DateTime(2026, 8, 3, 9, 30, 0),
            CodigoOcorrencia = codigoOcorrencia,
            IdentificadorExterno = "PED-0001",
            Linhas = linhas,
            ValorPagamento = valor,
            ChavePixUrl = chavePixUrl,
            CodigoBanco = codigoBanco,
            FavorecidoNome = "FAVORECIDO EXEMPLO LTDA",
            FavorecidoBanco = "001",
            FavorecidoAgencia = "00001",
            FavorecidoConta = "000000012345",
            FavorecidoDocumento = "98765432000188",
            FavorecidoTipoDocumento = 2,
            DebitoAgencia = comDadosDeDebito ? "00099" : null,
            DebitoConta = comDadosDeDebito ? "000000099999" : null,
            DebitoNome = comDadosDeDebito ? "CLIENTE EXEMPLO LTDA" : null,
            BeneficiarioNome = "BENEFICIARIO EXEMPLO",
            BeneficiarioDocumento = "11122233000144",
            BeneficiarioTipoDocumento = 2,
            CodigoBarra = "99991234500000015075000000000000000000000001",
            NossoNumero = "00000000000000000001",
            DataVencimento = new DateOnly(2026, 8, 10),
            ValorNominal = 150.75m,
            ValorAbatimento = 0m,
        };

    private static MovimentacoesDoCliente Cliente(params MovimentacaoPagamento[] movimentacoes)
        => new(DocumentoCliente, 2, "0009000009", movimentacoes);

    [Fact]
    public void Deve_gerar_um_lote_por_forma_de_lancamento()
    {
        // O header de lote comporta uma única forma de lançamento — TEF,
        // TED e boleto não podem dividir lote.
        var json = Montagem().Montar(
            Cliente(
                Movimentacao(MeioPagamento.Tef),
                Movimentacao(MeioPagamento.Ted),
                Movimentacao(MeioPagamento.Boleto, codigoBanco: "001")),
            sequencial: 7, Momento);

        Assert.Equal(3, json.Lotes.Count);
        Assert.Equal(
            [FormaLancamento.CreditoContaCorrente, FormaLancamento.PagamentoTituloOutrosBancos, FormaLancamento.TedOutraTitularidade],
            json.Lotes.Select(l => l.FormaLancamento));
    }

    [Fact]
    public void Movimentacoes_do_mesmo_meio_devem_dividir_o_lote()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef), Movimentacao(MeioPagamento.Tef)),
            sequencial: 1, Momento);

        Assert.Single(json.Lotes);
        Assert.Equal(2, json.Lotes[0].Pagamentos.Count);
    }

    [Fact]
    public void Lote_de_transferencia_deve_usar_segmento_A_e_o_de_titulo_segmento_J()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef), Movimentacao(MeioPagamento.Boleto, codigoBanco: "001")),
            sequencial: 1, Momento);

        var transferencia = json.Lotes.Single(l => l.FormaLancamento == FormaLancamento.CreditoContaCorrente);
        var titulo = json.Lotes.Single(l => l.FormaLancamento == FormaLancamento.PagamentoTituloOutrosBancos);

        Assert.All(transferencia.Pagamentos, p => Assert.Equal("A", p.Segmento));
        Assert.All(transferencia.Pagamentos, p => Assert.NotNull(p.Favorecido));
        Assert.All(titulo.Pagamentos, p => Assert.Equal("J", p.Segmento));
        Assert.All(titulo.Pagamentos, p => Assert.NotNull(p.Titulo));
    }

    [Fact]
    public void Pix_com_chave_deve_ser_qrcode_e_sem_chave_transferencia()
    {
        var json = Montagem().Montar(
            Cliente(
                Movimentacao(MeioPagamento.Pix),
                Movimentacao(MeioPagamento.Pix, chavePixUrl: "chave@exemplo.com")),
            sequencial: 1, Momento);

        Assert.Contains(json.Lotes, l => l.FormaLancamento == FormaLancamento.PixTransferencia);
        Assert.Contains(json.Lotes, l => l.FormaLancamento == FormaLancamento.PixQrCode);
    }

    [Fact]
    public void Titulo_do_proprio_banco_deve_usar_forma_30()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Boleto, codigoBanco: BancoAsa)),
            sequencial: 1, Momento);

        Assert.Equal(FormaLancamento.LiquidacaoTituloProprioBanco, json.Lotes[0].FormaLancamento);
    }

    [Fact]
    public void Numeracao_dos_registros_deve_reiniciar_a_cada_lote_e_andar_de_dois_em_dois()
    {
        // Cada pagamento ocupa dois registros (A+B ou J+J-52), e o
        // sequencial do G038 é por lote, não por arquivo.
        var json = Montagem().Montar(
            Cliente(
                Movimentacao(MeioPagamento.Tef),
                Movimentacao(MeioPagamento.Tef),
                Movimentacao(MeioPagamento.Ted)),
            sequencial: 1, Momento);

        var tef = json.Lotes.Single(l => l.FormaLancamento == FormaLancamento.CreditoContaCorrente);
        var ted = json.Lotes.Single(l => l.FormaLancamento == FormaLancamento.TedOutraTitularidade);

        Assert.Equal([1, 3], tef.Pagamentos.Select(p => p.NumeroRegistro));
        Assert.Equal([1], ted.Pagamentos.Select(p => p.NumeroRegistro));
    }

    [Fact]
    public void Totais_do_lote_devem_contar_header_e_trailer()
    {
        // G057 soma os registros tipo 1, 2, 3, 4 e 5 — header e trailer
        // entram. Dois pagamentos = 2 + 2*2 = 6.
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef, valor: 10m), Movimentacao(MeioPagamento.Tef, valor: 5m)),
            sequencial: 1, Momento);

        Assert.Equal(6, json.Lotes[0].Totais.QuantidadeRegistros);
        Assert.Equal(15m, json.Lotes[0].Totais.ValorTotal);
    }

    [Fact]
    public void Totais_do_arquivo_devem_somar_os_lotes_mais_header_e_trailer()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef), Movimentacao(MeioPagamento.Ted)),
            sequencial: 1, Momento);

        Assert.Equal(2, json.Totais.QuantidadeLotes);
        Assert.Equal(2 + json.Lotes.Sum(l => l.Totais.QuantidadeRegistros), json.Totais.QuantidadeRegistros);
    }

    [Fact]
    public void Sequencial_reservado_deve_ir_pro_header_do_arquivo()
    {
        var json = Montagem().Montar(Cliente(Movimentacao()), sequencial: 42, Momento);

        Assert.Equal(42, json.Arquivo.NumeroSequencialArquivo);
    }

    [Fact]
    public void Arquivo_deve_se_declarar_retorno()
    {
        var json = Montagem().Montar(Cliente(Movimentacao()), sequencial: 1, Momento);

        Assert.Equal("2", json.Arquivo.CodigoRemessaRetorno); // G015
    }

    [Fact]
    public void Ocorrencia_gravada_deve_prevalecer_sobre_o_mapeamento_por_status()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(status: (short)StatusPagamento.Rejeitado, codigoOcorrencia: "AG")),
            sequencial: 1, Momento);

        Assert.Equal("AG".PadRight(10), json.Lotes[0].Pagamentos[0].Ocorrencias);
    }

    [Theory]
    [InlineData(StatusPagamento.Finalizado, "00")]
    [InlineData(StatusPagamento.Cancelado, "02")]
    public void Sem_ocorrencia_gravada_deve_derivar_do_status(StatusPagamento status, string esperado)
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(status: (short)status)), sequencial: 1, Momento);

        Assert.Equal(esperado.PadRight(10), json.Lotes[0].Pagamentos[0].Ocorrencias);
    }

    [Fact]
    public void Pagamento_nao_efetivado_nao_pode_ter_valor_real()
    {
        // P003/P004 dizem o que de fato aconteceu. Preencher num
        // rejeitado faria o cliente conciliar uma baixa que não houve.
        var json = Montagem().Montar(
            Cliente(Movimentacao(status: (short)StatusPagamento.Rejeitado, valor: 200m)),
            sequencial: 1, Momento);

        var credito = json.Lotes[0].Pagamentos[0].Credito;
        Assert.NotNull(credito);
        Assert.Null(credito.DataRealEfetivacao);
        Assert.Equal(0m, credito.ValorRealEfetivacao);
        Assert.Equal(200m, credito.ValorPagamento); // o agendado continua lá
    }

    [Fact]
    public void Pagamento_efetivado_deve_ter_valor_real()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(status: (short)StatusPagamento.Finalizado, valor: 200m)),
            sequencial: 1, Momento);

        var credito = json.Lotes[0].Pagamentos[0].Credito;
        Assert.NotNull(credito);
        Assert.Equal("2026-08-03", credito.DataRealEfetivacao);
        Assert.Equal(200m, credito.ValorRealEfetivacao);
    }

    [Fact]
    public void Linhas_da_remessa_devem_prevalecer_sobre_as_colunas()
    {
        // O cliente concilia contra o que ele mandou; a coluna pode ter
        // sido normalizada de outro jeito.
        var segmentoA = MontarSegmentoA(nomeFavorecido: "NOME QUE VEIO NO ARQUIVO", banco: "237");

        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef, linhas: segmentoA)), sequencial: 1, Momento);

        var favorecido = json.Lotes[0].Pagamentos[0].Favorecido;
        Assert.NotNull(favorecido);
        Assert.Equal("NOME QUE VEIO NO ARQUIVO", favorecido.Nome);
        Assert.Equal("237", favorecido.Banco); // e não o "001" da coluna
    }

    [Fact]
    public void Sem_linhas_deve_cair_nas_colunas()
    {
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Tef, linhas: null)), sequencial: 1, Momento);

        var favorecido = json.Lotes[0].Pagamentos[0].Favorecido;
        Assert.NotNull(favorecido);
        Assert.Equal("FAVORECIDO EXEMPLO LTDA", favorecido.Nome);
        Assert.Equal("001", favorecido.Banco);
    }

    [Fact]
    public void Empresa_e_conta_devem_sair_dos_dados_de_debito()
    {
        var json = Montagem().Montar(Cliente(Movimentacao(MeioPagamento.Tef)), sequencial: 1, Momento);

        Assert.Equal(DocumentoCliente, json.Arquivo.Empresa.NumeroInscricao);
        Assert.Equal("CLIENTE EXEMPLO LTDA", json.Arquivo.Empresa.Nome);
        Assert.Equal("00099", json.Arquivo.Conta.Agencia);
        Assert.Equal("000000099999", json.Arquivo.Conta.Conta);
    }

    [Fact]
    public void Dia_so_de_boleto_deve_usar_a_conta_header_como_conta()
    {
        // Boleto/Tricon não têm dados de débito nas tabelas Info.
        var json = Montagem().Montar(
            Cliente(Movimentacao(MeioPagamento.Boleto, codigoBanco: "001", comDadosDeDebito: false)),
            sequencial: 1, Momento);

        Assert.Equal("0009000009", json.Arquivo.Conta.Conta);
    }

    [Fact]
    public void Nome_do_arquivo_deve_marcar_parcial_ou_consolidado()
    {
        var montagem = Montagem();

        Assert.Equal(
            "RetornoPagamento_12345678000199_03082026_180000_PARCIAL.ret",
            montagem.MontarNomeArquivo(DocumentoCliente, TipoJanelaNome.Parcial, Momento));

        Assert.Equal(
            "RetornoPagamento_12345678000199_03082026_180000_CONSOLIDADO.ret",
            montagem.MontarNomeArquivo(DocumentoCliente, TipoJanelaNome.Consolidado, Momento));
    }

    /// <summary>Segmento A de 240 posições com nome do favorecido
    /// (44-73) e banco (21-23) preenchidos.</summary>
    private static string MontarSegmentoA(string nomeFavorecido, string banco)
    {
        var linha = SegmentosRemessaTests.Linha('3', 'A').ToCharArray();

        for (var i = 0; i < 3; i++) linha[20 + i] = banco[i];
        for (var i = 0; i < nomeFavorecido.Length && i < 30; i++) linha[43 + i] = nomeFavorecido[i];

        return new string(linha);
    }
}
