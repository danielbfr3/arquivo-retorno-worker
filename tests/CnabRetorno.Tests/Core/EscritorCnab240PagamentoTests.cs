using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Cnab240;
using CnabRetorno.Core.Dominio;
using Xunit;

namespace CnabRetorno.Tests.Core;

public class EscritorCnab240PagamentoTests
{
    private const string DocumentoCliente = "12345678000199";

    private static EmpresaAdesao EmpresaDeExemplo(
        string? convenio = "00009999990000099999",
        string? agencia = "00099",
        string? conta = "000000099999",
        string? nome = "CLIENTE ADESAO LTDA") => new()
        {
            Documento = DocumentoCliente,
            CodigoConvenio = convenio,
            Agencia = agencia,
            DvAgencia = "9",
            Conta = conta,
            DvConta = "8",
            DvAgenciaConta = "7",
            NomeEmpresa = nome,
            Logradouro = "RUA EXEMPLO",
            NumeroEndereco = "100",
            ComplementoEndereco = "SALA 1",
            Cidade = "SAO PAULO",
            Cep = "01001",
            ComplementoCep = "000",
            Estado = "SP",
        };

    private static ArquivoPagamento ArquivoDeExemplo() => new()
    {
        Banco = "999",
        CodigoRemessaRetorno = "2",
        DataGeracao = "2026-08-03",
        HoraGeracao = "18:30:45",
        NumeroSequencialArquivo = 42,
        VersaoLayout = "103",
        Empresa = new EmpresaPagamento { TipoInscricao = "2", NumeroInscricao = DocumentoCliente, Nome = "NOME DO JSON" },
        Conta = new ContaPagamento { Agencia = "00011", Conta = "000000011111" },
    };

    private static LotePagamento LoteSegmentoA() => new()
    {
        Numero = 1,
        TipoOperacao = "C",
        TipoServico = "98",
        FormaLancamento = "01",
        VersaoLayout = "046",
        Empresa = new EmpresaPagamento { TipoInscricao = "2", NumeroInscricao = DocumentoCliente },
        Conta = new ContaPagamento { Agencia = "00011", Conta = "000000011111" },
        Ocorrencias = "00".PadRight(10),
        Pagamentos =
        [
            new DetalhePagamento
            {
                Segmento = "A",
                NumeroRegistro = 1,
                TipoMovimento = "0",
                CodigoInstrucao = "00",
                SeuNumero = "PED-0001",
                NossoNumero = "00000000000000000001",
                Ocorrencias = "00".PadRight(10),
                Favorecido = new FavorecidoPagamento
                {
                    Camara = "018",
                    Banco = "341",
                    Agencia = "01234",
                    DvAgencia = "5",
                    Conta = "000000098765",
                    DvConta = "4",
                    Nome = "FAVORECIDO EXEMPLO LTDA",
                    TipoInscricao = "2",
                    NumeroInscricao = "98765432000188",
                },
                Credito = new CreditoPagamento
                {
                    DataPagamento = "2026-08-03",
                    TipoMoeda = "BRL",
                    ValorPagamento = 150.75m,
                    DataRealEfetivacao = "2026-08-03",
                    ValorRealEfetivacao = 150.75m,
                    Informacao2 = "OBSERVACAO TESTE",
                },
            },
        ],
        Totais = new TotaisLotePagamento { QuantidadeRegistros = 4, ValorTotal = 150.75m },
    };

    private static LotePagamento LoteSegmentoJ() => new()
    {
        Numero = 2,
        TipoOperacao = "C",
        TipoServico = "98",
        FormaLancamento = "31",
        VersaoLayout = "040",
        Empresa = new EmpresaPagamento { TipoInscricao = "2", NumeroInscricao = DocumentoCliente },
        Conta = new ContaPagamento(),
        Ocorrencias = "00".PadRight(10),
        Pagamentos =
        [
            new DetalhePagamento
            {
                Segmento = "J",
                NumeroRegistro = 1,
                TipoMovimento = "0",
                CodigoInstrucao = "00",
                SeuNumero = "TIT-0001",
                NossoNumero = "00000000000000000002",
                Ocorrencias = "00".PadRight(10),
                Titulo = new TituloPagamento
                {
                    CodigoBarras = "99991234500000015075000000000000000000000001",
                    NomeBeneficiario = "BENEFICIARIO EXEMPLO",
                    TipoInscricaoBeneficiario = "2",
                    NumeroInscricaoBeneficiario = "11122233000144",
                    DataVencimento = "2026-08-10",
                    ValorTitulo = 200m,
                    ValorDesconto = 0m,
                    ValorAcrescimos = 0m,
                    DataPagamento = "2026-08-03",
                    ValorPagamento = 200m,
                    CodigoMoeda = "09",
                },
            },
            new DetalhePagamento
            {
                Segmento = "J",
                NumeroRegistro = 3,
                TipoMovimento = "0",
                CodigoInstrucao = "00",
                SeuNumero = "PIX-0001",
                NossoNumero = "00000000000000000003",
                Ocorrencias = "00".PadRight(10),
                Titulo = new TituloPagamento { ValorPagamento = 75m, DataPagamento = "2026-08-03", CodigoMoeda = "09" },
                Favorecido = new FavorecidoPagamento
                {
                    Nome = "FAVORECIDO PIX",
                    TipoInscricao = "1",
                    NumeroInscricao = "11122233344",
                    ChavePix = "favorecido@exemplo.com",
                },
            },
        ],
        Totais = new TotaisLotePagamento { QuantidadeRegistros = 6, ValorTotal = 275m },
    };

