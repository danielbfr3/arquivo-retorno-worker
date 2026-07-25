using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.RetornoCron.Worker.Json;
using Xunit;

namespace CnabRetorno.Tests.RetornoCron.Json;

public class MesclagemDadosConvertidosTests
{
    private readonly MesclagemDadosConvertidos _mesclagem = new();

    private static DadosConvertidos DadosDeExemplo(
        string banco = "999", string inscricaoEmpresa = "12345678000199", string conta = "000900000900",
        decimal valorTotal = 100m, int quantidadeTitulos = 1)
    {
        var empresa = new EmpresaConvertida { TipoInscricao = "2", NumeroInscricao = inscricaoEmpresa };
        var contaConvertida = new ContaConvertida { Agencia = "00001", Conta = conta };

        return new DadosConvertidos
        {
            Arquivo = new ArquivoConvertido { Banco = banco, Empresa = empresa, Conta = contaConvertida },
            Lote = new LoteConvertido { Empresa = empresa, Conta = contaConvertida },
            Titulos = Enumerable.Range(0, quantidadeTitulos).Select(TituloDeExemplo).ToList(),
            Totais = new TotaisConvertidos { Titulos = quantidadeTitulos, ValorTotalCobrancaSimples = valorTotal },
        };
    }

    private static TituloConvertido TituloDeExemplo(int numeroRegistro) => new()
    {
        Cliente = new ClienteConvertido { Documento = new DocumentoConvertido { Inscricao = "12345678000199" } },
        Sacado = new SacadoConvertido { Documento = new DocumentoConvertido() },
        NumeroRegistro = numeroRegistro,
    };

    [Fact]
    public void Mesclar_sem_pv_deve_manter_so_os_titulos_de_v()
    {
        var v = DadosDeExemplo(quantidadeTitulos: 2);

        var resultado = _mesclagem.Mesclar(v, null, []);

        Assert.Equal(2, resultado.Titulos.Count);
    }

    [Fact]
    public void Mesclar_deve_concatenar_v_pv_e_pendencias_nessa_ordem()
    {
        var v = DadosDeExemplo(quantidadeTitulos: 1);
        var pv = DadosDeExemplo(quantidadeTitulos: 1);
        var pendencia = TituloDeExemplo(0);

        var resultado = _mesclagem.Mesclar(v, pv, [pendencia]);

        Assert.Equal(3, resultado.Titulos.Count);
    }

    [Fact]
    public void Mesclar_deve_renumerar_sequencialmente_1_3_5()
    {
        var v = DadosDeExemplo(quantidadeTitulos: 1);
        var pv = DadosDeExemplo(quantidadeTitulos: 1);
        var pendencia = TituloDeExemplo(999); // número original deve ser descartado

        var resultado = _mesclagem.Mesclar(v, pv, [pendencia]);

        Assert.Equal([1, 3, 5], resultado.Titulos.Select(t => t.NumeroRegistro));
    }

    [Fact]
    public void Mesclar_totais_deve_somar_valor_de_v_e_pv()
    {
        var v = DadosDeExemplo(valorTotal: 100m, quantidadeTitulos: 1);
        var pv = DadosDeExemplo(valorTotal: 50m, quantidadeTitulos: 1);

        var resultado = _mesclagem.Mesclar(v, pv, []);

        Assert.Equal(150m, resultado.Totais.ValorTotalCobrancaSimples);
    }

    [Fact]
    public void Mesclar_pendencia_nao_deve_contribuir_valor_ao_total()
    {
        var v = DadosDeExemplo(valorTotal: 100m, quantidadeTitulos: 1);
        var pendencia = TituloDeExemplo(0);
        pendencia = pendencia with { ValorNominal = 500m };

        var resultado = _mesclagem.Mesclar(v, null, [pendencia]);

        Assert.Equal(100m, resultado.Totais.ValorTotalCobrancaSimples);
    }

    [Fact]
    public void Mesclar_resultado_deve_usar_arquivo_e_lote_de_v()
    {
        var v = DadosDeExemplo(banco: "999");
        var pv = DadosDeExemplo(banco: "999");

        var resultado = _mesclagem.Mesclar(v, pv, []);

        Assert.Same(v.Arquivo, resultado.Arquivo);
        Assert.Same(v.Lote, resultado.Lote);
    }

    [Fact]
    public void Mesclar_banco_divergente_deve_lancar_excecao()
    {
        var v = DadosDeExemplo(banco: "999");
        var pv = DadosDeExemplo(banco: "001");

        Assert.Throws<DadosConvertidosDivergentesException>(() => _mesclagem.Mesclar(v, pv, []));
    }

