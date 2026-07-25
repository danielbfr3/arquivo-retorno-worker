using CnabRetorno.Core.Dominio;
using CnabRetorno.RetornoCron.Worker.Json;
using Xunit;

namespace CnabRetorno.Tests.RetornoCron.Json;

public class PendenciasParaTitulosConvertidosFactoryTests
{
    private static Titulo TituloDeExemplo(string? codigoOcorrencia = "06") => new()
    {
        TituloID = Guid.NewGuid(),
        ClienteDocumento = "12345678000199",
        CodigoStatus = -1,
        DataAtualizacao = DateTime.UtcNow,
        ClienteTipoDocumento = 2,
        ClienteContaHeader = "000900000900",
        CodigoOcorrencia = codigoOcorrencia,
        DescricaoOcorrencia = codigoOcorrencia is null ? null : "Liquidação",
        NumeroCarteira = "2",
        CodigoBanco = "999",
        CodigoModalidade = "000",
        NossoNumero = "00000000001",
        NossoNumeroCorrespondente = "0000000000000",
        SeuNumero = "000001/01",
        ValorNominal = 17659.5m,
        DataVencimento = new DateOnly(2026, 7, 9),
        CampoLivre = "000001/0001",
        CodigoIndice = "09",
        SacadorAvalistaTipoDocumento = 2,
        SacadorAvalistaDocumento = "98765432000188",
        SacadorAvalistaNome = "CONFECCOES EXEMPLO LTDA",
        RegistroRetornoCodBanco = "001",
        RegistroRetornoCodAgenciaCob = "00001",
    };

    private static InstrucaoComTitulo InstrucaoComTituloDeExemplo(
        string? codigoOcorrencia = "06", bool comTituloCasado = true) => new()
    {
        InstrucaoID = Guid.NewGuid(),
        ClienteDocumento = "12345678000199",
        CodigoStatus = -1,
        DataAtualizacao = DateTime.UtcNow,
        ClienteTipoDocumento = 2,
        ClienteContaHeader = "000900000900",
        NossoNumero = "123",
        CodigoOcorrencia = codigoOcorrencia,
        TituloID = comTituloCasado ? Guid.NewGuid() : null,
        TituloNumeroCarteira = comTituloCasado ? "2" : null,
        CodigoBanco = comTituloCasado ? "123" : null,
        SeuNumero = comTituloCasado ? "123" : null,
        ValorNominal = comTituloCasado ? 1.0m : null,
        DataVencimento = comTituloCasado ? new DateOnly(2026, 10, 10) : null,
        CodigoIndice = comTituloCasado ? "1" : null,
        SacadorAvalistaTipoDocumento = comTituloCasado ? (short)2 : null,
        SacadorAvalistaDocumento = comTituloCasado ? "11111111111111" : null,
        SacadorAvalistaNome = comTituloCasado ? "Teste LTDA" : null,
        RegistroRetornoCodBanco = comTituloCasado ? "1" : null,
        RegistroRetornoCodAgenciaCob = comTituloCasado ? "1" : null,
    };

    [Fact]
    public void ConverterTitulo_deve_mapear_cliente_e_sacado()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal("000900000900", convertido.Cliente.ContaHeader);
        Assert.Equal("2", convertido.Cliente.Documento.Codigo);
        Assert.Equal("12345678000199", convertido.Cliente.Documento.Inscricao);
        Assert.Null(convertido.Cliente.Documento.Tipo);
        Assert.Equal("98765432000188", convertido.Sacado.Documento.Inscricao);
        Assert.Equal("CONFECCOES EXEMPLO LTDA", convertido.Sacado.Nome);
    }

    [Fact]
    public void ConverterTitulo_deve_mapear_valor_carteira_e_vencimento()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal(17659.5m, convertido.ValorNominal);
        Assert.Equal("2", convertido.NumeroCarteira);
        Assert.Equal("2026-07-09", convertido.DataVencimento);
        Assert.Equal("000001/01", convertido.SeuNumero);
    }

    [Fact]
    public void ConverterTitulo_deve_usar_cobrador_do_registro_retorno()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal("001", convertido.Cobrador!.Banco);
        Assert.Equal("00001", convertido.Cobrador.Agencia);
    }

    [Fact]
    public void ConverterTitulo_deve_usar_ocorrencia_do_titulo()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal("06", convertido.Ocorrencia!.Codigo);
        Assert.Equal("Liquidação", convertido.Ocorrencia.Descricao);
    }

    [Fact]
    public void ConverterTitulo_deve_usar_codigo_padrao_quando_titulo_sem_ocorrencia()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo(codigoOcorrencia: null));

        Assert.Equal("03", convertido.Ocorrencia!.Codigo); // Entrada Rejeitada — padrão
    }

    [Fact]
    public void ConverterTitulo_motivos_deve_ser_fixo()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal("0000000000", convertido.Motivos);
    }

    [Fact]
    public void ConverterTitulo_campos_fixos_devem_bater_com_o_exemplo_real()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterTitulo(TituloDeExemplo());

        Assert.Equal("0", convertido.DirecionamentoCobranca);
        Assert.Equal("00", convertido.UsoExclusivoBanco);
        Assert.Equal("112", convertido.ModalidadeComBancoCedente);
        Assert.Equal("0000", convertido.AlegacaoSacado!.Codigo);
        Assert.Equal("00000000", convertido.AlegacaoSacado.Data);
    }

    [Fact]
    public void ConverterInstrucao_deve_usar_numeroCarteira_do_titulo_casado_nao_da_instrucao()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterInstrucao(InstrucaoComTituloDeExemplo());

        Assert.Equal("2", convertido.NumeroCarteira); // TituloNumeroCarteira, não a carteira da instrução
    }

    [Fact]
    public void ConverterInstrucao_deve_usar_codigo_movimento_padrao_26_quando_sem_ocorrencia()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterInstrucao(
            InstrucaoComTituloDeExemplo(codigoOcorrencia: null));

        Assert.Equal("26", convertido.Ocorrencia!.Codigo); // Instrução Rejeitada — padrão
    }

    [Fact]
    public void ConverterInstrucao_deve_degradar_com_titulo_nao_encontrado()
    {
        var convertido = PendenciasParaTitulosConvertidosFactory.ConverterInstrucao(
            InstrucaoComTituloDeExemplo(comTituloCasado: false));

        Assert.Equal(0m, convertido.ValorNominal);
        Assert.Null(convertido.NumeroCarteira);
        Assert.Null(convertido.Sacado.Nome);
    }
}
