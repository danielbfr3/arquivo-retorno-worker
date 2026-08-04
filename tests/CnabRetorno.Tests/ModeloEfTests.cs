using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Cobranca = CnabRetorno.RemessaVan.Worker.Persistencia;
using Pagamento = CnabRetorno.PagamentoRetorno.Worker.Persistencia;

namespace CnabRetorno.Tests;

/// <summary>
/// Constrói o modelo dos dois <c>DbContext</c> sem tocar em banco nenhum —
/// o EF só abre conexão na primeira consulta, e montar o modelo já valida
/// chaves, propriedades sem coluna e entidades sem mapeamento.
///
/// É a única rede de proteção possível aqui: as bases são de outros times
/// e não há SQL Server neste ambiente (ver docs/segunda-fonte-de-dados-sql-server.md).
/// Um erro de mapeamento sem este teste só apareceria em runtime, no
/// cluster.
/// </summary>
public class ModeloEfTests
{
    private const string ConexaoFalsa = "Server=nao-conecta;Database=x;User Id=u;Password=p;TrustServerCertificate=True";

    private static Cobranca.CobrancaDbContext Cobranca()
        => new(new DbContextOptionsBuilder<Cobranca.CobrancaDbContext>()
            .UseSqlServer(ConexaoFalsa).Options);

    private static Pagamento.PagamentoDbContext Pagamento()
        => new(new DbContextOptionsBuilder<Pagamento.PagamentoDbContext>()
            .UseSqlServer(ConexaoFalsa).Options);

    [Fact]
    public void Modelo_de_cobranca_deve_ser_valido()
    {
        using var db = Cobranca();

        var arquivo = db.Model.FindEntityType(typeof(Arquivo))!;
        Assert.Equal("Arquivo", arquivo.GetTableName());
        Assert.Equal("Cobranca", arquivo.GetSchema());
        Assert.NotNull(arquivo.FindPrimaryKey()); // é a única entidade escrita
    }

    [Fact]
    public void Parametro_de_cobranca_deve_ser_sem_chave()
    {
        using var db = Cobranca();

        var parametro = db.Model.FindEntityType(typeof(Cobranca.ParametroCliente))!;
        Assert.Null(parametro.FindPrimaryKey()); // projeção só-leitura
    }

    [Fact]
    public void Modelo_de_pagamento_deve_ser_valido()
    {
        using var db = Pagamento();

        var arquivo = db.Model.FindEntityType(typeof(Arquivo))!;
        Assert.Equal("Arquivo", arquivo.GetTableName());
        Assert.Equal("Pagamento", arquivo.GetSchema());
        Assert.NotNull(arquivo.FindPrimaryKey());
    }

    [Fact]
    public void Movimentacao_deve_ser_projecao_sem_chave_com_todas_as_colunas()
    {
        using var db = Pagamento();

        var movimentacao = db.Model.FindEntityType(typeof(MovimentacaoPagamento))!;
        Assert.Null(movimentacao.FindPrimaryKey());

        // Toda propriedade do POCO precisa estar no UNION — uma que o EF
        // mapeie mas a consulta não devolva estoura só em runtime.
        var mapeadas = movimentacao.GetProperties().Select(p => p.Name).ToHashSet();
        var doPoco = typeof(MovimentacaoPagamento).GetProperties().Select(p => p.Name);

        Assert.All(doPoco, nome => Assert.Contains(nome, mapeadas));
    }

    [Fact]
    public void Controle_de_janela_deve_ser_continuo_por_cliente()
    {
        // A marca d'água NÃO tem dimensão de dia — o dia útil do robô é
        // consolidado→consolidado, e uma chave por dia de calendário
        // deixava desfechos pós-18h sem dono.
        using var db = Pagamento();

        var controle = db.Model.FindEntityType(typeof(Pagamento.ControleJanelaRetorno))!;
        var chave = controle.FindPrimaryKey();

        Assert.NotNull(chave);
        Assert.Equal(["ClienteDocumento"], chave.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Pagamento_reportado_deve_ter_chave_por_pagamento_e_status()
    {
        // O par é a chave de propósito: mesmo pagamento com status NOVO
        // deve poder ser reportado de novo (desfecho novo); com o mesmo
        // status, a PK barra o reenvio.
        using var db = Pagamento();

        var reportado = db.Model.FindEntityType(typeof(Pagamento.PagamentoReportado))!;
        var chave = reportado.FindPrimaryKey();

        Assert.NotNull(chave);
        Assert.Equal(["PagamentoID", "CodigoStatus"], chave.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Remessa_ingerida_deve_ter_o_md5_como_chave()
    {
        using var db = Cobranca();

        var ingerida = db.Model.FindEntityType(typeof(Cobranca.RemessaIngerida))!;
        var chave = ingerida.FindPrimaryKey();

        Assert.NotNull(chave);
        Assert.Equal(["Md5"], chave.Properties.Select(p => p.Name));
    }
}
