using CnabRetorno.Core.Dominio;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CnabRetorno.Tests;

/// <summary>
/// Constrói o modelo dos dois <c>DbContext</c> sem tocar em banco nenhum —
/// o EF só abre conexão na primeira consulta, e montar o modelo já valida
/// chaves, propriedades sem coluna e entidades sem mapeamento.
///
/// É a única rede de proteção possível aqui: as duas bases são de outros
/// times e não há SQL Server neste ambiente (ver
/// docs/segunda-fonte-de-dados-sql-server.md). Um erro de mapeamento sem
/// este teste só apareceria em runtime, no cluster.
/// </summary>
public class ModeloEfTests
{
    private const string ConexaoFalsa = "Server=nao-conecta;Database=x;User Id=u;Password=p;TrustServerCertificate=True";

    private static CobrancaDbContext Cobranca()
        => new(new DbContextOptionsBuilder<CobrancaDbContext>().UseSqlServer(ConexaoFalsa).Options);

    private static AdesaoDbContext Adesao()
        => new(new DbContextOptionsBuilder<AdesaoDbContext>().UseSqlServer(ConexaoFalsa).Options);

    [Fact]
    public void Arquivo_deve_estar_mapeado_em_Cobranca_com_chave()
    {
        using var db = Cobranca();

        var arquivo = db.Model.FindEntityType(typeof(Arquivo))!;
        Assert.Equal("Arquivo", arquivo.GetTableName());
        Assert.Equal("Cobranca", arquivo.GetSchema());
        Assert.NotNull(arquivo.FindPrimaryKey()); // é a entidade escrita
    }

    [Fact]
    public void Empresa_adesao_deve_ter_o_documento_como_chave()
    {
        // TODO(a-confirmar): schema e tabela são placeholder — este teste
        // trava o mapeamento atual pra que a troca pelo schema real seja
        // uma mudança visível, não um efeito colateral.
        using var db = Adesao();

        var empresa = db.Model.FindEntityType(typeof(EmpresaAdesao))!;
        var chave = empresa.FindPrimaryKey();

        Assert.NotNull(chave);
        Assert.Equal(["Documento"], chave.Properties.Select(p => p.Name));
        Assert.Equal("Empresa", empresa.GetTableName());
        Assert.Equal("Adesao", empresa.GetSchema());
    }

    [Fact]
    public void Razao_social_deve_estar_mapeada()
    {
        // Sem ela não há o que mandar no corpo da mensagem — é o único
        // dado que só existe na base de adesão.
        using var db = Adesao();

        var empresa = db.Model.FindEntityType(typeof(EmpresaAdesao))!;

        Assert.NotNull(empresa.FindProperty(nameof(EmpresaAdesao.RazaoSocial)));
    }

    [Fact]
    public void DocumentoDados_deve_estar_mapeado_em_Cobranca_com_o_documento_como_chave()
    {
        // TODO(a-confirmar): schema é placeholder, mesma ressalva de
        // Core.Dominio.DocumentoDados.
        using var db = Cobranca();

        var documentoDados = db.Model.FindEntityType(typeof(DocumentoDados))!;
        var chave = documentoDados.FindPrimaryKey();

        Assert.NotNull(chave);
        Assert.Equal(["NumeroDocumento"], chave.Properties.Select(p => p.Name));
        Assert.Equal("DocumentoDados", documentoDados.GetTableName());
        Assert.Equal("Cobranca", documentoDados.GetSchema());
        Assert.NotNull(documentoDados.FindProperty(nameof(DocumentoDados.Dados)));
    }
}