    private static RetornoPagamentoJson DadosDeExemplo() => new()
    {
        Arquivo = ArquivoDeExemplo(),
        Lotes = [LoteSegmentoA(), LoteSegmentoJ()],
        Totais = new TotaisArquivoPagamento { QuantidadeLotes = 2, QuantidadeRegistros = 12, ValorTotal = 425.75m },
    };

    private static IReadOnlyList<string> Escrever(RetornoPagamentoJson? dados = null, EmpresaAdesao? empresa = null)
        => Cnab240Campos.QuebrarLinhas(EscritorCnab240Pagamento.Escrever(dados ?? DadosDeExemplo(), empresa ?? EmpresaDeExemplo()));

    [Fact]
    public void Deve_escrever_uma_linha_por_registro_com_240_posicoes_cada()
    {
        // 1 header arquivo + (1 header lote + 2 detalhe + 1 trailer lote)
        // + (1 header lote + 4 detalhe + 1 trailer lote) + 1 trailer
        // arquivo = 12. QuebrarLinhas descarta silenciosamente qualquer
        // linha que não tenha exatamente 240 posições — a contagem exata
        // já é, portanto, uma verificação de comprimento.
        Assert.Equal(12, Escrever().Count);
    }

    [Fact]
    public void Header_de_arquivo_deve_ter_tipo_registro_zero_e_os_dados_do_json()
    {
        var header = Escrever()[0];

        Assert.Equal('0', Cnab240Campos.TipoRegistro(header));
        Assert.Equal("999", Cnab240Campos.LerTrim(header, 1, 3));
        Assert.Equal("2", Cnab240Campos.LerTrim(header, 143, 143)); // CodigoRemessaRetorno
        Assert.Equal("03082026", Cnab240Campos.LerTrim(header, 144, 151)); // DataGeracao
        Assert.Equal("183045", Cnab240Campos.LerTrim(header, 152, 157)); // HoraGeracao
        Assert.Equal(42, Cnab240Campos.LerInteiro(header, 158, 163)); // NSA
        Assert.Equal(103, Cnab240Campos.LerInteiro(header, 164, 166)); // VersaoLayout
    }

    [Fact]
    public void Header_de_arquivo_deve_priorizar_dados_institucionais_da_empresa_adesao()
    {
        // O nome do JSON (best-effort, vindo das movimentações) NÃO pode
        // vencer o nome institucional — é o ponto central do modo
        // CnabDireto.
        var header = Escrever()[0];

        Assert.Equal("CLIENTE ADESAO LTDA", Cnab240Campos.LerTrim(header, 73, 102));
        Assert.Equal("00009999990000099999", Cnab240Campos.LerTrim(header, 33, 52)); // Convênio
        Assert.Equal("00099", Cnab240Campos.LerTrim(header, 53, 57)); // Agência
        Assert.Equal("000000099999", Cnab240Campos.LerTrim(header, 59, 70)); // Conta
    }

    [Fact]
    public void Sem_agencia_na_empresa_adesao_deve_cair_na_conta_do_json()
    {
        var empresa = EmpresaDeExemplo(agencia: null, conta: null);
        var header = Escrever(empresa: empresa)[0];

        // ArquivoPagamento.Conta no fixture tem Agencia="00011", Conta="000000011111".
        Assert.Equal("00011", Cnab240Campos.LerTrim(header, 53, 57));
        Assert.Equal("000000011111", Cnab240Campos.LerTrim(header, 59, 70));
    }