    [Fact]
    public void Mesclar_empresa_divergente_deve_lancar_excecao()
    {
        var v = DadosDeExemplo(inscricaoEmpresa: "12345678000199");
        var pv = DadosDeExemplo(inscricaoEmpresa: "11111111000199");

        Assert.Throws<DadosConvertidosDivergentesException>(() => _mesclagem.Mesclar(v, pv, []));
    }

    [Fact]
    public void Mesclar_conta_divergente_deve_lancar_excecao()
    {
        var v = DadosDeExemplo(conta: "000900000900");
        var pv = DadosDeExemplo(conta: "999999999999");

        Assert.Throws<DadosConvertidosDivergentesException>(() => _mesclagem.Mesclar(v, pv, []));
    }

    [Fact]
    public void Mesclar_cabecalho_identico_nao_deve_lancar_excecao()
    {
        var v = DadosDeExemplo();
        var pv = DadosDeExemplo();

        var resultado = _mesclagem.Mesclar(v, pv, []);

        Assert.NotNull(resultado);
    }

    [Fact]
    public void AplicarSequencial_deve_escrever_nos_dois_campos_de_sequencial()
    {
        // Os dois campos precisam bater: o CNAB carrega o mesmo número no
        // header de arquivo e no header de lote.
        var dados = DadosDeExemplo();

        var resultado = _mesclagem.AplicarSequencial(dados, 517);

        Assert.Equal(517, resultado.Arquivo.NumeroSequencialArquivo);
        Assert.Equal(517, resultado.Lote.NumeroRemessaRetorno);
    }

    [Fact]
    public void AplicarSequencial_deve_sobrescrever_o_sequencial_que_veio_do_v()
    {
        // O V traz o sequencial da REMESSA — tem que ser descartado, não
        // reaproveitado (é justamente o bug que o controle resolve).
        var dados = DadosDeExemplo() with
        {
            Arquivo = DadosDeExemplo().Arquivo with { NumeroSequencialArquivo = 2 },
            Lote = DadosDeExemplo().Lote with { NumeroRemessaRetorno = 2 },
        };

        var resultado = _mesclagem.AplicarSequencial(dados, 999);

        Assert.Equal(999, resultado.Arquivo.NumeroSequencialArquivo);
        Assert.Equal(999, resultado.Lote.NumeroRemessaRetorno);
    }

    [Fact]
    public void AplicarSequencial_nao_deve_mexer_em_mais_nada()
    {
        var dados = DadosDeExemplo(banco: "999", quantidadeTitulos: 2, valorTotal: 250m);

        var resultado = _mesclagem.AplicarSequencial(dados, 42);

        Assert.Equal("999", resultado.Arquivo.Banco);
        Assert.Equal("12345678000199", resultado.Arquivo.Empresa.NumeroInscricao);
        Assert.Equal(2, resultado.Titulos.Count);
        Assert.Equal(250m, resultado.Totais.ValorTotalCobrancaSimples);
    }

    [Fact]
    public void AplicarSequencial_deve_funcionar_sobre_o_sintetico()
    {
        // Sem V de origem o sintético nasce com 0 nos dois campos — é o
        // caso em que o controle é indispensável.
        var header = new HeaderSintetico(Banco: "000", Cnpj: "12345678000199", NomeEmpresa: "Cliente Teste");
        var sintetico = _mesclagem.MontarSintetico(header, [TituloDeExemplo(0)]);
        Assert.Equal(0, sintetico.Lote.NumeroRemessaRetorno);

        var resultado = _mesclagem.AplicarSequencial(sintetico, 7);

        Assert.Equal(7, resultado.Arquivo.NumeroSequencialArquivo);
        Assert.Equal(7, resultado.Lote.NumeroRemessaRetorno);
    }

    [Fact]
    public void MontarSintetico_deve_gerar_dados_validos_so_com_pendencias()
    {
        var header = new HeaderSintetico(Banco: "000", Cnpj: "12345678000199", NomeEmpresa: "Cliente Teste");
        var pendencias = new List<TituloConvertido> { TituloDeExemplo(0), TituloDeExemplo(0) };

        var resultado = _mesclagem.MontarSintetico(header, pendencias);

        Assert.Equal(2, resultado.Titulos.Count);
        Assert.Equal([1, 3], resultado.Titulos.Select(t => t.NumeroRegistro));
        Assert.Equal("000", resultado.Arquivo.Banco);
        Assert.Equal("12345678000199", resultado.Arquivo.Empresa.NumeroInscricao);
        Assert.Equal(0m, resultado.Totais.ValorTotalCobrancaSimples);
    }
}