    [Fact]
    public void Todas_as_linhas_devem_ter_o_mesmo_codigo_de_banco()
    {
        // O código do banco (posições 1-3) se repete em toda linha do
        // arquivo, não só no header — é o bug que a primeira versão deste
        // escritor tinha (banco em branco fora do header/trailer arquivo).
        var linhas = Escrever();

        Assert.All(linhas, l => Assert.Equal("999", Cnab240Campos.LerTrim(l, 1, 3)));
    }

    [Fact]
    public void Trailer_de_arquivo_deve_ter_tipo_registro_nove_e_os_totais()
    {
        var trailer = Escrever()[^1];

        Assert.Equal('9', Cnab240Campos.TipoRegistro(trailer));
        Assert.Equal(9999, Cnab240Campos.LerInteiro(trailer, 4, 7)); // Lote 9999
        Assert.Equal(2, Cnab240Campos.LerInteiro(trailer, 18, 23)); // QuantidadeLotes
        Assert.Equal(12, Cnab240Campos.LerInteiro(trailer, 24, 29)); // QuantidadeRegistros
    }

    [Fact]
    public void Header_de_lote_deve_ter_a_forma_de_lancamento_e_o_endereco_da_empresa()
    {
        var linhas = Escrever();
        var headerLote1 = linhas[1]; // primeira linha depois do header de arquivo

        Assert.Equal('1', Cnab240Campos.TipoRegistro(headerLote1));
        Assert.Equal(1, Cnab240Campos.LerInteiro(headerLote1, 4, 7)); // número do lote
        Assert.Equal("01", Cnab240Campos.LerTrim(headerLote1, 12, 13)); // FormaLancamento
        Assert.Equal("046", Cnab240Campos.LerTrim(headerLote1, 14, 16)); // VersaoLayout
        Assert.Equal("RUA EXEMPLO", Cnab240Campos.LerTrim(headerLote1, 143, 172));
        Assert.Equal("SAO PAULO", Cnab240Campos.LerTrim(headerLote1, 193, 212));
        Assert.Equal("SP", Cnab240Campos.LerTrim(headerLote1, 221, 222));
    }

    [Fact]
    public void Trailer_de_lote_deve_ter_tipo_registro_cinco_e_o_valor_total()
    {
        var linhas = Escrever();
        // Lote 1: header(1) + A(2) + B(3) + trailer(4) — a linha 4 (índice 3).
        var trailerLote1 = linhas[4];

        Assert.Equal('5', Cnab240Campos.TipoRegistro(trailerLote1));
        Assert.Equal(4, Cnab240Campos.LerInteiro(trailerLote1, 18, 23));
        Assert.Equal(150.75m, Cnab240Campos.LerValor(trailerLote1, 24, 41));
    }

    [Fact]
    public void Segmento_A_deve_carregar_favorecido_e_valores()
    {
        var linhas = Escrever();
        var segmentoA = linhas[2]; // header arquivo(0), header lote(1), A(2)

        Assert.Equal('3', Cnab240Campos.TipoRegistro(segmentoA));
        Assert.Equal('A', Cnab240Campos.Segmento(segmentoA));
        Assert.Equal(1, Cnab240Campos.LerInteiro(segmentoA, 9, 13)); // NumeroRegistro
        Assert.Equal("341", Cnab240Pagamento.SegmentoA.FavorecidoBanco(segmentoA));
        Assert.Equal("FAVORECIDO EXEMPLO LTDA", Cnab240Pagamento.SegmentoA.FavorecidoNome(segmentoA));
        Assert.Equal(150.75m, Cnab240Pagamento.SegmentoA.ValorPagamento(segmentoA));
    }

    [Fact]
    public void Segmento_B_deve_seguir_o_A_com_numero_de_registro_seguinte()
    {
        var linhas = Escrever();
        var segmentoB = linhas[3];

        Assert.Equal('3', Cnab240Campos.TipoRegistro(segmentoB));
        Assert.Equal('B', Cnab240Campos.Segmento(segmentoB));
        Assert.Equal(2, Cnab240Campos.LerInteiro(segmentoB, 9, 13));
        Assert.Equal("2", Cnab240Pagamento.SegmentoB.TipoInscricao(segmentoB));
        Assert.Equal("98765432000188", Cnab240Pagamento.SegmentoB.NumeroInscricao(segmentoB));
    }

    [Fact]
    public void Segmento_J_de_titulo_deve_carregar_codigo_de_barras_e_valores()
    {
        var linhas = Escrever();
        // header arquivo(0), lote1[header,A,B,trailer]=1..4, lote2 header(5), J(6)
        var segmentoJ = linhas[6];

        Assert.Equal('3', Cnab240Campos.TipoRegistro(segmentoJ));
        Assert.Equal('J', Cnab240Campos.Segmento(segmentoJ));
        Assert.False(Cnab240Pagamento.SegmentoJ.EhRegistroOpcional(segmentoJ));
        Assert.Equal("99991234500000015075000000000000000000000001", Cnab240Pagamento.SegmentoJ.CodigoBarras(segmentoJ));
        Assert.Equal(200m, Cnab240Pagamento.SegmentoJ.ValorTitulo(segmentoJ));
        Assert.Equal("10082026", Cnab240Campos.LerTrim(segmentoJ, 92, 99)); // DataVencimento
    }

    [Fact]
    public void J52_de_titulo_deve_ter_o_beneficiario_e_o_pagador_institucional()
    {
        var linhas = Escrever();
        var j52 = linhas[7]; // logo depois do J do título

        Assert.Equal('3', Cnab240Campos.TipoRegistro(j52));
        Assert.Equal('J', Cnab240Campos.Segmento(j52));
        Assert.True(Cnab240Pagamento.SegmentoJ.EhRegistroOpcional(j52));
        Assert.Equal("2", Cnab240Campos.LerTrim(j52, 20, 20)); // tipo pagador — CNPJ
        // 21-35 tem 15 posições — uma a mais que o campo de 14 do header
        // e do segmento B — por isso o CNPJ de 14 dígitos sai com um zero
        // à esquerda aqui, e não é erro do escritor.
        Assert.Equal($"0{DocumentoCliente}", Cnab240Campos.LerTrim(j52, 21, 35));
        Assert.Equal("CLIENTE ADESAO LTDA", Cnab240Campos.LerTrim(j52, 36, 75));
        Assert.Equal("BENEFICIARIO EXEMPLO", Cnab240Pagamento.SegmentoJ52.BeneficiarioNome(j52));
        // Mesmo detalhe de largura (15 posições) do comentário acima.
        Assert.Equal("011122233000144", Cnab240Pagamento.SegmentoJ52.BeneficiarioNumeroInscricao(j52));
    }

    [Fact]
    public void J_de_pix_qrcode_deve_ser_seguido_por_j52_pix_com_a_chave()
    {
        var linhas = Escrever();
        // lote2: header(5), J titulo(6), J52 titulo(7), J pix(8), J52 pix(9)
        var jPix = linhas[8];
        var j52Pix = linhas[9];

        Assert.Equal('J', Cnab240Campos.Segmento(jPix));
        Assert.Equal(75m, Cnab240Pagamento.SegmentoJ.ValorPagamento(jPix));

        Assert.True(Cnab240Pagamento.SegmentoJ.EhRegistroOpcional(j52Pix));
        Assert.Equal("FAVORECIDO PIX", Cnab240Campos.LerTrim(j52Pix, 92, 131)); // nome do favorecido
        Assert.Equal("favorecido@exemplo.com", Cnab240Pagamento.SegmentoJ52.ChavePix(j52Pix));
    }

    [Fact]
    public void Pagamento_nao_efetivado_deve_escrever_data_real_zerada()
    {
        var dados = DadosDeExemplo() with
        {
            Lotes =
            [
                LoteSegmentoA() with
                {
                    Pagamentos =
                    [
                        LoteSegmentoA().Pagamentos[0] with
                        {
                            Credito = LoteSegmentoA().Pagamentos[0].Credito! with
                            {
                                DataRealEfetivacao = null,
                                ValorRealEfetivacao = 0m,
                            },
                        },
                    ],
                },
            ],
        };

        var segmentoA = Escrever(dados)[2];

        Assert.Equal("00000000", Cnab240Campos.LerTrim(segmentoA, 155, 162));
        Assert.Equal(0m, Cnab240Campos.LerValor(segmentoA, 163, 177));
    }

    [Fact]
    public void Sem_empresa_deve_falhar_alto_em_vez_de_escrever_header_incompleto()
        => Assert.Throws<ArgumentNullException>(() => EscritorCnab240Pagamento.Escrever(DadosDeExemplo(), null!));

    [Fact]
    public void Sem_lote_nenhum_deve_falhar()
        => Assert.Throws<ArgumentException>(
            () => EscritorCnab240Pagamento.Escrever(DadosDeExemplo() with { Lotes = [] }, EmpresaDeExemplo()));

    [Fact]
    public void Segmento_A_sem_favorecido_deve_falhar_em_vez_de_escrever_linha_vazia()
    {
        var dados = DadosDeExemplo() with
        {
            Lotes = [LoteSegmentoA() with { Pagamentos = [LoteSegmentoA().Pagamentos[0] with { Favorecido = null }] }],
        };

        Assert.Throws<ArgumentException>(() => EscritorCnab240Pagamento.Escrever(dados, EmpresaDeExemplo()));
    }
}
